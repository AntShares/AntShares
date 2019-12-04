using Neo.IO.Caching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Neo.IO.Json
{
    public class JObject
    {
        public static readonly JObject Null = null;
        public IDictionary<string, JObject> Properties { get; } = new OrderedDictionary<string, JObject>();

        public JObject this[string name]
        {
            get
            {
                Properties.TryGetValue(name, out JObject value);
                return value;
            }
            set
            {
                Properties[name] = value;
            }
        }

        public virtual bool AsBoolean()
        {
            return true;
        }

        public virtual double AsNumber()
        {
            return double.NaN;
        }

        public virtual string AsString()
        {
            return ToString();
        }

        public bool ContainsProperty(string key)
        {
            return Properties.ContainsKey(key);
        }

        public static JObject Parse(ReadOnlySpan<byte> value, int max_nest = 100)
        {
            Utf8JsonReader reader = new Utf8JsonReader(value, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = max_nest
            });
            try
            {
                JObject json = Read(ref reader);
                if (reader.Read()) throw new FormatException();
                return json;
            }
            catch (JsonException ex)
            {
                throw new FormatException(ex.Message, ex);
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static JObject Parse(string value, int max_nest = 100)
        {
            return Parse(Encoding.UTF8.GetBytes(value), max_nest);
        }

        private static JObject Read(ref Utf8JsonReader reader, bool skipReading = false)
        {
            if (!skipReading && !reader.Read()) throw new FormatException();
            return reader.TokenType switch
            {
                JsonTokenType.False => false,
                JsonTokenType.Null => Null,
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.StartArray => ReadArray(ref reader),
                JsonTokenType.StartObject => ReadObject(ref reader),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.True => true,
                _ => throw new FormatException(),
            };
        }

        private static JArray ReadArray(ref Utf8JsonReader reader)
        {
            JArray array = new JArray();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.EndArray:
                        return array;
                    default:
                        array.Add(Read(ref reader, skipReading: true));
                        break;
                }
            }
            throw new FormatException();
        }

        private static JObject ReadObject(ref Utf8JsonReader reader)
        {
            JObject obj = new JObject();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.EndObject:
                        return obj;
                    case JsonTokenType.PropertyName:
                        string name = reader.GetString();
                        if (obj.Properties.ContainsKey(name)) throw new FormatException();
                        JObject value = Read(ref reader);
                        obj.Properties.Add(name, value);
                        break;
                    default:
                        throw new FormatException();
                }
            }
            throw new FormatException();
        }

        public override string ToString()
        {
            return ToString(false);
        }

        public string ToString(bool indented)
        {
            using MemoryStream ms = new MemoryStream();
            using Utf8JsonWriter writer = new Utf8JsonWriter(ms, new JsonWriterOptions
            {
                Indented = indented,
                SkipValidation = true
            });
            Write(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public virtual T TryGetEnum<T>(T defaultValue = default, bool ignoreCase = false) where T : Enum
        {
            return defaultValue;
        }

        internal virtual void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, JObject> pair in Properties)
            {
                writer.WritePropertyName(pair.Key);
                if (pair.Value is null)
                    writer.WriteNullValue();
                else
                    pair.Value.Write(writer);
            }
            writer.WriteEndObject();
        }

        public static implicit operator JObject(Enum value)
        {
            return (JString)value;
        }

        public static implicit operator JObject(JObject[] value)
        {
            return (JArray)value;
        }

        public static implicit operator JObject(bool value)
        {
            return (JBoolean)value;
        }

        public static implicit operator JObject(double value)
        {
            return (JNumber)value;
        }

        public static implicit operator JObject(string value)
        {
            return (JString)value;
        }
    }
}
