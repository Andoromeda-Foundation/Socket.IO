using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace Andoromeda.Socket.IO.Client
{
    class JsonDataWriter : TextWriter
    {
        private readonly ClientWebSocket _socket;
        private readonly byte[] _buffer;

        private int _offset;

        public override Encoding Encoding => Encoding.UTF8;

        public JsonDataWriter(ClientWebSocket socket, byte[] buffer)
        {
            _socket = socket;
            _buffer = buffer;

            _offset = 2;
        }

        public override Task WriteAsync(char value)
        {
            if (_offset == _buffer.Length)
                return SendAndAppend(value);

            _buffer[_offset++] = (byte)value;
            return Task.CompletedTask;
        }
        public override Task WriteAsync(string value)
        {
            var charCount = Encoding.UTF8.GetMaxCharCount(_buffer.Length - _offset) - 1;
            if (value.Length > charCount)
                return SendBigString(value, charCount);

            var byteCount = Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _offset);
            _offset += byteCount;
            return Task.CompletedTask;
        }
        private async Task SendBigString(string value, int charCount)
        {
            var offset = Math.Min(value.Length, charCount);

            var count = Encoding.UTF8.GetBytes(value, 0, offset, _buffer, _offset);
            _offset += count;

            charCount = Encoding.UTF8.GetMaxCharCount(_buffer.Length) - 1;

#if NETSTANDARD2_1
            var buffer = _buffer.AsMemory();
#else
                var buffer = new ArraySegment<byte>(_buffer);
#endif

            do
            {
                await _socket.SendAsync(buffer, WebSocketMessageType.Text, false, default);

                _offset = Encoding.UTF8.GetBytes(value, offset, Math.Min(value.Length - offset, charCount), _buffer, 0);
                offset += charCount;
            }
            while (_offset == _buffer.Length);
        }

        async Task SendAndAppend(char value)
        {
#if NETSTANDARD2_1
            await _socket.SendAsync(_buffer.AsMemory(), WebSocketMessageType.Text, false, default);
#else
                await _socket.SendAsync(new ArraySegment<byte>(_buffer), WebSocketMessageType.Text, false, default);
#endif

            _offset = 0;
            _buffer[0] = (byte)value;
        }

        public override Task FlushAsync() =>
#if NETSTANDARD2_1
                _socket.SendAsync(_buffer.AsMemory(0, _offset), WebSocketMessageType.Text, true, default).AsTask();
#else
                _socket.SendAsync(new ArraySegment<byte>(_buffer, 0, _offset), WebSocketMessageType.Text, true, default);
#endif
    }
}
