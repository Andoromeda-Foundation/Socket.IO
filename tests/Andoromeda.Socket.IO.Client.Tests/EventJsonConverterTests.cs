using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Andoromeda.Socket.IO.Client.Tests
{
    public static class EventJsonConverterTests
    {
        class MappedObject
        {
            public string PrimitiveType { get; set; }
            public int[] Array { get; set; }
            public NestedObject Nested { get; set; }

            public static MappedObject Example { get; } = new MappedObject()
            {
                PrimitiveType = "test_string",
                Array = new[] { 1, 2, 3, 4, 5 },
                Nested = new NestedObject() { prop = 123, values = new[] { true, false } },
            };

            public class NestedObject
            {
                public int prop { get; set; }
                public bool[] values { get; set; }
            }
        }

        static EventJsonConverterTests()
        {
            SocketIOEvent.MapEvent<int[]>("one_array_mapped_array_int");
            SocketIOEvent.MapEvent<IEnumerable<int>>("one_array_mapped_ienumerable_int");

            SocketIOEvent.MapEvent<MappedObject>("one_object_mapped");
        }

        private static SocketIOEvent Deserialize(string json) => JsonSerializer.Deserialize<SocketIOEvent>(json);

        [Fact]
        public static void DeserializeEventNameOnly()
        {
            var @event = Deserialize("[\"event_only\"]");

            Assert.Equal("event_only", @event.Name);
            Assert.Null(@event.Argument);
            Assert.Null(@event.Arguments);
        }

        [Fact]
        public static void DeserializeOnePrimitiveTypeArgument()
        {
            SocketIOEvent @event;

            @event = Deserialize("[\"pta\",null]");

            Assert.Equal("pta", @event.Name);
            Assert.Null(@event.Argument);
            Assert.Null(@event.Arguments);

            JsonElement element;

            @event = Deserialize("[\"pta\",true]");

            Assert.Equal("pta", @event.Name);
            Assert.IsType<JsonElement>(@event.Argument);
            Assert.Null(@event.Arguments);
            element = (JsonElement)@event.Argument;
            Assert.Equal(JsonValueKind.True, element.ValueKind);

            @event = Deserialize("[\"pta\",false]");

            Assert.Equal("pta", @event.Name);
            Assert.IsType<JsonElement>(@event.Argument);
            Assert.Null(@event.Arguments);
            element = (JsonElement)@event.Argument;
            Assert.Equal(JsonValueKind.False, element.ValueKind);

            @event = Deserialize("[\"pta\",1234]");

            Assert.Equal("pta", @event.Name);
            Assert.IsType<JsonElement>(@event.Argument);
            Assert.Null(@event.Arguments);
            element = (JsonElement)@event.Argument;
            Assert.Equal(JsonValueKind.Number, element.ValueKind);
            Assert.Equal(1234, element.GetInt32());

            @event = Deserialize("[\"pta\",1234.1234]");

            Assert.Equal("pta", @event.Name);
            Assert.IsType<JsonElement>(@event.Argument);
            Assert.Null(@event.Arguments);
            element = (JsonElement)@event.Argument;
            Assert.Equal(JsonValueKind.Number, element.ValueKind);
            Assert.Equal(1234.1234, element.GetDouble());

            @event = Deserialize("[\"pta\",\"string\"]");

            Assert.Equal("pta", @event.Name);
            Assert.IsType<JsonElement>(@event.Argument);
            Assert.Null(@event.Arguments);
            element = (JsonElement)@event.Argument;
            Assert.Equal(JsonValueKind.String, element.ValueKind);
            Assert.Equal("string", element.GetString());
        }

        [Fact]
        public static void DeserializeOneArray()
        {
            SocketIOEvent @event;

            @event = JsonSerializer.Deserialize<SocketIOEvent>("[\"one_array\",[]]");

            Assert.Equal("one_array", @event.Name);
            Assert.Equal(Array.Empty<JsonElement>(), @event.Argument);
            Assert.Null(@event.Arguments);

            @event = JsonSerializer.Deserialize<SocketIOEvent>("[\"one_array_mapped_array_int\",[1,2,3]]");

            Assert.Equal("one_array_mapped_array_int", @event.Name);
            Assert.Equal(new[] { 1, 2, 3 }, @event.Argument);
            Assert.Null(@event.Arguments);

            @event = JsonSerializer.Deserialize<SocketIOEvent>("[\"one_array_mapped_ienumerable_int\",[1,2,3]]");

            Assert.Equal("one_array_mapped_ienumerable_int", @event.Name);
            Assert.Equal(new[] { 1, 2, 3 }, @event.Argument);
            Assert.Null(@event.Arguments);
        }

        [Fact]
        public static void DeserializeOneObject()
        {
            SocketIOEvent @event;

            @event = JsonSerializer.Deserialize<SocketIOEvent>("[\"one_object\",{\"key\":\"value\"}]");
            Assert.IsType<JsonElement>(@event.Argument);
            Assert.Null(@event.Arguments);
            var element = (JsonElement)@event.Argument;
            Assert.Equal(JsonValueKind.Object, element.ValueKind);
            Assert.True(element.TryGetProperty("key", out element));
            Assert.Equal(JsonValueKind.String, element.ValueKind);
            Assert.Equal("value", element.GetString());

            @event = JsonSerializer.Deserialize<SocketIOEvent>("[\"one_object_mapped\",{\"PrimitiveType\":\"test_string\",\"Array\":[1,2,3,4,5],\"Nested\":{\"prop\":123456789,\"values\":[true,false]}}]");
            Assert.Equal("one_object_mapped", @event.Name);
            Assert.Null(@event.Arguments);
            var obj = (MappedObject)@event.Argument;
            Assert.Equal("test_string", obj.PrimitiveType);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, obj.Array);
            var nested = obj.Nested;
            Assert.Equal(123456789, nested.prop);
            Assert.Equal(new[] { true, false }, nested.values);
        }

        [Fact]
        public static void SerializeEventNameOnly()
        {
            var @event = new SocketIOEvent("event_only");
            var json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"event_only\"]", json);
        }

        [Fact]
        public static void SerializeOnePrimitiveTypeArgument()
        {
            SocketIOEvent @event;
            string json;

            @event = new SocketIOEvent("pta") { Argument = true };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"pta\",true]", json);

            @event = new SocketIOEvent("pta") { Argument = false };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"pta\",false]", json);

            @event = new SocketIOEvent("pta") { Argument = 1234 };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"pta\",1234]", json);

            @event = new SocketIOEvent("pta") { Argument = 1234.1234 };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"pta\",1234.1234]", json);

            @event = new SocketIOEvent("pta") { Argument = "string" };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"pta\",\"string\"]", json);
        }

        [Fact]
        public static void SerializeOneArray()
        {
            SocketIOEvent @event;
            string json;

            @event = new SocketIOEvent("one_array") { Argument = Array.Empty<object>() };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"one_array\",[]]", json);

            @event = new SocketIOEvent("one_array") { Argument = Array.Empty<JsonElement>() };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"one_array\",[]]", json);

            @event = new SocketIOEvent("one_array_mapped_array_int") { Argument = new[] { 1, 2, 3 } };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"one_array_mapped_array_int\",[1,2,3]]", json);

            @event = new SocketIOEvent("one_array_mapped_ienumerable_int") { Argument = new[] { 1, 2, 3 } };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"one_array_mapped_ienumerable_int\",[1,2,3]]", json);
        }

        [Fact]
        public static void SerializeOneObject()
        {
            SocketIOEvent @event;
            string json;

            @event = new SocketIOEvent("one_object") { Argument = new { key = "value" } };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"one_object\",{\"key\":\"value\"}]", json);

            @event = new SocketIOEvent("one_object_mapped") { Argument = MappedObject.Example };
            json = JsonSerializer.Serialize(@event);
            Assert.Equal("[\"one_object_mapped\",{\"PrimitiveType\":\"test_string\",\"Array\":[1,2,3,4,5],\"Nested\":{\"prop\":123,\"values\":[true,false]}}]", json);
        }
    }
}
