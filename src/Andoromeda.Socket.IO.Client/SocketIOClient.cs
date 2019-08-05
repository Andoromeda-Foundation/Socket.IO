using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Andoromeda.Socket.IO.Client
{
    public sealed class SocketIOClient : IDisposable
    {
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        private readonly byte[] _engineIOPacketBuffer = new byte[1];
        private readonly byte[] _socketIOPacketTypeBuffer = new byte[1];

        private ClientWebSocket _socket;
        private SocketIOEventStream _eventStream;

        public bool IsConnected { get; private set; }

        private Channel<EngineIOPacket> _sendChannel;
        private Timer _timer;
        private Task _coreIOTask;

        public event Action<SocketIOClient> Connected;
        public event Action<SocketIOEvent> EventReceived;

        public SocketIOClient(string baseUrl, HttpClient httpClient)
        {
            _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #region IDisposable implementation
        private volatile int _isDisposed;
        public bool IsDisposed => _isDisposed != 0;

        ~SocketIOClient() => Dispose(false);
        public void Dispose()
        {
            if (_isDisposed != 0 || Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
                return;

            try { Dispose(true); }
            finally { GC.SuppressFinalize(this); }
        }
        private void Dispose(bool disposing)
        {
            _timer?.Dispose();
            _eventStream?.Dispose();
            _socket?.Dispose();

            _socket = null;
            _eventStream = null;
            _timer = null;
            _sendChannel = null;
            _coreIOTask = null;
        }
        #endregion

        public ValueTask ConnectAsync() => ConnectAsync(null);
        public async ValueTask ConnectAsync(ConnectionOptions options)
        {
            ConnectionInfo info;

            if (options is null || !options.NoLongPollingConnection)
                info = await EstablishNormally().ConfigureAwait(false);
            else
                info = await EstablishWebsocketConnectionDirectly().ConfigureAwait(false);

            _eventStream = new SocketIOEventStream(_socket);

            _sendChannel = Channel.CreateUnbounded<EngineIOPacket>(new UnboundedChannelOptions()
            {
                SingleReader = true,
            });

            Start();

            _timer = new Timer(delegate
            {
                _sendChannel.Writer.TryWrite(EngineIOPacket.Ping);
            }, null, info.PingInterval, info.PingInterval);

            IsConnected = true;
            Connected?.Invoke(this);
        }
        private async ValueTask<ConnectionInfo> EstablishNormally()
        {
            var builder = new UriBuilder(_baseUrl);

            builder.Path = "/socket.io/";
            builder.Query = "EIO=3&transport=polling&b64=1&t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            ConnectionInfo info;
            using (var response = await _httpClient.GetAsync(builder.Uri).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                info = ParseConnectionInfo(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            }

            if (!Array.Exists(info.Upgrades, r => r.Equals("websocket", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This library supports WebSocket only.");

            builder.Scheme = "ws";
            builder.Query = "EIO=3&transport=websocket&sid=" + info.SocketId;
            await EstablishWebsocketConnection(builder.Uri).ConfigureAwait(false);

            await SendEngineIOPingProbeAsync().ConfigureAwait(false);

            if (await ReceiveEngineIOPacketAsync().ConfigureAwait(false) != EngineIOPacket.PongProbe)
                throw new InvalidOperationException();

            await SendEngineIOPacketAsync(EngineIOPacket.Upgrade).ConfigureAwait(false);

            return info;

            static ConnectionInfo ParseConnectionInfo(ReadOnlySpan<byte> content)
            {
                if (!Utf8Parser.TryParse(content, out int length, out var consumed))
                    Utils.ThrowParseException();

                content = content.Slice(consumed + 1);
                var info = ConnectionInfo.Parse(content.Slice(0, length));
                content = content.Slice(length);

                if (!content.IsEmpty)
                {
                    if (!Utf8Parser.TryParse(content, out length, out consumed) || length != 2)
                        Utils.ThrowParseException();

                    content = content.Slice(consumed + 1);

                    if (!Utf8Parser.TryParse(content, out int message, out _) || message != 40)
                        Utils.ThrowParseException();
                }

                return info;
            }
        }

        private async ValueTask<ConnectionInfo> EstablishWebsocketConnectionDirectly()
        {
            var builder = new UriBuilder(_baseUrl)
            {
                Scheme = "ws",
                Path = "/socket.io/",
                Query = "EIO=3&transport=websocket",
            };
            await EstablishWebsocketConnection(builder.Uri).ConfigureAwait(false);

            var array = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
#if NETSTANDARD2_1
                Memory<byte> buffer = array;
                var receiveResult = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

                var info = ConnectionInfo.Parse(buffer.Span[..receiveResult.Count]);
#else
                var buffer = new ArraySegment<byte>(array);
                var receiveResult = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

                var info = ConnectionInfo.Parse(buffer.AsSpan().Slice(0, receiveResult.Count));
#endif

                receiveResult = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);
                if (receiveResult.Count != 2)
                    throw new InvalidOperationException();

#if NETSTANDARD2_1
                if (!Is40(buffer.Span))
#else
                if (!Is40(buffer))
#endif
                    throw new InvalidOperationException();

                return info;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }

            static bool Is40(ReadOnlySpan<byte> span) =>
                span[0] == '4' && span[1] == '0';
        }

        private async ValueTask EstablishWebsocketConnection(Uri uri)
        {
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.Zero;

            await socket.ConnectAsync(uri, default).ConfigureAwait(false);

            _socket = socket;
        }

        ValueTask SendEngineIOPacketAsync(EngineIOPacket packet)
        {
            _engineIOPacketBuffer[0] = (byte)packet.Type;

#if NETSTANDARD2_1
            var buffer = _engineIOPacketBuffer.AsMemory();

            return _socket.SendAsync(buffer, WebSocketMessageType.Text, true, default);
#else
            var buffer = new ArraySegment<byte>(_engineIOPacketBuffer);

            return new ValueTask(_socket.SendAsync(buffer, WebSocketMessageType.Text, true, default));
#endif
        }
        ValueTask SendEngineIOPingProbeAsync()
        {
#if NETSTANDARD2_1
            var buffer = new[] { (byte)'2', (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' }.AsMemory();

            return _socket.SendAsync(buffer, WebSocketMessageType.Text, true, default);
#else
            var buffer = new ArraySegment<byte>(new[] { (byte)'2', (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' });

            return new ValueTask(_socket.SendAsync(buffer, WebSocketMessageType.Text, true, default));
#endif
        }
        async ValueTask<EngineIOPacket> ReceiveEngineIOPacketAsync()
        {
#if NETSTANDARD2_1
            var buffer = _engineIOPacketBuffer.AsMemory();
#else
            var buffer = new ArraySegment<byte>(_engineIOPacketBuffer);
#endif

            var receiveResult = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);
            if (receiveResult.MessageType == WebSocketMessageType.Close)
                return null;

            switch ((EngineIOPacketType)_engineIOPacketBuffer[0])
            {
                case EngineIOPacketType.Pong when receiveResult.EndOfMessage:
                    return EngineIOPacket.Pong;

                case EngineIOPacketType.Pong:
                    var bufferArray = new byte[5];
#if NETSTANDARD2_1
                    buffer = bufferArray;
#else
                    buffer = new ArraySegment<byte>(bufferArray);
#endif
                    receiveResult = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

                    if (IsProbe(bufferArray))
                        return EngineIOPacket.PongProbe;

                    throw new InvalidOperationException();

                case EngineIOPacketType.Message:
                    return EngineIOPacket.Message;

                default:
                    throw new InvalidOperationException();
            }

            static bool IsProbe(ReadOnlySpan<byte> span)
            {
                Span<byte> probe = stackalloc byte[5] { (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' };

                return MemoryExtensions.SequenceEqual(span, probe);
            }
        }

        void Start() => _coreIOTask = Task.WhenAll(SendLoop(), ReceiveLoop());

        async Task SendLoop()
        {
            while (true)
            {
                var packet = await _sendChannel.Reader.ReadAsync().ConfigureAwait(false);

                if (packet == EngineIOPacket.Ping)
                {
                    await SendEngineIOPacketAsync(packet).ConfigureAwait(false);
                    Debug.WriteLine("[Ping]");
                    continue;
                }

                if (packet is SocketIOEventPacket eventPacket)
                {
                    using (_eventStream.RentBuffer())
                    {
#if NETSTANDARD2_1
                        await _eventStream.WriteAsync(new[] { (byte)'4', (byte)'2' }, default).ConfigureAwait(false);
#else
                        await _eventStream.WriteAsync(new[] { (byte)'4', (byte)'2' }, 0, 2, default).ConfigureAwait(false);
#endif
                        await JsonSerializer.SerializeAsync(_eventStream, eventPacket.Event).ConfigureAwait(false);
                        await _eventStream.FlushAsync().ConfigureAwait(false);
                    }
                    continue;
                }

                if (packet == EngineIOPacket.SocketIOClose)
                {
                    _timer.Dispose();
                    _sendChannel.Writer.Complete();

#if NETSTANDARD2_1
                    var buffer = new[] { (byte)'4', (byte)'1' }.AsMemory();
#else
                    var buffer = new ArraySegment<byte>(new[] { (byte)'4', (byte)'1' });

#endif
                    await _socket.SendAsync(buffer, WebSocketMessageType.Text, true, default).ConfigureAwait(false);
                    return;
                }

                throw new InvalidOperationException();
            }
        }

        async Task ReceiveLoop()
        {
            while (true)
            {
                var packet = await ReceiveEngineIOPacketAsync().ConfigureAwait(false);
                if (packet is null)
                    return;

                if (packet == EngineIOPacket.Pong)
                {
                    Debug.WriteLine("[Pong]");
                    continue;
                }

                if (packet != EngineIOPacket.Message)
                    throw new InvalidOperationException();

                await ReceiveSocketIOPacketAsync().ConfigureAwait(false);
            }
        }
        async ValueTask ReceiveSocketIOPacketAsync()
        {
#if NETSTANDARD2_1
            var buffer = _socketIOPacketTypeBuffer.AsMemory();
#else
            var buffer = new ArraySegment<byte>(_socketIOPacketTypeBuffer);
#endif

            var receiveResult = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

            switch ((SocketIOPacketType)_socketIOPacketTypeBuffer[0])
            {
                case SocketIOPacketType.Event:
                    var @event = await JsonSerializer.DeserializeAsync<SocketIOEvent>(_eventStream).ConfigureAwait(false);

                    EventReceived?.Invoke(@event);
                    break;

                default:
                    throw new InvalidOperationException();
            }
        }

        public void Send(string @event, object argument = null) => Send(new SocketIOEvent(@event) { Argument = argument });
        public void Send(string @event, IList<object> arguments) => Send(new SocketIOEvent(@event) { Arguments = arguments });
        public void Send(string @event, params object[] arguments) => Send(new SocketIOEvent(@event) { Arguments = arguments });
        public void Send(SocketIOEvent @event) => _sendChannel.Writer.TryWrite(new SocketIOEventPacket(@event));

        public async ValueTask CloseAsync()
        {
            await _sendChannel.Writer.WriteAsync(EngineIOPacket.SocketIOClose).ConfigureAwait(false);

            await _coreIOTask.ConfigureAwait(false);

            await _socket.CloseAsync(WebSocketCloseStatus.Empty, null, default).ConfigureAwait(false);

            IsConnected = false;

            _socket = null;
            _eventStream = null;
            _timer = null;
            _sendChannel = null;
            _coreIOTask = null;
        }
    }
}
