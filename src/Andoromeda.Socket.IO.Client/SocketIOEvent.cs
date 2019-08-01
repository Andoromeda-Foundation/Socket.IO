using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Andoromeda.Socket.IO.Client
{
    [JsonConverter(typeof(SocketIOEventJsonConverter))]
    public sealed class SocketIOEvent
    {
        static SortedList<string, Type> _mappedEvents = new SortedList<string, Type>(StringComparer.Ordinal);

        public string Event { get; }

        public object Data { get; internal set; }

        public SocketIOEvent(string @event)
        {
            Event = @event ?? throw new ArgumentNullException(@event);
        }

        public static void MapEvent<T>(string @event) =>
            _mappedEvents[@event] = typeof(T);

        internal static Type GetMappedType(string @event)
        {
            if (!_mappedEvents.TryGetValue(@event, out var result))
                return null;

            return result;
        }
    }
}
