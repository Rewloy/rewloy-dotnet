using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rewloy.Generator;

/// <summary>
/// Turns JSON Schemas into C# classes and type names.
///
/// The choices (docs/DECISIONS.md explains them):
///   - an object schema with properties becomes a sealed class of get/set
///     properties, so that it compiles in every C# version a .NET Framework
///     project may use and fills step by step in ERP code; it derives from
///     RewloyObject, which keeps the fields the API adds before the next
///     regeneration;
///   - a union of objects (oneOf of different shapes, such as the two answers
///     of passAction) becomes one class holding every member's properties:
///     those not in every member are nullable and say which member sends
///     them; the members' own descriptions head the class. Two members that
///     give one property different shapes make the union a <c>JsonElement</c>;
///   - a free-form object, an unknown type and any other union stay
///     <c>JsonElement</c>;
///   - enums stay string (and integer enums stay numbers), the values in the
///     doc comment: an enum type would throw on the day the API adds a value.
/// </summary>
internal sealed class ModelBuilder
{
    internal readonly record struct Mapped(string Type, bool IsValue, bool Nullable);

    private sealed class PropDecl
    {
        public required string JsonName { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required string? Doc { get; init; }
        public string? Obsolete { get; init; }
        public string? Initializer { get; init; }
        public bool OptionalWrapper { get; init; }
        public required bool IsQueryList { get; init; }
    }

    private sealed class ClassDecl
    {
        public required string Name { get; init; }
        public required string? Doc { get; init; }
        public required bool IsQuery { get; init; }
        public List<PropDecl> Props { get; } = [];
        public string? PagedOp { get; init; }
        public string? PageType { get; set; }
    }

    private static readonly HashSet<string> ObjectMembers = new(StringComparer.Ordinal)
    {
        "GetType", "ToString", "Equals", "GetHashCode", "MemberwiseClone", "Finalize", "AdditionalProperties", "WriteTo", "ForPage",
    };

    private readonly List<ClassDecl> _classes = [];
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimable;
    private readonly Dictionary<string, string> _components = new(StringComparer.Ordinal);

    public ModelBuilder(IEnumerable<string> claimable)
    {
        _claimable = new HashSet<string>(claimable, StringComparer.Ordinal);
    }

    private string Reserve(string hint)
    {
        if (ApiGenerator.ReservedTypes.Contains(hint)) throw new GeneratorException($"type name {hint} collides with a library type");
        if (_claimable.Remove(hint))
        {
            _used.Add(hint);
            return hint;
        }
        var name = hint;
        for (var i = 2; _used.Contains(name) || _claimable.Contains(name) || ApiGenerator.ReservedTypes.Contains(name); i++) name = hint + i.ToString(CultureInfo.InvariantCulture);
        _used.Add(name);
        return name;
    }

    public void AddComponents(JsonElement? components)
    {
        foreach (var c in components.Members())
        {
            if (c.Name == "Error") continue; // RewloyException carries it
            var name = Naming.Pascal(c.Name);
            var m = Map(c.Value, name, request: false, $"component {c.Name}");
            _components[c.Name] = m.Type;
        }
    }

    // ------------------------------------------------------------------ schema → type

    private static readonly Mapped JsonElementType = new("JsonElement", true, false);

    /// <summary>The C# type of a path parameter: a Guid for a uuid, a long for an integer, a string otherwise.</summary>
    public string MapScalar(JsonElement schema, string where)
    {
        var m = Map(schema, "unused", request: true, where);
        if (m.Type is "string" or "Guid" or "long" or "int") return m.Type;
        throw new GeneratorException($"{where}: a path parameter of type {m.Type} is not supported");
    }

    public Mapped Map(JsonElement schema, string hint, bool request, string where)
    {
        if (schema.ValueKind != JsonValueKind.Object) return JsonElementType; // `true`, `{}` or absent: anything
        var nullable = schema.Flag("nullable");
        var m = MapCore(schema, hint, request, where);
        if (nullable) m = m with { Nullable = true };
        return m;
    }

    private Mapped MapCore(JsonElement schema, string hint, bool request, string where)
    {
        if (schema.Str("$ref") is { } reference)
        {
            const string prefix = "#/components/schemas/";
            if (!reference.StartsWith(prefix, StringComparison.Ordinal) || !_components.TryGetValue(reference[prefix.Length..], out var type)) throw new GeneratorException($"{where}: unsupported $ref {reference}");
            return new Mapped(type, false, false);
        }
        if (schema.Prop("const") is { } constant) return ConstType(constant);
        if (schema.Prop("allOf") is not null) throw new GeneratorException($"{where}: allOf is not supported");
        var union = schema.Prop("oneOf") ?? schema.Prop("anyOf");
        if (union is not null) return MapUnion(union.Value, hint, request, where);

        var nullInTypes = false;
        var types = new List<string>();
        switch (schema.Prop("type"))
        {
            case { ValueKind: JsonValueKind.String } t: types.Add(t.GetString()!); break;
            case { ValueKind: JsonValueKind.Array } ta:
                foreach (var t in ta.EnumerateArray()) types.Add(t.GetString() ?? throw new GeneratorException($"{where}: a type that is not a string"));
                break;
            case null:
                if (schema.Prop("properties") is not null || schema.Prop("additionalProperties") is not null) types.Add("object");
                else if (schema.Prop("items") is not null) types.Add("array");
                else if (schema.Prop("enum") is { ValueKind: JsonValueKind.Array } e && e.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String)) types.Add("string");
                break;
            default: throw new GeneratorException($"{where}: a type that is neither a string nor a list");
        }
        if (types.Remove("null")) nullInTypes = true;
        types = types.Distinct().ToList();
        Mapped result;
        if (types.Count != 1) result = JsonElementType;
        else
        {
            result = types[0] switch
            {
                "string" => schema.Str("format") switch
                {
                    "uuid" => new Mapped("Guid", true, false),
                    "date-time" => new Mapped("DateTimeOffset", true, false),
                    _ => new Mapped("string", false, false),
                },
                "integer" => new Mapped(IntegerType(schema), true, false),
                "number" => new Mapped("double", true, false),
                "boolean" => new Mapped("bool", true, false),
                "array" => MapArray(schema, hint, request, where),
                "object" => MapObject(schema, hint, request, where),
                _ => JsonElementType,
            };
        }
        return nullInTypes ? result with { Nullable = true } : result;
    }

    private static Mapped ConstType(JsonElement constant) => constant.ValueKind switch
    {
        JsonValueKind.String => new Mapped("string", false, false),
        JsonValueKind.True or JsonValueKind.False => new Mapped("bool", true, false),
        JsonValueKind.Number => new Mapped(constant.TryGetInt64(out _) ? "long" : "double", true, false),
        _ => JsonElementType,
    };

    /// <summary>int when the schema bounds the number inside Int32 on both sides, long otherwise.</summary>
    private static string IntegerType(JsonElement schema)
    {
        if (schema.Prop("minimum") is { ValueKind: JsonValueKind.Number } min && schema.Prop("maximum") is { ValueKind: JsonValueKind.Number } max
            && min.TryGetInt64(out var lo) && max.TryGetInt64(out var hi) && lo >= int.MinValue && hi <= int.MaxValue) return "int";
        return "long";
    }

    private Mapped MapUnion(JsonElement members, string hint, bool request, string where)
    {
        if (MergeObjects(members) is { } merged) return MapObject(merged, hint, request, where);
        var types = new HashSet<string>(StringComparer.Ordinal);
        var nullable = false;
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Object) return JsonElementType;
            // A member with a shape (object, array, nested union) makes this a real union: JsonElement.
            if (member.Prop("properties") is not null || member.Prop("items") is not null || member.Prop("oneOf") is not null
                || member.Prop("anyOf") is not null || member.Prop("$ref") is not null || member.Str("type") is "object" or "array") return JsonElementType;
            if (member.Str("type") == "null")
            {
                nullable = true;
                continue;
            }
            // Strings of different formats (a date, a date-time) are strings alike.
            var m = member.Str("type") == "string" ? new Mapped("string", false, false) : MapCore(member, "union", request: false, where);
            if (m.Type == "JsonElement") return JsonElementType;
            types.Add(m.Type);
        }
        if (types.Count != 1) return JsonElementType;
        var only = types.First();
        return new Mapped(only, only is not "string", nullable);
    }

    /// <summary>
    /// The one object schema of a union whose members are all objects with properties: every property once, required
    /// only when every member requires it, and told apart by a note which members send it. Null when the union is
    /// something else, or two members give one property different shapes.
    /// </summary>
    private static JsonElement? MergeObjects(JsonElement members)
    {
        var list = members.EnumerateArray().ToList();
        if (list.Count < 2 || list.Any(m => m.ValueKind != JsonValueKind.Object || m.Prop("properties") is not { ValueKind: JsonValueKind.Object }
            || m.Prop("oneOf") is not null || m.Prop("anyOf") is not null || m.Prop("allOf") is not null || m.Prop("$ref") is not null
            || m.Prop("nullable") is not null || m.Str("type") != "object")) return null;

        // The member's title; else the property that tells the members apart (`kind` = "staff"); else its number.
        string Label(int i) => list[i].Str("title") is { Length: > 0 } t ? t
            : list[i].Prop("properties").Members().FirstOrDefault(p => p.Value.Prop("const") is { ValueKind: JsonValueKind.String }) is { Value.ValueKind: JsonValueKind.Object } d
                ? $"{d.Name} = \"{d.Value.Prop("const")!.Value.GetString()}\""
                : $"option {i + 1}";
        var order = new List<string>();
        var seen = new Dictionary<string, List<(int Member, JsonElement Schema)>>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++)
        {
            foreach (var p in list[i].Prop("properties").Members())
            {
                if (!seen.TryGetValue(p.Name, out var at)) seen[p.Name] = at = [];
                if (at.Count == 0) order.Add(p.Name);
                at.Add((i, p.Value));
            }
        }

        var required = list.Select(m => new HashSet<string>(m.Prop("required").Items().Select(r => r.GetString()!), StringComparer.Ordinal)).ToList();
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("type", "object");
            w.WriteStartArray("required");
            foreach (var name in order) if (required.TrueForAll(r => r.Contains(name))) w.WriteStringValue(name);
            w.WriteEndArray();
            var variants = string.Join("\n\n", Enumerable.Range(0, list.Count).Select(i =>
                $"{(i + 1).ToString(CultureInfo.InvariantCulture)}. {Label(i)}" + (list[i].Str("description") is { Length: > 0 } d ? $": {d}" : string.Empty)));
            w.WriteString("description", $"An answer of one of {list.Count.ToString(CultureInfo.InvariantCulture)} shapes; the properties that are not in every shape are null in the others.\n\n{variants}");
            w.WriteStartObject("properties");
            foreach (var name in order)
            {
                var at = seen[name];
                JsonElement schema = at[0].Schema;
                if (at.Count > 1 && !at.Skip(1).All(a => a.Schema.GetRawText() == schema.GetRawText()))
                {
                    // Different values of one kind (a `const` per member, say) are that kind; anything else is not merged.
                    var kinds = at.Select(a => ScalarKind(a.Schema)).ToList();
                    if (kinds.Any(k => k.Kind != "string")) return null;
                    schema = JsonDocument.Parse(kinds.Any(k => k.Nullable) ? "{\"type\":[\"string\",\"null\"]}" : "{\"type\":\"string\"}").RootElement;
                }
                var note = at.Count == list.Count ? null : $"Only in: {string.Join(", ", at.Select(a => Label(a.Member)))}.";
                w.WritePropertyName(name);
                WriteSchemaWithNote(w, schema, note);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement;
    }

    /// <summary>The one JSON type of a scalar schema (a <c>const</c> or an enum counts by its values), and whether null is allowed.</summary>
    private static (string? Kind, bool Nullable) ScalarKind(JsonElement schema)
    {
        if (schema.Prop("const") is { ValueKind: JsonValueKind.String }) return ("string", false);
        var types = schema.Prop("type") switch
        {
            { ValueKind: JsonValueKind.String } t => [t.GetString()!],
            { ValueKind: JsonValueKind.Array } ta => ta.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList(),
            _ => new List<string>(),
        };
        var nullable = types.Remove("null");
        return types.Count == 1 ? (types[0], nullable) : (null, nullable);
    }

    private static void WriteSchemaWithNote(Utf8JsonWriter w, JsonElement schema, string? note)
    {
        if (note is null) { schema.WriteTo(w); return; }
        w.WriteStartObject();
        foreach (var p in schema.EnumerateObject())
        {
            if (p.Name == "description") continue;
            p.WriteTo(w);
        }
        var description = schema.Str("description");
        w.WriteString("description", description is null ? note : $"{description}\n\n{note}");
        w.WriteEndObject();
    }

    private Mapped MapArray(JsonElement schema, string hint, bool request, string where)
    {
        var item = Map(schema.Prop("items") ?? default, hint + "Item", request, where + "[]");
        var inner = item.Type + (item.Nullable ? "?" : string.Empty);
        return new Mapped($"IReadOnlyList<{inner}>", false, false);
    }

    private Mapped MapObject(JsonElement schema, string hint, bool request, string where)
    {
        var props = schema.Prop("properties");
        var extra = schema.Prop("additionalProperties");
        var hasProps = props.Members().Any();
        if (!hasProps && !props.IsObject() && extra is not { ValueKind: JsonValueKind.False })
        {
            // No properties declared: a map of one type of value, or anything.
            if (extra is { ValueKind: JsonValueKind.Object } valueSchema)
            {
                var value = Map(valueSchema, hint + "Value", request, where + "{}");
                return new Mapped($"IReadOnlyDictionary<string, {value.Type}{(value.Nullable ? "?" : string.Empty)}>", false, false);
            }
            return JsonElementType;
        }

        var name = Reserve(hint);
        var decl = new ClassDecl { Name = name, Doc = schema.Str("description"), IsQuery = false };
        _classes.Add(decl);
        var required = new HashSet<string>(schema.Prop("required").Items().Select(r => r.GetString()!), StringComparer.Ordinal);
        var taken = new HashSet<string>(ObjectMembers, StringComparer.Ordinal) { name };
        foreach (var p in props.Members())
        {
            decl.Props.Add(MakeProp(p.Name, p.Value, required.Contains(p.Name), name, request, taken, $"{where}.{p.Name}"));
        }
        return new Mapped(name, false, false);
    }

    private PropDecl MakeProp(string jsonName, JsonElement schema, bool isRequired, string className, bool request, HashSet<string> taken, string where, string? fallbackDoc = null)
    {
        var csName = Naming.Pascal(jsonName);
        if (!taken.Add(csName))
        {
            var n = csName;
            for (var i = 2; !taken.Add(n = csName + i.ToString(CultureInfo.InvariantCulture)); i++) { }
            csName = n;
        }
        var m = Map(schema, className + Naming.Pascal(jsonName), request, where);
        var optional = !isRequired;
        string type;
        var optionalWrapper = false;
        string? init = null;
        if (request && m.Nullable)
        {
            // `null` is a JSON value the API reads: keep it apart from "left out".
            type = $"Optional<{m.Type}>";
            optionalWrapper = true;
        }
        else if (request && m.Type == "JsonElement")
        {
            type = "JsonElement?"; // a default JsonElement cannot be written
        }
        else
        {
            type = optional || m.Nullable ? m.Type + "?" : m.Type;
            if (!optional && !m.Nullable && !m.IsValue) init = m.Type.StartsWith("IReadOnlyList<", StringComparison.Ordinal) ? $"Array.Empty<{m.Type[14..^1]}>()" : "default!";
        }

        string? obsolete = null;
        if (schema.Flag("deprecated"))
        {
            var d = schema.Prop("x-deprecation");
            var sunset = d?.Str("sunset") ?? schema.Str("x-sunset");
            var use = d?.Str("use") ?? schema.Str("x-replacement");
            obsolete = $"{jsonName} is deprecated" + (sunset is null ? "." : $"; the API stops sending it after {sunset}.") + (use is null ? string.Empty : $" Use {use} instead.");
        }

        var doc = schema.Str("description") ?? fallbackDoc;
        var notes = new List<string>();
        if (schema.Prop("enum") is { ValueKind: JsonValueKind.Array } e)
        {
            notes.Add("One of: " + string.Join(", ", e.EnumerateArray().Select(v => v.ValueKind switch { JsonValueKind.String => $"`{v.GetString()}`", JsonValueKind.Null => "null", _ => v.ToString() })) + ".");
        }
        if (schema.Prop("const") is { } c) notes.Add($"Always `{c.ToString()}`.");
        if (isRequired && !(request && m.Nullable)) notes.Add(request ? "Required." : "Always present.");
        if (request && m.Nullable) notes.Add("`Optional<T>` tells a left-out field from an explicit `null`: set `Optional<T>.Null` to send `null`.");
        var text = string.Join("\n\n", new[] { doc }.Concat(notes).Where(t => !string.IsNullOrEmpty(t)));
        return new PropDecl
        {
            JsonName = jsonName, Name = csName, Type = type, Doc = text.Length == 0 ? null : text, Obsolete = obsolete,
            Initializer = init, OptionalWrapper = optionalWrapper, IsQueryList = m.Type.StartsWith("IReadOnlyList<", StringComparison.Ordinal),
        };
    }

    /// <summary>A query class: the operation's query parameters as nullable properties and the code that writes them.</summary>
    public string AddQuery(string hint, List<(string Name, JsonElement Schema, bool Required, string? Description)> parameters, bool paged, string opId)
    {
        var name = Reserve(hint);
        var decl = new ClassDecl { Name = name, Doc = $"Query parameters of `{opId}`.", IsQuery = true, PagedOp = paged ? opId : null };
        _classes.Add(decl);
        var taken = new HashSet<string>(ObjectMembers, StringComparer.Ordinal) { name };
        foreach (var (pname, schema, required, description) in parameters)
        {
            var m = Map(schema, name + Naming.Pascal(pname), request: true, $"{opId} query {pname}");
            if (m.Type is not ("string" or "bool" or "int" or "long" or "double" or "Guid" or "DateTimeOffset") && !m.Type.StartsWith("IReadOnlyList<", StringComparison.Ordinal))
            {
                throw new GeneratorException($"{opId}: query parameter \"{pname}\" of type {m.Type} is not supported");
            }
            var prop = MakeProp(pname, schema, required, name, request: true, taken, $"{opId} query {pname}", description);
            // A query parameter is never `Optional<T>` (it cannot be null) nor required-null.
            decl.Props.Add(prop);
            if (paged && pname == "page")
            {
                decl.PageType = m.Type;
                if (m.Type is not ("int" or "long")) throw new GeneratorException($"{opId}: the page parameter is a {m.Type}, not an integer");
            }
        }
        if (paged && decl.PageType is null) throw new GeneratorException($"{opId} is a paged list but takes no page parameter");
        return name;
    }

    // ------------------------------------------------------------------ Models.g.cs

    public string Emit(string apiVersion)
    {
        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tools/Rewloy.Generator from the Rewloy OpenAPI document\n");
        sb.Append($"// (openapi/openapi.json, API {apiVersion}). Do not edit: run `dotnet run --project tools/Rewloy.Generator -- --file openapi/openapi.json`.\n");
        sb.Append("// </auto-generated>\n#nullable enable\n#pragma warning disable CS0618 // obsolete members are generated on purpose\n#pragma warning disable CS1591 // missing XML comment\n\n");
        sb.Append("using System;\nusing System.Collections.Generic;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n");
        sb.Append("namespace Rewloy.Models\n{");
        foreach (var c in _classes)
        {
            sb.Append('\n');
            Naming.Doc(sb, "    ", c.Doc, summaryFallback: $"The `{c.Name}` object.");
            sb.Append($"    public sealed class {c.Name} : {(c.IsQuery ? "RewloyQuery" : "RewloyObject")}\n    {{");
            foreach (var p in c.Props)
            {
                sb.Append('\n');
                Naming.Doc(sb, "        ", p.Doc, summaryFallback: $"`{p.JsonName}`.");
                if (!c.IsQuery)
                {
                    sb.Append($"        [JsonPropertyName({Naming.Literal(p.JsonName)})]\n");
                    if (p.OptionalWrapper) sb.Append("        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]\n");
                }
                if (p.Obsolete is not null) sb.Append($"        [Obsolete({Naming.Literal(p.Obsolete)})]\n");
                sb.Append($"        public {p.Type} {p.Name} {{ get; set; }}{(p.Initializer is null ? string.Empty : " = " + p.Initializer + ";")}\n");
            }
            if (c.IsQuery)
            {
                sb.Append("\n        internal override void WriteTo(QueryWriter writer)\n        {\n");
                foreach (var p in c.Props) sb.Append($"            writer.{(p.IsQueryList ? "AddMany" : "Add")}({Naming.Literal(p.JsonName)}, {p.Name});\n");
                sb.Append("        }\n");
                if (c.PagedOp is not null)
                {
                    sb.Append($"\n        /// <summary>A copy of these parameters asking for another page.</summary>\n        internal {c.Name} ForPage(long page)\n        {{\n");
                    sb.Append($"            var copy = ({c.Name})MemberwiseClone();\n            copy.Page = {(c.PageType == "int" ? "checked((int)page)" : "page")};\n            return copy;\n        }}\n");
                }
            }
            sb.Append("    }\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }
}
