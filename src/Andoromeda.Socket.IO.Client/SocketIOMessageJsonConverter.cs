using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andoromeda.Socket.IO.Client
{
    sealed class SocketIOMessageJsonConverter : JsonConverter<SocketIOMessage>
    {
        public override SocketIOMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ThrowIfNot(ref reader, JsonTokenType.StartArray);

            reader.Read();
            var @event = reader.GetString();
            var result = new SocketIOMessage(@event);

            var mappedType = SocketIOMessage.GetMappedType(@event);
            object item = null;
            List<object> items = null;

            reader.Read();
            do
            {
                if (items is null && !(item is null))
                    items = new List<object>() { item };

                if (!(mappedType is null))
                    item = JsonSerializer.Deserialize(ref reader, mappedType);
                else
                    item = JsonSerializer.Deserialize<object>(ref reader);

                if (!(items is null))
                    items.Add(item);

                reader.Read();
            } while (reader.TokenType != JsonTokenType.EndArray);

            if (reader.Read())
                throw new InvalidOperationException();

            if (items is null)
                result.Data = item;
            else
                result.Data = items;

            return result;
        }

        void ThrowIfNot(ref Utf8JsonReader reader, JsonTokenType tokenType)
        {
            if (reader.TokenType != tokenType)
                Utils.ThrowParseException();
        }

        public override void Write(Utf8JsonWriter writer, SocketIOMessage value, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }
    }
}
