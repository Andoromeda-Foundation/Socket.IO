using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Andoromeda.Socket.IO.Client
{
    public sealed class SocketIOClient : IDisposable
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public SocketIOClient(string baseUrl) : this(baseUrl, new DefaultHttpClientFactory()) { }
        public SocketIOClient(string baseUrl, IHttpClientFactory httpClientFactory)
        {
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
        private ValueTask EstablishNormally(HttpClient httpClient)
        {
            throw new NotImplementedException();
        }
        private ValueTask EstablishWebsocketConnectionDirectly(HttpClient httpClient)
        {
            throw new NotImplementedException();
        }
    }
}
