// Converters/MongoIdConverter.cs
using SPTarkov.Server.Core.Models.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestFilterMod.Converters
{
    public class MongoIdConverter : JsonConverter<MongoId>
    {
        public override MongoId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new MongoId(reader.GetString());
            }
            throw new JsonException("Ожидалась строка для MongoId");
        }

        public override void Write(Utf8JsonWriter writer, MongoId value, JsonSerializerOptions options)
        {
            if (value == null || value.IsEmpty)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value.ToString()); // "579dc571d53a0658a154fbec"
            }
        }
    }
}