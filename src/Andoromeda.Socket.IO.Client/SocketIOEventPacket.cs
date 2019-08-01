using System;

namespace Andoromeda.Socket.IO.Client
{
    class SocketIOEventPacket : EngineIOPacket
    {
        public SocketIOEvent Event { get; }

        public SocketIOEventPacket(SocketIOEvent @event) : base(EngineIOPacketType.Message)
        {
            Event = @event ?? throw new ArgumentNullException(nameof(@event));
        }
    }
}
