namespace Andoromeda.Socket.IO.Client
{
    enum SocketIOPacketType
    {
        Connect = '0',
        Disconnect,
        Event,
        Ack,
        Error,
        BinaryEvent,
        BinaryAck,
    }
}
