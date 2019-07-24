using System.Net.Http;

namespace Andoromeda.Socket.IO.Client
{
    public interface IHttpClientFactory
    {
        HttpClient Create();
    }
}
