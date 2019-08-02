using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Andoromeda.Socket.IO.Client.Tests
{
    public static class ConnectionTests
    {
        static HttpClient _httpClient = new HttpClient();

        [Fact]
        public static Task PollingAndThenWebSocketTest() => TestCore(false);
        [Fact]
        public static Task DirectWebSocketTest() => TestCore(true);

        private static async Task TestCore(bool directConnection)
        {
            using var client = new SocketIOClient("http://localhost:10000/", _httpClient);

            await client.ConnectAsync(new ConnectionOptions() { NoLongPollingConnection = directConnection });

            Assert.True(client.IsConnected);

            await client.CloseAsync();

            Assert.False(client.IsConnected);
        }
    }
}
