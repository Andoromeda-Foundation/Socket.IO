using System.Threading.Tasks;
using Xunit;

namespace Andoromeda.Socket.IO.Client.Tests
{
    public static class ConnectionTests
    {
        static IHttpClientFactory _httpClientFactory = new DefaultHttpClientFactory();

        [Fact]
        public static async Task PollingAndThenWebsocketTest()
        {
            using var client = new SocketIOClient("http://localhost:10000/", _httpClientFactory);

            await client.ConnectAsync();
        }

        [Fact]
        public static async Task WebsocketDirectlyTest()
        {
            using var client = new SocketIOClient("http://localhost:10000/", _httpClientFactory);

            await client.ConnectAsync(new ConnectionOptions() { NoLongPollingConnection = true });
        }
    }
}
