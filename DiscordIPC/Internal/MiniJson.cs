using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiscordIPC.Internal
{
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            using (Parser parser = new Parser(json)) return parser.ParseValue();
        }

        public static string Serialize(object obj)
        {
            StringBuilder builder = new StringBuilder(256);
            Serializer.SerializeValue(obj, builder);
            return builder.ToString();
        }

        private sealed class Parser : IDisposable
        {
            private readonly StringReader _reader;

            public Parser(string json)
            {
                _reader = new StringReader(json);
            }

            public void Dispose()
            {
                _reader.Dispose();
            }

            public object ParseValue()
            {
                EatWhitespace();
                int c = _reader.Peek();
                if (c == -1) return null;

                switch ((char)c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': return ParseLiteral("true", true);
                    case 'f': return ParseLiteral("false", false);
                    case 'n': return ParseLiteral("null", null);
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> table = new Dictionary<string, object>();
                _reader.Read();

                while (true)
                {
                    EatWhitespace();
                    int c = _reader.Peek();
                    if (c == -1) throw new FormatException("Unexpected end of JSON object.");
                    if ((char)c == '}')
                    {
                        _reader.Read();
                        return table;
                    }

                    string key = ParseString();
                    EatWhitespace();
                    if (_reader.Read() != ':') throw new FormatException("Expected ':' in JSON object.");
                    object value = ParseValue();
                    table[key] = value;

                    EatWhitespace();
                    c = _reader.Read();
                    if (c == '}') return table;
                    if (c != ',') throw new FormatException("Expected ',' in JSON object.");
                }
            }

            private List<object> ParseArray()
            {
                List<object> array = new List<object>();
                _reader.Read();

                while (true)
                {
                    EatWhitespace();
                    int c = _reader.Peek();
                    if (c == -1) throw new FormatException("Unexpected end of JSON array.");
                    if ((char)c == ']')
                    {
                        _reader.Read();
                        return array;
                    }

                    array.Add(ParseValue());
                    EatWhitespace();
                    c = _reader.Read();
                    if (c == ']') return array;
                    if (c != ',') throw new FormatException("Expected ',' in JSON array.");
                }
            }

            private string ParseString()
            {
                EatWhitespace();
                if (_reader.Read() != '"') throw new FormatException("Expected JSON string.");

                StringBuilder s = new StringBuilder();
                while (true)
                {
                    int next = _reader.Read();
                    if (next == -1) throw new FormatException("Unterminated JSON string.");
                    char c = (char)next;
                    if (c == '"') return s.ToString();
                    if (c != '\\')
                    {
                        s.Append(c);
                        continue;
                    }

                    int escaped = _reader.Read();
                    if (escaped == -1) throw new FormatException("Unterminated JSON escape.");
                    switch ((char)escaped)
                    {
                        case '"': s.Append('"'); break;
                        case '\\': s.Append('\\'); break;
                        case '/': s.Append('/'); break;
                        case 'b': s.Append('\b'); break;
                        case 'f': s.Append('\f'); break;
                        case 'n': s.Append('\n'); break;
                        case 'r': s.Append('\r'); break;
                        case 't': s.Append('\t'); break;
                        case 'u':
                            char[] hex = new char[4];
                            if (_reader.Read(hex, 0, 4) != 4) throw new FormatException("Invalid unicode escape.");
                            s.Append((char)Convert.ToInt32(new string(hex), 16));
                            break;
                        default: throw new FormatException("Invalid JSON escape.");
                    }
                }
            }

            private object ParseNumber()
            {
                StringBuilder number = new StringBuilder();
                while (true)
                {
                    int c = _reader.Peek();
                    if (c == -1) break;
                    char ch = (char)c;
                    if (!(char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == 'e' || ch == 'E')) break;
                    number.Append((char)_reader.Read());
                }

                string text = number.ToString();
                if (text.Length == 0) throw new FormatException("Invalid JSON value.");
                long integer;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer;
                double real;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out real)) return real;
                throw new FormatException("Invalid JSON number: " + text);
            }

            private object ParseLiteral(string literal, object value)
            {
                for (int i = 0; i < literal.Length; i++)
                {
                    if (_reader.Read() != literal[i]) throw new FormatException("Invalid JSON literal.");
                }
                return value;
            }

            private void EatWhitespace()
            {
                while (_reader.Peek() != -1 && char.IsWhiteSpace((char)_reader.Peek())) _reader.Read();
            }
        }

        private static class Serializer
        {
            public static void SerializeValue(object value, StringBuilder builder)
            {
                if (value == null) { builder.Append("null"); return; }

                string text = value as string;
                if (text != null) { SerializeString(text, builder); return; }

                if (value is bool) { builder.Append((bool)value ? "true" : "false"); return; }

                IDictionary dictionary = value as IDictionary;
                if (dictionary != null) { SerializeObject(dictionary, builder); return; }

                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null) { SerializeArray(enumerable, builder); return; }

                if (value is char) { SerializeString(value.ToString(), builder); return; }

                if (value is float || value is double || value is decimal)
                {
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }

                if (value is byte || value is sbyte || value is short || value is ushort ||
                    value is int || value is uint || value is long || value is ulong)
                {
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }

                SerializeString(value.ToString(), builder);
            }

            private static void SerializeObject(IDictionary obj, StringBuilder builder)
            {
                bool first = true;
                builder.Append('{');
                foreach (DictionaryEntry entry in obj)
                {
                    if (!first) builder.Append(',');
                    SerializeString(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), builder);
                    builder.Append(':');
                    SerializeValue(entry.Value, builder);
                    first = false;
                }
                builder.Append('}');
            }

            private static void SerializeArray(IEnumerable array, StringBuilder builder)
            {
                bool first = true;
                builder.Append('[');
                foreach (object value in array)
                {
                    if (!first) builder.Append(',');
                    SerializeValue(value, builder);
                    first = false;
                }
                builder.Append(']');
            }

            private static void SerializeString(string str, StringBuilder builder)
            {
                builder.Append('"');
                for (int i = 0; i < str.Length; i++)
                {
                    char c = str[i];
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (c < 32)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4"));
                            }
                            else builder.Append(c);
                            break;
                    }
                }
                builder.Append('"');
            }
        }
    }
}
