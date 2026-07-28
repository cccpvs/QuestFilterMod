//JsonHelper.cs

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

        /// <summary>
        /// Provides utility methods for loading and serializing JSON with custom formatting and type handling.
        /// Supports complex nested objects, dictionaries, collections, and enums.
        /// </summary>
        static JsonHelper()
        {
            Options.Converters.Add(new ObjectConverter());
        }

        /// <summary>
        /// Loads and deserializes a JSON file into a strongly-typed object.
        /// Throws FileNotFoundError if the file does not exist.
        /// </summary>
        /// <param name="filePath">Path to the JSON file.</param>
        /// <typeparam name="T">Target type to deserialize to.</typeparam>
        /// <returns>Instance of type T populated from JSON data.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
        public static T LoadFromJson<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Config not found: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, Options);
        }


        // <summary>
        /// Serializes an object to a formatted JSON string using custom options (indented, null-ignored).
        /// Falls back to error message on serialization failure.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        /// <returns>Formatted JSON string, or "[JSON ERROR] {message}" on failure.</returns>
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

    /// <summary>
    /// Custom JSON converter handling arbitrary objects (e.g., IDictionary, IEnumerable, DateTime, Enum).
    /// Used to ensure correct serialization of complex and mixed-type objects in configuration.
    /// Deserialization is intentionally disabled.
    /// </summary>
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