namespace Andoromeda.Socket.IO.Client
{
    class EngineIOPacket
    {
        public static EngineIOPacket Ping { get; } = new EngineIOPacket(EngineIOPacketType.Ping);
        public static EngineIOPacket Pong { get; } = new EngineIOPacket(EngineIOPacketType.Pong);
        public static EngineIOPacket PongProbe { get; } = new EngineIOPacket(EngineIOPacketType.Pong);
        public static EngineIOPacket Message { get; } = new EngineIOPacket(EngineIOPacketType.Message);
        public static EngineIOPacket Upgrade { get; } = new EngineIOPacket(EngineIOPacketType.Upgrade);

        public static EngineIOPacket SocketIOClose { get; } = new EngineIOPacket(EngineIOPacketType.Message);

        public EngineIOPacketType Type { get; }

        public EngineIOPacket(EngineIOPacketType type)
        {
            Type = type;
        }
    }
}
