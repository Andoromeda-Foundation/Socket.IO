using Newtonsoft.Json;

namespace Andoromeda.Socket.IO.Client
{
    sealed class ConnectionInfo
    {
        [JsonProperty("sid")]
        public string SocketId { get; set; }
        [JsonProperty("upgrades")]
        public string[] Upgrades { get; set; }
        [JsonProperty("pingInterval")]
        public int PingInterval { get; set; }
        [JsonProperty("pingTimeout")]
        public int PingTimeout { get; set; }
    }
}
