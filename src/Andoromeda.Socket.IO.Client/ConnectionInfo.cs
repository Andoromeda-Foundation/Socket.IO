using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andoromeda.Socket.IO.Client
{
    sealed partial class ConnectionInfo
    {
        [JsonPropertyName("sid")]
        public string SocketId { get; set; }

        [JsonPropertyName("upgrades")]
        public string[] Upgrades { get; set; }

        [JsonPropertyName("pingInterval")]
        public int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")]
        public int PingTimeout { get; set; }

        public static ConnectionInfo Parse(ReadOnlySpan<byte> content)
        {
            if (content[0] != '0')
                Utils.ThrowParseException();

            return JsonSerializer.Deserialize<ConnectionInfo>(content.Slice(1));
        }
    }
}
