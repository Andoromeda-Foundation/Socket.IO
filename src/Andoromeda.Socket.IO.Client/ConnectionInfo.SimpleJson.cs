#if !NETSTANDARD2_1
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;

namespace Andoromeda.Socket.IO.Client
{
    partial class ConnectionInfo
    {
        public static ConnectionInfo Parse(ReadOnlySpan<byte> content)
        {
            if (content[0] != '0' || content[1] != '{')
                Utils.ThrowParseException();

            content = content.Slice(2);

            var info = new ConnectionInfo();

            while (true)
            {
                if (!TryParseQuotedString(content, out var key, out var consumed))
                    Utils.ThrowParseException();

                if (content[consumed] != ':')
                    Utils.ThrowParseException();
                consumed += 1;
                content = content.Slice(consumed);

                if (key.Equals("sid", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseQuotedString(content, out var socketId, out consumed))
                        Utils.ThrowParseException();

                    info.SocketId = socketId;
                }
                else if (key.Equals("upgrades", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseUpgradableList(content, out var upgrades, out consumed))
                        Utils.ThrowParseException();

                    info.Upgrades = upgrades;
                }
                else if (key.Equals("pingInterval", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Utf8Parser.TryParse(content, out int interval, out consumed))
                        Utils.ThrowParseException();

                    info.PingInterval = interval;
                }
                else if (key.Equals("pingTimeout", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Utf8Parser.TryParse(content, out int timeout, out consumed))
                        Utils.ThrowParseException();

                    info.PingTimeout = timeout;
                }
                else
                    Utils.ThrowParseException();

                if (content[consumed] == ',')
                    consumed++;

                if (content[consumed] == '}')
                    break;

                content = content.Slice(consumed);
            }

            return info;
        }

        static bool TryParseQuotedString(ReadOnlySpan<byte> source, out string result, out int consumed)
        {
            result = null;
            consumed = 0;

            if (source[0] != '"')
                return false;

            var index = source.Slice(1).IndexOf((byte)'"');
            if (index == -1)
                return false;

            source = source.Slice(1, index);
            result = Encoding.UTF8.GetString(source.ToArray());
            consumed = source.Length + 2;
            return true;
        }
        static bool TryParseUpgradableList(ReadOnlySpan<byte> source, out string[] result, out int consumed)
        {
            result = null;
            consumed = 0;

            if (source[0] != '[')
                return false;

            source = source.Slice(1);

            var list = new List<string>();
            while (source[0] != ']')
            {
                if (!TryParseQuotedString(source, out var upgrade, out var innerConsumed))
                    return false;

                list.Add(upgrade);

                consumed += innerConsumed;

                if (source[innerConsumed] == ',')
                    consumed++;

                source = source.Slice(consumed);
            }

            consumed += 2;
            if (list.Count == 0)
                result = Array.Empty<string>();
            else
                result = list.ToArray();
            return true;
        }
    }
}
#endif
