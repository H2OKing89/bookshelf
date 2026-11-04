using System;
using System.Globalization;
using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.MyAnonaMouse
{
    public class MyAnonaMouseFlagConverter : JsonConverter<bool>
    {
        public override bool ReadJson(JsonReader reader, Type objectType, bool existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return false;
            }

            if (bool.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture), out var boolean))
            {
                return boolean;
            }

            if (long.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number != 0;
            }

            throw new JsonSerializationException("Unexpected MAM flag value.");
        }

        public override void WriteJson(JsonWriter writer, bool value, JsonSerializer serializer) => writer.WriteValue(value);
    }
}
