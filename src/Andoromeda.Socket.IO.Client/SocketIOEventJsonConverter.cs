﻿using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andoromeda.Socket.IO.Client
{
    sealed class SocketIOEventJsonConverter : JsonConverter<SocketIOEvent>
    {
        public override SocketIOEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                Utils.ThrowParseException();

            reader.Read();
            var @event = reader.GetString();
            var result = new SocketIOEvent(@event);

            var mappedType = SocketIOEvent.GetMappedType(@event);

            List<object> arguments = null;

            reader.Read();

            if (reader.TokenType == JsonTokenType.EndArray)
                return result;

            do
            {
                if (!(result.Argument is null) && arguments is null)
                    result.Arguments = arguments = new List<object>() { result.Argument };

                var argument = DeserializeArgument(ref reader, mappedType);

                if (result.Argument is null)
                    result.Argument = argument;

                if (!(arguments is null))
                    arguments.Add(argument);

                reader.Read();
            } while (reader.TokenType != JsonTokenType.EndArray);

            if (reader.Read())
                throw new InvalidOperationException();

            return result;
        }
        object DeserializeArgument(ref Utf8JsonReader reader, Type mappedType)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                if (mappedType is null)
                    return JsonSerializer.Deserialize<JsonElement[]>(ref reader);

                if (mappedType.IsArray || typeof(IEnumerable).IsAssignableFrom(mappedType))
                    return JsonSerializer.Deserialize(ref reader, mappedType);

                throw new InvalidOperationException("Expected mapped type is array or enumerable type.");
            }

            if (reader.TokenType == JsonTokenType.StartObject && !(mappedType is null))
                return JsonSerializer.Deserialize(ref reader, mappedType);

            return JsonSerializer.Deserialize<object>(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, SocketIOEvent value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(value.Name);

            if (!(value.Argument is null && value.Arguments is null))
            {
                var mappedType = SocketIOEvent.GetMappedType(value.Name);

                if (value.Arguments is null)
                    SerializeArgument(writer, value.Argument, mappedType);
                else
                    foreach (var argument in value.Arguments)
                        SerializeArgument(writer, argument, mappedType);
            }

            writer.WriteEndArray();
        }

        void SerializeArgument(Utf8JsonWriter writer, object argument, Type mappedType)
        {
            if (argument is IEnumerable<object> arguments)
            {
                writer.WriteStartArray();
                foreach (var item in arguments)
                    SerializeArgument(writer, item, mappedType);
                writer.WriteEndArray();
                return;
            }

            if (mappedType is null)
                JsonSerializer.Serialize(writer, argument);
            else
                JsonSerializer.Serialize(writer, argument, mappedType);
        }
    }
}
