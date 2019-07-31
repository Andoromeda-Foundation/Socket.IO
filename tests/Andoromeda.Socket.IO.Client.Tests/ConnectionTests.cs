using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Andoromeda.Socket.IO.Client.Tests
{
    public static class ConnectionTests
    {
        [Fact]
        public static async Task PollingAndThenWebsocketTest()
        {
            using var client = new SocketIOClient("http://localhost:10000/", new HttpClient());

            await client.ConnectAsync();

            Assert.True(client.IsConnected);

            await client.CloseAsync();

            Assert.False(client.IsConnected);
        }
    }
}
