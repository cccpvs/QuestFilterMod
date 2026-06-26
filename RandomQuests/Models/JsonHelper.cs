using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestFilterMod.RandomQuests.Utils
{
    public static class JsonHelper
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        static JsonHelper()
        {
            Options.Converters.Add(new ObjectConverter());
        }
        public static T LoadFromJson<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Config not found: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        public static string ToJson(object obj)
        {
            try
            {
                return JsonSerializer.Serialize(obj, obj.GetType(), Options);
            }
            catch (Exception e)
            {
                return $"[JSON ERROR] {e.Message}";
            }
        }
    }

    public class ObjectConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Deserialization not required here.");
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            switch (value)
            {
                case string _:
                case bool _:
                case int _:
                case long _:
                case float _:
                case double _:
                case decimal _:
                    JsonSerializer.Serialize(writer, value);
                    break;

                case DateTime dt:
                    writer.WriteStringValue(dt.ToUniversalTime().ToString("O"));
                    break;
                case Guid guid:
                    writer.WriteStringValue(guid.ToString());
                    break;
                case Enum enumValue:
                    writer.WriteStringValue(enumValue.ToString());
                    break;
                case IDictionary<string, object> dict:
                    WriteDictionary(writer, dict, options);
                    break;
                case IEnumerable enumerable when !(value is string):
                    WriteArray(writer, enumerable, options);
                    break;
                default:
                    JsonSerializer.Serialize(writer, value, value.GetType(), options);
                    break;
            }
        }

        private void WriteDictionary(Utf8JsonWriter writer, IDictionary<string, object> dict, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kvp in dict)
            {
                writer.WritePropertyName(kvp.Key);
                Write(writer, kvp.Value, options);
            }
            writer.WriteEndObject();
        }

        private void WriteArray(Utf8JsonWriter writer, IEnumerable array, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in array)
            {
                Write(writer, item, options);
            }
            writer.WriteEndArray();
        }
    }
}