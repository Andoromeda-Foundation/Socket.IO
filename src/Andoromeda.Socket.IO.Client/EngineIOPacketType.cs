namespace Andoromeda.Socket.IO.Client
{
    enum EngineIOPacketType
    {
        Open = '0',
        Close,
        Ping,
        Pong,
        Message,
        Upgrade,
        Noop,
    }
}
