using System.Net.Http;

namespace Andoromeda.Socket.IO.Client
{
    public sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient Create() => new HttpClient();
    }
}
