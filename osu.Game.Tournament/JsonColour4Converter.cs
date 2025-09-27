// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using Newtonsoft.Json;

namespace osu.Game.Tournament
{
    public class JsonColour4Converter : JsonConverter<Colour4>
    {
        public override void WriteJson(JsonWriter writer, Colour4 value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, new { value.R, value.G, value.B, value.A });
        }

        public override Colour4 ReadJson(JsonReader reader, Type objectType, Colour4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            float r = 0, g = 0, b = 0, a = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject) break;

                if (reader.TokenType == JsonToken.PropertyName)
                {
                    string? name = reader.Value?.ToString();
                    double? val = reader.ReadAsDouble();

                    if (name == null || val == null)
                        continue;

                    switch (name)
                    {
                        case "R":
                            r = (float)val.Value;
                            break;

                        case "G":
                            g = (float)val.Value;
                            break;

                        case "B":
                            b = (float)val.Value;
                            break;

                        case "A":
                            a = (float)val.Value;
                            break;
                    }
                }
            }

            return new Colour4(r, g, b, a);
        }
    }
}
