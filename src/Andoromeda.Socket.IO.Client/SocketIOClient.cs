using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Andoromeda.Socket.IO.Client
{
    public sealed class SocketIOClient : IDisposable
    {
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        private ClientWebSocket _socket;
        private WebSocketMessageStream _messageStream;

        public bool IsConnected { get; private set; }

        public event Action<SocketIOClient> Connected;

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
        }
        #endregion

        public ValueTask ConnectAsync() => ConnectAsync(null);
        public async ValueTask ConnectAsync(ConnectionOptions options)
        {
            if (options is null || !options.NoLongPollingConnection)
                await EstablishNormally().ConfigureAwait(false);
            else
                await EstablishWebsocketConnectionDirectly().ConfigureAwait(false);

            _messageStream = new WebSocketMessageStream(_socket);

            IsConnected = true;
            Connected?.Invoke(this);
        }
        private async ValueTask EstablishNormally()
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
            await EstablishWebsocketConnection(builder.Uri, info).ConfigureAwait(false);

            // Send [Ping]
#if NETSTANDARD2_1
            Memory<byte> buffer = new[] { (byte)'2', (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' };
#else
            var buffer = new ArraySegment<byte>(new[] { (byte)'2', (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' });
#endif
            await _socket.SendAsync(buffer, WebSocketMessageType.Text, true, default).ConfigureAwait(false);

            // Receive [Pong]
            await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

            static bool Is3Probe(ReadOnlySpan<byte> span) =>
                span[0] == '3' && span[1] == 'p' && span[2] == 'r' && span[3] == 'o' && span[4] == 'b' && span[5] == 'e';
#if NETSTANDARD2_1
            if (!Is3Probe(buffer.Span))
#else
            if (!Is3Probe(buffer))
#endif
                throw new InvalidOperationException();

            // Send [Upgrade]
#if NETSTANDARD2_1
            buffer = new[] { (byte)'5' };
#else
            buffer = new ArraySegment<byte>(new[] { (byte)'5' });
#endif
            await _socket.SendAsync(buffer, WebSocketMessageType.Text, true, default).ConfigureAwait(false);

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

        private async ValueTask EstablishWebsocketConnectionDirectly()
        {
            var builder = new UriBuilder(_baseUrl)
            {
                Path = "/socket.io/",
                Query = "EIO=3&transport=websocket",
            };

            using (var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri))
            {
                var (key, expectedAccept) = GenerateWebsocketKeyAndExpectedHash();

                request.Headers.Add("Connection", "Upgrade");
                request.Headers.Add("Upgrade", "websocket");
                request.Headers.Add("Sec-Websocket-Version", "13");
                request.Headers.Add("Sec-Websocket-Key", key);
                request.Headers.Add("Sec-Websocket-Extensions", "permessage-deflate; client_max_window_bits");

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
                    throw new InvalidOperationException();

                var accept = response.Headers.GetValues("Sec-Websocket-Accept").SingleOrDefault();
                if (accept != expectedAccept)
                    throw new InvalidOperationException();
            }

            builder.Scheme = "ws";
            await EstablishWebsocketConnection(builder.Uri, null).ConfigureAwait(false);

#if NETSTANDARD2_1
            Memory<byte> buffer = new byte[256];
            await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

            var info = ConnectionInfo.Parse(buffer.Span);
#else
            var buffer = new ArraySegment<byte>(new byte[256]);
            await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

            var info = ConnectionInfo.Parse(buffer);
#endif

            var result = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);
            if (result.Count != 2)
                throw new InvalidOperationException();

#if NETSTANDARD2_1
            if (!Is40(buffer.Span))
#else
            if (!Is40(buffer))
#endif
                throw new InvalidOperationException();

            static bool Is40(ReadOnlySpan<byte> span) =>
                span[0] == '4' && span[1] == '0';
        }

        static (string, string) GenerateWebsocketKeyAndExpectedHash()
        {
            var guid = Guid.NewGuid();

#if NETSTANDARD2_1
            Span<byte> buffer = stackalloc byte[16];
            guid.TryWriteBytes(buffer);

            var key = Convert.ToBase64String(buffer);
#else
            var key = Convert.ToBase64String(guid.ToByteArray());
#endif

            // GUID source: https://tools.ietf.org/html/rfc6455
            var contentToHash = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

            using var sha1 = SHA1.Create();
#if NETSTANDARD2_1
            Span<byte> contentBuffer = stackalloc byte[Encoding.UTF8.GetByteCount(contentToHash)];
            Encoding.UTF8.GetBytes(contentToHash, contentBuffer);

            buffer = stackalloc byte[20];
            sha1.TryComputeHash(contentBuffer, buffer, out _);
            var hash = Convert.ToBase64String(buffer);
#else
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(contentToHash));
            var hash = Convert.ToBase64String(hashBytes);
#endif

            return (key, hash);
        }

        private async ValueTask EstablishWebsocketConnection(Uri uri, ConnectionInfo info)
        {
            var socket = new ClientWebSocket();

            if (!(info is null))
                socket.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(info.PingInterval);

            await socket.ConnectAsync(uri, default).ConfigureAwait(false);

            _socket = socket;
        }

        public async ValueTask SendAsync(string eventName, object data)
        {
            var array = data == null ? new string[] { eventName } : new object[] { eventName, data };

            using (_messageStream.RentBuffer())
            {
#if NETSTANDARD2_1
                await _messageStream.WriteAsync(new[] { (byte)'4', (byte)'2' }, default).ConfigureAwait(false);
#else
                await _messageStream.WriteAsync(new[] { (byte)'4', (byte)'2' }, 0, 2, default).ConfigureAwait(false);
#endif
                await JsonSerializer.SerializeAsync(_messageStream, array).ConfigureAwait(false);
                await _messageStream.FlushAsync().ConfigureAwait(false);
            }
        }

        public async ValueTask CloseAsync()
        {
#if NETSTANDARD2_1
            Memory<byte> buffer = new[] { (byte)'4', (byte)'1' };
#else
            var buffer = new ArraySegment<byte>(new[] { (byte)'4', (byte)'1' });
#endif

            await _socket.SendAsync(buffer, WebSocketMessageType.Text, true, default).ConfigureAwait(false);
            await _socket.CloseAsync(WebSocketCloseStatus.Empty, null, default).ConfigureAwait(false);

            IsConnected = false;
        }

        public async ValueTask<(string Event, JToken Data)> ReceiveAsync()
        {
            using var stream = await ReceiveWithStream().ConfigureAwait(false);

            var buffer = new byte[2];
            stream.Read(buffer, 0, 2);
            if (buffer[0] != '4' || buffer[1] != '2')
                throw new InvalidOperationException();

            using var reader = new JsonTextReader(new StreamReader(stream));
            var response = await JArray.LoadAsync(reader).ConfigureAwait(false);

            return (response[0].ToObject<string>(), response[1]);
        }
        private async ValueTask<Stream> ReceiveWithStream()
        {
            var stream = new MemoryStream();

            var array = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
#if NETSTANDARD2_1
                var buffer = array.AsMemory();
                ValueWebSocketReceiveResult result;
#else
                var buffer = new ArraySegment<byte>(array);
                WebSocketReceiveResult result = null;
#endif

                do
                {
                    result = await _socket.ReceiveAsync(buffer, default).ConfigureAwait(false);

#if NETSTANDARD2_1
                    stream.Write(buffer.Span[..result.Count]);
#else
                    stream.Write(buffer.Array, buffer.Offset, result.Count);
#endif
                }
                while (!result.EndOfMessage);

                stream.Position = 0;

                return stream;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }
    }
}
