// Converters/ListOrTStringConverter.cs
using SPTarkov.Server.Core.Utils.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestFilterMod.Converters
{
    public class ListOrTStringConverter : JsonConverter<ListOrT<string>>
    {
        public override ListOrT<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                return new ListOrT<string>(null, value);
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                return new ListOrT<string>(list, null);
            }

            throw new JsonException($"Unexpected token: {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, ListOrT<string> value, JsonSerializerOptions options)
        {
            if (value?.IsItem == true)
            {
                writer.WriteStringValue(value.Item); // строка
            }
            else if (value?.IsList == true && value.List != null)
            {
                JsonSerializer.Serialize(writer, value.List, options); // массив
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}