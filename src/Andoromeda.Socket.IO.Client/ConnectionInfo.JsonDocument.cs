#if NETSTANDARD2_1
using System;
using System.Text.Json;

namespace Andoromeda.Socket.IO.Client
{
    partial class ConnectionInfo
    {
        public static ConnectionInfo Parse(ReadOnlySpan<byte> content)
        {
            if (content[0] != '0')
                Utils.ThrowParseException();

            return JsonSerializer.Deserialize<ConnectionInfo>(content[1..]);
        }
    }
}
#endif
