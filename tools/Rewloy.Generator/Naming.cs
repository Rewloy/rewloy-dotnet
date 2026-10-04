using System.Text;
using System.Text.Json;

namespace Rewloy.Generator;

/// <summary>The generator refuses what it does not understand, loudly: a regeneration that needs a human fails.</summary>
public sealed class GeneratorException(string message) : Exception($"generate: {message}");

internal static class Naming
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>"programId" → "ProgramId", "kvkk-consent" → "KvkkConsent": each run of letters and digits starts upper-case.</summary>
    public static string Pascal(string name)
    {
        var sb = new StringBuilder(name.Length);
        var up = true;
        foreach (var c in name)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                up = true;
                continue;
            }
            sb.Append(up ? char.ToUpperInvariant(c) : c);
            up = false;
        }
        if (sb.Length == 0) throw new GeneratorException($"\"{name}\" has no letters or digits to make a name from");
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    /// <summary>A parameter name: camelCase, and escaped when it is a C# keyword.</summary>
    public static string Parameter(string name)
    {
        var p = Pascal(name);
        var camel = char.ToLowerInvariant(p[0]) + p[1..];
        return Keywords.Contains(camel) ? "@" + camel : camel;
    }

    public static string Xml(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>A C# string literal (regular, escaped), for text that is not code.</summary>
    public static string Literal(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// An XML doc block: the first paragraph is the summary, the rest go to
    /// remarks (a paragraph each); <paramref name="rawExtra"/> are more remark paragraphs,
    /// already XML (the caller escapes them).
    /// </summary>
    public static void Doc(StringBuilder sb, string indent, string? text, IEnumerable<string>? rawExtra = null, string? summaryFallback = null)
    {
        var paragraphs = (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var summary = paragraphs.Count > 0 ? paragraphs[0] : summaryFallback;
        var remarks = paragraphs.Skip(1).Select(p => (Text: p, Raw: false)).Concat((rawExtra ?? []).Select(p => (Text: p, Raw: true))).ToList();
        if (summary is null && remarks.Count == 0) return;
        if (summary is not null) Block(sb, indent, "summary", summary);
        if (remarks.Count > 0)
        {
            sb.Append(indent).Append("/// <remarks>\n");
            foreach (var (paragraph, raw) in remarks) Block(sb, indent, "para", paragraph, raw);
            sb.Append(indent).Append("/// </remarks>\n");
        }
    }

    private static void Block(StringBuilder sb, string indent, string tag, string text, bool raw = false)
    {
        var body = raw ? text : Xml(text);
        var lines = body.Split('\n').Select(l => l.TrimEnd()).ToArray();
        if (lines.Length == 1)
        {
            sb.Append(indent).Append("/// <").Append(tag).Append('>').Append(lines[0]).Append("</").Append(tag).Append(">\n");
            return;
        }
        sb.Append(indent).Append("/// <").Append(tag).Append(">\n");
        foreach (var line in lines) sb.Append(indent).Append("/// ").Append(line).Append('\n');
        sb.Append(indent).Append("/// </").Append(tag).Append(">\n");
    }
}

internal static class Json
{
    // The element-or-absent helpers come in two forms, so that a chain works whether or not the link before was optional.
    public static JsonElement? Prop(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    public static JsonElement? Prop(this JsonElement? e, string name) => e is { } x ? x.Prop(name) : null;

    public static string? Str(this JsonElement e, string name) => e.Prop(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static string? Str(this JsonElement? e, string name) => e is { } x ? x.Str(name) : null;

    public static bool Flag(this JsonElement e, string name) => e.Prop(name) is { ValueKind: JsonValueKind.True };

    public static bool IsObject(this JsonElement? e) => e is { ValueKind: JsonValueKind.Object };

    public static IEnumerable<JsonProperty> Members(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Object } o ? o.EnumerateObject() : Enumerable.Empty<JsonProperty>();

    public static IEnumerable<JsonProperty> Members(this JsonElement e) => ((JsonElement?)e).Members();

    public static IEnumerable<JsonElement> Items(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public static IEnumerable<JsonElement> Items(this JsonElement e) => ((JsonElement?)e).Items();
}
