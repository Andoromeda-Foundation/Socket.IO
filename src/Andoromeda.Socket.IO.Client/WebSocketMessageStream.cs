using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Andoromeda.Socket.IO.Client
{
    sealed class WebSocketMessageStream : Stream
    {
        private const int BufferSize = 8192;

        private readonly ClientWebSocket _socket;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        private byte[] _bufferArray;
        private int _bufferPosition;

        private bool _isEndOfMessage;

        public WebSocketMessageStream(ClientWebSocket socket)
        {
            _socket = socket;
        }

        public void EnsureFinalBlockIsHandled()
        {
            if (_isEndOfMessage)
                throw new InvalidOperationException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_isEndOfMessage)
            {
                _isEndOfMessage = false;
                return 0;
            }

            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken).ConfigureAwait(false);

            if (result.EndOfMessage)
                _isEndOfMessage = true;

            return result.Count;
        }

#if NETSTANDARD2_1
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_isEndOfMessage)
            {
                _isEndOfMessage = false;
                return 0;
            }

            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (result.EndOfMessage)
                _isEndOfMessage = true;

            return result.Count;
        }
#endif

        public BufferScope RentBuffer() => new BufferScope(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetAvailableCount(int desired) => Math.Min(desired, BufferSize - _bufferPosition);

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var remaning = count;

            var availableCount = GetAvailableCount(remaning);
            if (availableCount > 0)
            {
                Buffer.BlockCopy(buffer, offset, _bufferArray, _bufferPosition, availableCount);
                offset += availableCount;

                _bufferPosition += availableCount;
                remaning -= availableCount;
            }

            while (_bufferPosition == BufferSize)
            {
                await _socket.SendAsync(new ArraySegment<byte>(_bufferArray, 0, BufferSize), WebSocketMessageType.Text, false, cancellationToken).ConfigureAwait(false);
                _bufferPosition = 0;

                if (remaning == 0)
                    return;

                availableCount = GetAvailableCount(remaning);

                Buffer.BlockCopy(buffer, offset, _bufferArray, 0, availableCount);
                offset += availableCount;

                _bufferPosition += availableCount;
                remaning -= availableCount;
            }
        }

#if NETSTANDARD2_1
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var memory = _bufferArray.AsMemory();
            var remaning = buffer.Length;

            var count = GetAvailableCount(buffer.Length);
            if (count > 0)
            {
                buffer[..count].CopyTo(memory[_bufferPosition..]);
                buffer = buffer[count..];

                _bufferPosition += count;
                remaning -= count;
            }

            while (_bufferPosition == BufferSize)
            {
                await _socket.SendAsync(memory, WebSocketMessageType.Text, false, cancellationToken).ConfigureAwait(false);
                _bufferPosition = 0;

                if (remaning == 0)
                    return;

                count = GetAvailableCount(remaning);

                buffer[..count].CopyTo(memory);
                buffer = buffer[count..];

                _bufferPosition += count;
                remaning -= count;
            }
        }
#endif

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _socket.SendAsync(new ArraySegment<byte>(_bufferArray, 0, _bufferPosition), WebSocketMessageType.Text, true, cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public struct BufferScope : IDisposable
        {
            private readonly WebSocketMessageStream _owner;

            public BufferScope(WebSocketMessageStream owner)
            {
                _owner = owner;

                _owner._bufferArray = ArrayPool<byte>.Shared.Rent(BufferSize);
                _owner._bufferPosition = 0;
            }

            public void Dispose()
            {
                ArrayPool<byte>.Shared.Return(_owner._bufferArray);
                _owner._bufferPosition = 0;
            }
        }
    }
}
