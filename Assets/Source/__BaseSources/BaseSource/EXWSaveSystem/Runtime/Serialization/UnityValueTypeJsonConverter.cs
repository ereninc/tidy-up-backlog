using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EXW.SaveSystem
{
    internal sealed class UnityValueTypeJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector2) ||
                   objectType == typeof(Vector3) ||
                   objectType == typeof(Vector2Int) ||
                   objectType == typeof(Vector3Int) ||
                   objectType == typeof(Quaternion) ||
                   objectType == typeof(Color);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            switch (value)
            {
                case Vector2 vector2:
                    Write(writer, "x", vector2.x);
                    Write(writer, "y", vector2.y);
                    break;

                case Vector3 vector3:
                    Write(writer, "x", vector3.x);
                    Write(writer, "y", vector3.y);
                    Write(writer, "z", vector3.z);
                    break;

                case Vector2Int vector2Int:
                    Write(writer, "x", vector2Int.x);
                    Write(writer, "y", vector2Int.y);
                    break;

                case Vector3Int vector3Int:
                    Write(writer, "x", vector3Int.x);
                    Write(writer, "y", vector3Int.y);
                    Write(writer, "z", vector3Int.z);
                    break;

                case Quaternion quaternion:
                    Write(writer, "x", quaternion.x);
                    Write(writer, "y", quaternion.y);
                    Write(writer, "z", quaternion.z);
                    Write(writer, "w", quaternion.w);
                    break;

                case Color color:
                    Write(writer, "r", color.r);
                    Write(writer, "g", color.g);
                    Write(writer, "b", color.b);
                    Write(writer, "a", color.a);
                    break;

                default:
                    throw new JsonSerializationException(
                        $"Unsupported Unity value type {value?.GetType().Name ?? "null"}.");
            }

            writer.WriteEndObject();
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            JObject value = JObject.Load(reader);

            if (objectType == typeof(Vector2))
            {
                return new Vector2(ReadFloat(value, "x"), ReadFloat(value, "y"));
            }

            if (objectType == typeof(Vector3))
            {
                return new Vector3(
                    ReadFloat(value, "x"),
                    ReadFloat(value, "y"),
                    ReadFloat(value, "z"));
            }

            if (objectType == typeof(Vector2Int))
            {
                return new Vector2Int(ReadInt(value, "x"), ReadInt(value, "y"));
            }

            if (objectType == typeof(Vector3Int))
            {
                return new Vector3Int(
                    ReadInt(value, "x"),
                    ReadInt(value, "y"),
                    ReadInt(value, "z"));
            }

            if (objectType == typeof(Quaternion))
            {
                return new Quaternion(
                    ReadFloat(value, "x"),
                    ReadFloat(value, "y"),
                    ReadFloat(value, "z"),
                    ReadFloat(value, "w"));
            }

            if (objectType == typeof(Color))
            {
                return new Color(
                    ReadFloat(value, "r"),
                    ReadFloat(value, "g"),
                    ReadFloat(value, "b"),
                    ReadFloat(value, "a", 1f));
            }

            throw new JsonSerializationException($"Unsupported Unity value type {objectType.Name}.");
        }

        private static void Write(JsonWriter writer, string name, float value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static void Write(JsonWriter writer, string name, int value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static float ReadFloat(JObject value, string name, float fallback = 0f)
        {
            return value.TryGetValue(name, out JToken token) ? token.Value<float>() : fallback;
        }

        private static int ReadInt(JObject value, string name)
        {
            return value.TryGetValue(name, out JToken token) ? token.Value<int>() : 0;
        }
    }
}
