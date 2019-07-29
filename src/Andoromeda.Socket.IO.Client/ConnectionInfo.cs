#if NETSTANDARD2_1
using System.Text.Json.Serialization;
#endif

namespace Andoromeda.Socket.IO.Client
{
    sealed partial class ConnectionInfo
    {
#if NETSTANDARD2_1
        [JsonPropertyName("sid")]
#endif
        public string SocketId { get; set; }

#if NETSTANDARD2_1
        [JsonPropertyName("upgrades")]
#endif
        public string[] Upgrades { get; set; }

#if NETSTANDARD2_1
        [JsonPropertyName("pingInterval")]
#endif
        public int PingInterval { get; set; }

#if NETSTANDARD2_1
        [JsonPropertyName("pingTimeout")]
#endif
        public int PingTimeout { get; set; }
    }
}
