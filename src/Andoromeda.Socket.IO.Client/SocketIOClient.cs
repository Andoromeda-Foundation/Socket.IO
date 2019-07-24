using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Andoromeda.Socket.IO.Client
{
    public sealed class SocketIOClient : IDisposable
    {
        private readonly string _baseUrl;
        private readonly IHttpClientFactory _httpClientFactory;

        public SocketIOClient(string baseUrl) : this(baseUrl, new DefaultHttpClientFactory()) { }
        public SocketIOClient(string baseUrl, IHttpClientFactory httpClientFactory)
        {
            _baseUrl = baseUrl;
            _httpClientFactory = httpClientFactory;
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
            var httpClient = _httpClientFactory.Create();

            if (options is null || !options.NoLongPollingConnection)
                await EstablishNormally(httpClient);
            else
                await EstablishWebsocketConnectionDirectly(httpClient);
        }
        private async ValueTask EstablishNormally(HttpClient httpClient)
        {
            var builder = new UriBuilder(_baseUrl);

            builder.Path = "/socket.io/";
            builder.Query = "EIO=3&transport=polling&t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            ConnectionInfo info;
            using (var response = await httpClient.GetAsync(builder.Uri))
            {
                response.EnsureSuccessStatusCode();

                info = ParseConnectionInfo(await response.Content.ReadAsByteArrayAsync());
            }

            if (!Array.Exists(info.Upgrades, r => r.Equals("websocket", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This library supports WebSocket only.");

            builder.Scheme = "ws";
            builder.Query = "EIO=3&transport=websocket&sid=" + info.SocketId;
        }
        private async ValueTask EstablishWebsocketConnectionDirectly(HttpClient httpClient)
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

                using var response = await httpClient.SendAsync(request);

                if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
                    throw new InvalidOperationException();

                var accept = response.Headers.GetValues("Sec-Websocket-Accept").SingleOrDefault();
                if (accept != expectedAccept)
                    throw new InvalidOperationException();
            }
        }

        private static ConnectionInfo ParseConnectionInfo(ReadOnlySpan<byte> content)
        {
            if (!Utf8Parser.TryParse(content, out int length, out var consumed))
                ThrowParseException();

            content = content.Slice(consumed + 1);
            var info = ParseConnectionInfoCore(content.Slice(0, length));
            content = content.Slice(length);

            if (!Utf8Parser.TryParse(content.Slice(0), out length, out consumed) || length != 2)
                ThrowParseException();

            content = content.Slice(consumed + 1);

            if (!Utf8Parser.TryParse(content.Slice(0), out int message, out _) || message != 40)
                ThrowParseException();

            return info;

            static ConnectionInfo ParseConnectionInfoCore(ReadOnlySpan<byte> content)
            {
                if (content[0] != '0' || content[1] != '{')
                    ThrowParseException();

                content = content.Slice(2);

                var info = new ConnectionInfo();

                while (true)
                {
                    if (!TryParseQuotedString(content, out var key, out var consumed))
                        ThrowParseException();

                    if (content[consumed] != ':')
                        ThrowParseException();
                    consumed += 1;
                    content = content.Slice(consumed);

                    if (key.Equals("sid", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseQuotedString(content, out var socketId, out consumed))
                            ThrowParseException();

                        info.SocketId = socketId;
                    }
                    else if (key.Equals("upgrades", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseUpgradableList(content, out var upgrades, out consumed))
                            ThrowParseException();

                        info.Upgrades = upgrades;
                    }
                    else if (key.Equals("pingInterval", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Utf8Parser.TryParse(content, out int interval, out consumed))
                            ThrowParseException();

                        info.PingInterval = interval;
                    }
                    else if (key.Equals("pingTimeout", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Utf8Parser.TryParse(content, out int timeout, out consumed))
                            ThrowParseException();

                        info.PingTimeout = timeout;
                    }
                    else
                        ThrowParseException();

                    if (content[consumed] == ',')
                        consumed++;

                    if (content[consumed] == '}')
                        break;

                    content = content.Slice(consumed);
                }

                return info;
            }
            static bool TryParseQuotedString(ReadOnlySpan<byte> source, out string result, out int consumed)
            {
                result = null;
                consumed = 0;

                if (source[0] != '"')
                    return false;

                var index = source.Slice(1).IndexOf((byte)'"');
                if (index == -1)
                    return false;

                source = source.Slice(1, index);
#if NETSTANDARD2_1
                result = Encoding.UTF8.GetString(source);
#else
                result = Encoding.UTF8.GetString(source.ToArray());
#endif
                consumed = source.Length + 2;
                return true;
            }
            static bool TryParseUpgradableList(ReadOnlySpan<byte> source, out string[] result, out int consumed)
            {
                result = null;
                consumed = 0;

                if (source[0] != '[')
                    return false;

                source = source.Slice(1);

                var list = new List<string>();
                while (source[0] != ']')
                {
                    if (!TryParseQuotedString(source, out var upgrade, out var innerConsumed))
                        return false;

                    list.Add(upgrade);

                    consumed += innerConsumed;

                    if (source[innerConsumed] == ',')
                        consumed++;

                    source = source.Slice(consumed);
                }

                consumed += 2;
                if (list.Count == 0)
                    result = Array.Empty<string>();
                else
                    result = list.ToArray();
                return true;
            }
        }
        static void ThrowParseException() => throw new InvalidOperationException();

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
    }
}
