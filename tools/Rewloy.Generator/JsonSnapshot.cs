using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rewloy.Generator;

/// <summary>
/// Writes a JSON document the way <c>JSON.stringify(value, null, 2)</c> does,
/// so that the snapshot this tool keeps is byte for byte the one the Node and
/// PHP libraries keep (<c>cmp</c> compares the three repositories').
/// </summary>
public static class JsonSnapshot
{
    public static string Write(JsonElement root)
    {
        var sb = new StringBuilder(capacity: 1 << 21);
        Write(sb, root, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    private static void Write(StringBuilder sb, JsonElement e, int depth)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var any = false;
                foreach (var p in e.EnumerateObject())
                {
                    sb.Append(any ? ",\n" : "{\n");
                    any = true;
                    Indent(sb, depth + 1);
                    Quote(sb, p.Name);
                    sb.Append(": ");
                    Write(sb, p.Value, depth + 1);
                }
                if (!any) sb.Append("{}");
                else
                {
                    sb.Append('\n');
                    Indent(sb, depth);
                    sb.Append('}');
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var any = false;
                foreach (var item in e.EnumerateArray())
                {
                    sb.Append(any ? ",\n" : "[\n");
                    any = true;
                    Indent(sb, depth + 1);
                    Write(sb, item, depth + 1);
                }
                if (!any) sb.Append("[]");
                else
                {
                    sb.Append('\n');
                    Indent(sb, depth);
                    sb.Append(']');
                }
                break;
            }
            case JsonValueKind.String: Quote(sb, e.GetString()!); break;
            case JsonValueKind.Number: sb.Append(e.GetRawText()); break;
            case JsonValueKind.True: sb.Append("true"); break;
            case JsonValueKind.False: sb.Append("false"); break;
            case JsonValueKind.Null: sb.Append("null"); break;
            default: throw new InvalidOperationException($"unexpected JSON value kind {e.ValueKind}");
        }
    }

    /// <summary>JSON.stringify's string escaping: only what JSON requires, non-ASCII left as it is.</summary>
    private static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    var loneSurrogate = char.IsHighSurrogate(c)
                        ? i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])
                        : char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(s[i - 1]));
                    if (c < 0x20 || loneSurrogate) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
