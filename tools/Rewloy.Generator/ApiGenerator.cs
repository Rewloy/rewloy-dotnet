using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rewloy.Generator;

public sealed record GeneratedFile(string Path, string Content);

/// <summary>
/// Rewloy's OpenAPI 3.1 document in, the C# of src/Rewloy/Generated/ out.
/// Pure (no I/O), so that a test can run it on the committed snapshot and
/// compare (GenerationTests).
///
/// It reads only what the document says, the way the platform writes it:
/// inline JSON Schemas with <c>PageMeta</c> and <c>Error</c> as the only
/// components, <c>x-credentials</c> for the credential kinds, the
/// <c>Rewloy-Merchant</c> and <c>Idempotency-Key</c> header parameters, the
/// <c>{ data[, meta] }</c> envelope and one example per error code.
///
/// Output (deterministic: the document's order, no dates):
///   Models.g.cs       a class per object schema: request bodies, query objects, answers
///   Operations.g.cs   the metadata table (RewloyOperations)
///   ErrorCode.g.cs    every error code, with its title
///   RewloyClient.g.cs one method per operationId (plus the whole-answer and paging variants)
/// </summary>
public static class ApiGenerator
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];
    private const string MerchantHeader = "rewloy-merchant";
    private const string IdempotencyHeader = "idempotency-key";
    private const string Reference = "https://rewloy.com/gelistiriciler/api";
    private const string Changelog = "https://rewloy.com/gelistiriciler/degisiklikler";

    private static readonly string[] TrMonths = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

    /// <summary>Names the hand-written library defines, in namespace Rewloy or Rewloy.Models: no generated name may take them.</summary>
    internal static readonly HashSet<string> ReservedTypes = new(StringComparer.Ordinal)
    {
        "RewloyClient", "RewloyClientOptions", "RequestOptions", "RewloyResponse", "Page", "RewloyFile", "RewloyException",
        "RateLimitException", "RewloyConnectionException", "RewloyTimeoutException", "WebhookSignatureException",
        "WebhookFailure", "Webhook", "WebhookEvent", "PassEventData", "EventStream", "ServerSentEvent", "SseParser",
        "OperationInfo", "Deprecation", "CredentialKinds", "ResponseKind", "IdempotencyMode", "RewloyOperations",
        "ErrorCode", "RewloyObject", "RewloyQuery", "QueryWriter", "Optional", "DeprecationEventArgs", "RewloyJson",
        "RewloyVersion", "RewloyHeaders",
    };

    /// <summary>Method names the client defines itself: an operationId may not take them.</summary>
    private static readonly HashSet<string> ReservedMethods = new(StringComparer.Ordinal)
    {
        "Dispose", "Equals", "GetHashCode", "GetType", "ToString", "PaginateAsync", "InvokeAsync", "InvokeFileAsync",
        "InvokeNoContentAsync", "OpenStream", "Deprecated",
    };

    public static int CountOperations(JsonElement document) =>
        document.Prop("paths").Members().Sum(item => item.Value.Members().Count(m => HttpMethods.Contains(m.Name)));

    // ------------------------------------------------------------------ reading

    private sealed class Param
    {
        public required string Name { get; init; }
        public required JsonElement Schema { get; init; }
        public required bool Required { get; init; }
        public string? Description { get; init; }
    }

    private sealed class Op
    {
        public required string Id { get; init; }
        public required string Type { get; init; }
        public required string Method { get; init; }
        public required string Path { get; init; }
        public required string Tag { get; init; }
        public required string Summary { get; init; }
        public required string Description { get; init; }
        public required List<string> Auth { get; init; }
        public (string? Sunset, string? Use)? Deprecated { get; init; }
        public required List<Param> PathParams { get; init; }
        public required List<Param> QueryParams { get; init; }
        public Param? Merchant { get; init; }
        public Param? Idempotency { get; init; }
        public (JsonElement Schema, bool Required)? Body { get; init; }
        public required string Response { get; init; }
        public required bool Paged { get; init; }
        public JsonElement? DataSchema { get; init; }
        public required List<string> SuccessStatuses { get; init; }
    }

    /// <summary>
    /// No required property at the top level: <c>{}</c> is a valid body. A union (<c>oneOf</c> / <c>anyOf</c>) takes
    /// <c>{}</c> when any of its shapes does; one whose shapes all require something (CreateApiKey's two key shapes) does not.
    /// </summary>
    private static bool RequiredFree(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return false;
        var union = schema.Prop("oneOf") ?? schema.Prop("anyOf");
        if (union is { ValueKind: JsonValueKind.Array } shapes && shapes.GetArrayLength() > 0) return shapes.EnumerateArray().Any(RequiredFree);
        return !(schema.Prop("required") is { ValueKind: JsonValueKind.Array } r && r.GetArrayLength() > 0);
    }

    private static List<Param> ReadParams(JsonElement op, string where) =>
        op.Prop("parameters").Items().Where(p => p.Str("in") == where).Select(p => new Param
        {
            Name = p.Str("name") ?? throw new GeneratorException("a parameter without a name"),
            Schema = p.Prop("schema") ?? default,
            Required = p.Flag("required"),
            Description = p.Str("description") ?? p.Prop("schema")?.Str("description"),
        }).ToList();

    /// <summary>"1 Nisan 2027" (the document's deprecation sentence) → "2027-04-01"; the replacement is "yerine `x`".</summary>
    internal static (string? Sunset, string? Use) ParseDeprecation(string description)
    {
        var date = System.Text.RegularExpressions.Regex.Match(description, $@"(\d{{1,2}}) ({string.Join("|", TrMonths)}) (\d{{4}})");
        var use = System.Text.RegularExpressions.Regex.Match(description, @"yerine `([A-Za-z0-9_]+)`");
        string? sunset = date.Success
            ? $"{date.Groups[3].Value}-{(Array.IndexOf(TrMonths, date.Groups[2].Value) + 1):00}-{date.Groups[1].Value.PadLeft(2, '0')}"
            : null;
        return (sunset, use.Success ? use.Groups[1].Value : null);
    }

    private static Op ReadOp(string path, string method, JsonElement op)
    {
        var id = op.Str("operationId") ?? throw new GeneratorException($"{method.ToUpperInvariant()} {path} has no operationId");
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][A-Za-z0-9]*$")) throw new GeneratorException($"operationId \"{id}\" is not camelCase");

        var headers = ReadParams(op, "header");
        var merchant = headers.FirstOrDefault(h => h.Name.Equals(MerchantHeader, StringComparison.OrdinalIgnoreCase));
        var idempotency = headers.FirstOrDefault(h => h.Name.Equals(IdempotencyHeader, StringComparison.OrdinalIgnoreCase));
        var other = headers.Where(h => h != merchant && h != idempotency).ToList();
        if (other.Count > 0) throw new GeneratorException($"{id}: header parameter \"{other[0].Name}\" is not supported (only Rewloy-Merchant and Idempotency-Key are)");
        if (ReadParams(op, "cookie").Count > 0) throw new GeneratorException($"{id}: cookie parameters are not supported");

        List<string> auth;
        if (op.Prop("x-credentials") is { ValueKind: JsonValueKind.Array } creds) auth = creds.EnumerateArray().Select(c => c.ToString()).ToList();
        else
        {
            auth = [];
            foreach (var s in op.Prop("security").Items().Where(s => s.ValueKind == JsonValueKind.Object))
            {
                var keys = s.EnumerateObject().Select(p => p.Name).ToList();
                if (keys.Count == 0) auth.Add("public");
                foreach (var k in keys) auth.Add(k switch { "apiKey" => "key", "staffSession" => "staff", "holderSession" => "holder", _ => k });
            }
        }
        foreach (var a in auth)
        {
            if (a is not ("key" or "staff" or "holder" or "public")) throw new GeneratorException($"{id}: unknown credential kind \"{a}\"");
        }

        (JsonElement, bool)? body = null;
        if (op.Prop("requestBody") is { ValueKind: JsonValueKind.Object } rb)
        {
            if (rb.Prop("content").Prop("application/json") is not { } json) throw new GeneratorException($"{id}: only application/json request bodies are supported");
            body = (json.Prop("schema") ?? default, rb.Flag("required"));
        }

        var responses = op.Prop("responses");
        if (!responses.IsObject()) throw new GeneratorException($"{id} has no responses");
        var success = responses.Members().Select(r => r.Name).Where(s => s.Length == 3 && s[0] == '2').OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (success.Count == 0) throw new GeneratorException($"{id} has no 2xx response");
        var first = responses!.Value.GetProperty(success[0]);
        var content = first.Prop("content");
        string response;
        var paged = false;
        JsonElement? dataSchema = null;
        if (!content.IsObject() || success[0] == "204") response = "none";
        else
        {
            var (type, media) = content.Members().Select(m => (m.Name, m.Value)).First();
            var schema = media.Prop("schema");
            var isJson = System.Text.RegularExpressions.Regex.IsMatch(type, @"^application/([a-z.+-]+\+)?json\b");
            if (type.StartsWith("text/event-stream", StringComparison.Ordinal)) response = "stream";
            else if (isJson && schema?.Prop("properties")?.Prop("data") is { } data)
            {
                response = "json";
                dataSchema = data;
                paged = schema!.Value.Prop("properties")?.Prop("meta") is not null;
            }
            else response = isJson ? "raw-json" : "blob";
        }

        var description = op.Str("description") ?? string.Empty;
        (string?, string?)? deprecated = null;
        if (op.Flag("deprecated"))
        {
            var structured = op.Prop("x-deprecation");
            deprecated = structured.IsObject() ? (structured!.Value.Str("sunset"), structured.Value.Str("use")) : ParseDeprecation(description);
        }

        return new Op
        {
            Id = id, Type = Naming.Pascal(id), Method = method.ToUpperInvariant(), Path = path,
            Tag = op.Prop("tags").Items().Select(t => t.GetString()).FirstOrDefault() ?? string.Empty,
            Summary = op.Str("summary") ?? string.Empty, Description = description,
            Auth = auth, Deprecated = deprecated,
            PathParams = ReadParams(op, "path"), QueryParams = ReadParams(op, "query"),
            Merchant = merchant, Idempotency = idempotency, Body = body,
            Response = response, Paged = paged, DataSchema = dataSchema, SuccessStatuses = success,
        };
    }

    /// <summary>Error titles, from each error code's example (<c>summary</c> is the catalogue's title).</summary>
    private static Dictionary<string, string> ErrorTitles(JsonElement paths, out List<string> order)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        order = [];
        foreach (var item in paths.Members())
        {
            foreach (var m in item.Value.Members().Where(m => HttpMethods.Contains(m.Name)))
            {
                foreach (var r in m.Value.Prop("responses").Members())
                {
                    foreach (var ex in r.Value.Prop("content")?.Prop("application/json")?.Prop("examples").Members() ?? Enumerable.Empty<JsonProperty>())
                    {
                        var title = ex.Value.Str("summary");
                        if (!string.IsNullOrEmpty(title) && titles.TryAdd(ex.Name, title)) order.Add(ex.Name);
                    }
                }
            }
        }
        return titles;
    }

    // ------------------------------------------------------------------ entry

    public static IReadOnlyList<GeneratedFile> Generate(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) throw new GeneratorException("the document is not an object");
        if (document.Str("openapi") is not { } version3 || !version3.StartsWith("3.", StringComparison.Ordinal)) throw new GeneratorException("not an OpenAPI 3 document");
        var apiVersion = document.Prop("info")?.Str("version") ?? "0.0.0";
        var paths = document.Prop("paths");
        if (!paths.IsObject()) throw new GeneratorException("the document has no paths");
        var components = document.Prop("components")?.Prop("schemas");

        var ops = new List<Op>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in paths.Members())
        {
            foreach (var m in HttpMethods)
            {
                if (item.Value.Prop(m) is not { ValueKind: JsonValueKind.Object } op) continue;
                var o = ReadOp(item.Name, m, op);
                if (!seen.Add(o.Id)) throw new GeneratorException($"operationId \"{o.Id}\" is used twice");
                ops.Add(o);
            }
        }
        if (ops.Count == 0) throw new GeneratorException("the document has no operations");

        // Method names: every generated name must be unique (and not the client's own).
        var methodNames = new HashSet<string>(ReservedMethods, StringComparer.Ordinal);
        foreach (var o in ops)
        {
            var names = new List<string> { $"{o.Type}Async" };
            if (o.Response != "stream") names.Add($"{o.Type}WithResponseAsync");
            if (o.Paged) names.Add($"{o.Type}AllAsync");
            foreach (var n in names)
            {
                if (!methodNames.Add(n)) throw new GeneratorException($"method name {n} (from operationId \"{o.Id}\") collides with another or with the client's own");
            }
            if (ReservedTypes.Contains(o.Type) || o.Type is "All" or "ApiVersion" or "ById") throw new GeneratorException($"operationId \"{o.Id}\" collides with a library name");
        }

        var model = new ModelBuilder(ops.SelectMany(o => new[] { $"{o.Type}Query", $"{o.Type}Body", $"{o.Type}Data", $"{o.Type}Item" }));
        model.AddComponents(components);
        var plans = ops.ToDictionary(o => o.Id, o => PlanOp(o, model));

        var titles = ErrorTitles(paths!.Value, out var titleOrder);
        var codes = ErrorCodes(components, titleOrder);

        return
        [
            new("src/Rewloy/Generated/Models.g.cs", model.Emit(apiVersion)),
            new("src/Rewloy/Generated/Operations.g.cs", EmitOperations(apiVersion, ops)),
            new("src/Rewloy/Generated/ErrorCode.g.cs", EmitErrorCodes(apiVersion, codes, titles)),
            new("src/Rewloy/Generated/RewloyClient.g.cs", EmitClient(apiVersion, ops, plans)),
        ];
    }

    private static List<string> ErrorCodes(JsonElement? components, List<string> fromExamples)
    {
        var enumValues = components?.Prop("Error")?.Prop("properties")?.Prop("error")?.Prop("properties")?.Prop("code")?.Prop("enum").Items()
            .Select(v => v.ToString()).ToList() ?? [];
        return [.. enumValues, .. fromExamples.Where(c => !enumValues.Contains(c))];
    }

    // ------------------------------------------------------------------ planning: the types of one operation

    private sealed class Plan
    {
        public required List<(string Type, string Name, bool Required, string Doc)> Parameters { get; init; }
        public required List<string> PathExpressions { get; init; }
        public string? QueryExpr { get; init; }
        public string? BodyExpr { get; init; }
        /// <summary>What the answer's <c>data</c> is as C# (without the page wrapper).</summary>
        public required string DataType { get; init; }
        public string? ItemType { get; init; }
        public string? QueryType { get; init; }
    }

    private static Plan PlanOp(Op op, ModelBuilder model)
    {
        var parameters = new List<(string Type, string Name, bool Required, string Doc)>();
        var taken = new HashSet<string>(["body", "query", "options", "cancellationToken"], StringComparer.Ordinal);
        var pathExpr = new List<string>();

        // Path parameters, in the order the path names them (the client fills the placeholders in that order).
        var inPath = System.Text.RegularExpressions.Regex.Matches(op.Path, @"\{([^}]+)\}").Select(m => m.Groups[1].Value).ToList();
        if (inPath.Distinct().Count() != inPath.Count || !inPath.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(op.PathParams.Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal)))
        {
            throw new GeneratorException($"{op.Id}: the path's placeholders ({string.Join(", ", inPath)}) and its path parameters differ");
        }
        foreach (var name in inPath)
        {
            var p = op.PathParams.First(x => x.Name == name);
            var m = model.MapScalar(p.Schema, $"{op.Id}.{name}");
            var pname = Naming.Parameter(name);
            if (!taken.Add(pname.TrimStart('@'))) throw new GeneratorException($"{op.Id}: path parameter \"{name}\" collides with another parameter");
            parameters.Add((m, pname, true, p.Description ?? $"The `{name}` of the path."));
            pathExpr.Add(pname);
        }

        string? bodyType = null;
        var bodyRequired = false;
        if (op.Body is { } b)
        {
            var mapped = model.Map(b.Schema, $"{op.Type}Body", request: true, $"{op.Id} body");
            bodyType = mapped.Type;
            bodyRequired = b.Required && !RequiredFree(b.Schema);
        }

        string? queryType = null;
        var queryRequired = false;
        if (op.QueryParams.Count > 0)
        {
            queryType = model.AddQuery($"{op.Type}Query", op.QueryParams.Select(q => (q.Name, q.Schema, q.Required, q.Description)).ToList(), op.Paged, op.Id);
            queryRequired = op.QueryParams.Any(q => q.Required);
        }
        else if (op.Paged) throw new GeneratorException($"{op.Id} is a paged list but takes no page parameter");

        // A parameter can be optional only when everything after it is.
        if (bodyType is not null) parameters.Add((bodyType, "body", bodyRequired || queryRequired, bodyRequired ? "The JSON body." : "The JSON body; left out, `{}` is sent."));
        if (queryType is not null) parameters.Add((queryType, "query", queryRequired, queryRequired ? "The query parameters." : "The query parameters."));

        // The answer.
        string dataType;
        string? itemType = null;
        switch (op.Response)
        {
            case "none": dataType = "void"; break;
            case "blob": dataType = "RewloyFile"; break;
            case "raw-json": dataType = "JsonElement"; break;
            case "stream": dataType = "EventStream"; break;
            default:
            {
                var data = op.DataSchema!.Value;
                var isArray = data.Str("type") == "array";
                if (op.Paged && !isArray) throw new GeneratorException($"{op.Id}: a paged list's data is not an array");
                if (isArray)
                {
                    var items = data.Prop("items");
                    var nullableItems = false;
                    var mapped = model.Map(items ?? default, $"{op.Type}Item", request: false, $"{op.Id} items");
                    nullableItems = mapped.Nullable;
                    itemType = mapped.Type + (nullableItems ? "?" : string.Empty);
                    dataType = $"IReadOnlyList<{itemType}>";
                }
                else
                {
                    var mapped = model.Map(data, $"{op.Type}Data", request: false, $"{op.Id} data");
                    dataType = mapped.Type + (mapped.Nullable ? "?" : string.Empty);
                }
                break;
            }
        }

        return new Plan
        {
            Parameters = parameters, PathExpressions = pathExpr,
            QueryExpr = queryType is null ? null : "query", BodyExpr = bodyType is null ? null : "body",
            DataType = dataType, ItemType = itemType, QueryType = queryType,
        };
    }

    // ------------------------------------------------------------------ Operations.g.cs

    private static string Header(string apiVersion, string usings) => string.Join('\n',
    [
        "// <auto-generated>",
        "// Generated by tools/Rewloy.Generator from the Rewloy OpenAPI document",
        $"// (openapi/openapi.json, API {apiVersion}). Do not edit: run `dotnet run --project tools/Rewloy.Generator -- --file openapi/openapi.json`.",
        "// </auto-generated>",
        "#nullable enable",
        "#pragma warning disable CS0618 // the generated code may call what it marks obsolete",
        "#pragma warning disable CS1591 // missing XML comment",
        "",
        usings,
        "",
    ]);

    private static string Str(string s) => Naming.Literal(s);

    private static string EmitOperations(string apiVersion, List<Op> ops)
    {
        var sb = new StringBuilder(Header(apiVersion, "using System.Collections.Generic;\n"));
        sb.Append("namespace Rewloy\n{\n");
        Naming.Doc(sb, "    ", "The metadata table: per operation, its method and path, the credential kinds it accepts, whether it takes `Rewloy-Merchant` and `Idempotency-Key`, how its answer is read and whether it is paged or deprecated.");
        sb.Append("    public static class RewloyOperations\n    {\n");
        Naming.Doc(sb, "        ", "The version of the API document this was generated from (`info.version`).");
        sb.Append($"        public const string ApiVersion = {Str(apiVersion)};\n");
        foreach (var op in ops)
        {
            var kinds = string.Join(" | ", op.Auth.Select(a => "CredentialKinds." + Naming.Pascal(a)));
            var idem = op.Idempotency is null ? "IdempotencyMode.None" : op.Idempotency.Required ? "IdempotencyMode.Required" : "IdempotencyMode.Optional";
            var inPath = System.Text.RegularExpressions.Regex.Matches(op.Path, @"\{([^}]+)\}").Select(m => Str(m.Groups[1].Value));
            var dep = op.Deprecated is { } d
                ? $"new Deprecation({(d.Sunset is null ? "null" : Str(d.Sunset))}, {(d.Use is null ? "null" : Str(d.Use))})"
                : "null";
            var response = op.Response switch { "json" => "Json", "none" => "None", "blob" => "File", "raw-json" => "RawJson", _ => "Stream" };
            sb.Append('\n');
            Naming.Doc(sb, "        ", $"`{op.Method} {op.Path}`: {op.Summary}");
            sb.Append($"        public static readonly OperationInfo {op.Type} = new OperationInfo(\n");
            var pathNames = inPath.ToList();
            var pathArray = pathNames.Count == 0 ? "Array.Empty<string>()" : $"new string[] {{ {string.Join(", ", pathNames)} }}";
            sb.Append($"            {Str(op.Id)}, {Str(op.Method)}, {Str(op.Path)}, {pathArray}, {(kinds.Length == 0 ? "CredentialKinds.None" : kinds)},\n");
            sb.Append($"            acceptsMerchant: {Bool(op.Merchant is not null)}, idempotency: {idem}, hasBody: {Bool(op.Body is not null)}, response: ResponseKind.{response},\n");
            sb.Append($"            isPaged: {Bool(op.Paged)}, deprecation: {dep});\n");
        }
        sb.Append('\n');
        Naming.Doc(sb, "        ", "Every operation by its operationId.");
        sb.Append("        public static IReadOnlyDictionary<string, OperationInfo> All { get; } = new Dictionary<string, OperationInfo>(StringComparer.Ordinal)\n        {\n");
        foreach (var op in ops) sb.Append($"            [{Str(op.Id)}] = {op.Type},\n");
        sb.Append("        };\n    }\n}\n");
        return sb.ToString().Replace("using System.Collections.Generic;\n", "using System;\nusing System.Collections.Generic;\n", StringComparison.Ordinal);
    }

    private static string Bool(bool b) => b ? "true" : "false";

    // ------------------------------------------------------------------ ErrorCode.g.cs

    private static string EmitErrorCodes(string apiVersion, List<string> codes, Dictionary<string, string> titles)
    {
        var sb = new StringBuilder(Header(apiVersion, "using System;\nusing System.Collections.Generic;\n"));
        sb.Append("namespace Rewloy\n{\n");
        Naming.Doc(sb, "    ", "Every error code the API can answer with (the catalogue: https://rewloy.com/gelistiriciler/hatalar). New codes may be added without notice: keep a default branch. The codes are constants, not an enum, so that an unknown one is still a string.");
        sb.Append("    public static class ErrorCode\n    {\n");
        var used = new HashSet<string>(["All", "Titles", "Title"], StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var name = Naming.Pascal(code.ToLowerInvariant());
            if (!used.Add(name)) throw new GeneratorException($"error code {code} makes a constant name that is taken ({name})");
            Naming.Doc(sb, "        ", titles.TryGetValue(code, out var t) ? t : $"`{code}`");
            sb.Append($"        public const string {name} = {Str(code)};\n\n");
        }
        Naming.Doc(sb, "        ", "Every code, in the catalogue's order.");
        sb.Append("        public static IReadOnlyList<string> All { get; } = new string[]\n        {\n");
        foreach (var code in codes) sb.Append($"            {Str(code)},\n");
        sb.Append("        };\n\n");
        Naming.Doc(sb, "        ", "Each code's one-line title in the catalogue (https://rewloy.com/gelistiriciler/hatalar).");
        sb.Append("        public static IReadOnlyDictionary<string, string> Titles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)\n        {\n");
        foreach (var code in codes.Where(titles.ContainsKey)) sb.Append($"            [{Str(code)}] = {Str(titles[code])},\n");
        sb.Append("        };\n\n");
        Naming.Doc(sb, "        ", "The title of a code, or `null` when the catalogue has none (a code newer than this library).");
        sb.Append("        public static string? Title(string? code) => code is not null && Titles.TryGetValue(code, out var title) ? title : null;\n");
        sb.Append("    }\n}\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ RewloyClient.g.cs

    private static string EmitClient(string apiVersion, List<Op> ops, Dictionary<string, Plan> plans)
    {
        var sb = new StringBuilder(Header(apiVersion, "using System;\nusing System.Collections.Generic;\nusing System.Text.Json;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Rewloy.Models;\n"));
        sb.Append("namespace Rewloy\n{\n");
        Naming.Doc(sb, "    ", "A client of the Rewloy API (`https://app.rewloy.com/v1`). This half is generated: one method per operation of the API, named by its operationId, with `Async` after it.");
        sb.Append("    public sealed partial class RewloyClient\n    {");
        string? tag = null;
        foreach (var op in ops)
        {
            var plan = plans[op.Id];
            if (op.Tag != tag)
            {
                tag = op.Tag;
                sb.Append($"\n        // {new string('-', 60)} {tag}\n");
            }

            // The parameter lists: with defaults where everything after is optional.
            var signature = string.Join(", ", plan.Parameters
                .Select(p => p.Required ? $"{p.Type} {p.Name}" : $"{p.Type}? {p.Name} = null")
                .Concat(["RequestOptions? options = null", "CancellationToken cancellationToken = default"]));
            var callArgs = string.Concat(plan.Parameters.Select(p => p.Name + ", "));
            var pathValues = plan.PathExpressions.Count == 0
                ? "Array.Empty<string>()"
                : $"new string[] {{ {string.Join(", ", plan.PathExpressions.Select(e => $"PathValue({e})"))} }}";

            var obsolete = op.Deprecated is { } dep
                ? $"        [Obsolete({Str(DeprecationMessage(op, dep))})]\n"
                : string.Empty;

            void WriteDoc(string? summary, bool whole)
            {
                var extra = new List<string>
                {
                    $"<c>{Naming.Xml(op.Method)} {Naming.Xml(op.Path)}</c>",
                    $"<see href=\"{Reference}#op-{op.Id}\">API referansı</see>",
                };
                if (op.Deprecated is { } d) extra.Add(Naming.Xml(DeprecationNote(op, d)));
                if (op.Idempotency is not null)
                {
                    extra.Add(op.Idempotency.Required
                        ? "<c>options.IdempotencyKey</c> is required: 8-64 printable ASCII characters. The call throws an <c>ArgumentException</c> before sending when it is missing, and the client never makes one up (a generated key would not survive a restart of your program). The same key is sent on every retry of this call."
                        : "<c>options.IdempotencyKey</c> is optional: 8-64 printable ASCII characters. When it is left out, the client generates a UUID and sends the same one on every retry of this call.");
                }
                if (whole) extra.Add("Returns the whole answer: the status, headers, <c>RequestId</c>, <c>Mode</c> (the <c>Rewloy-Mode</c> header) and <c>Replayed</c> besides the data.");
                var text = whole ? $"{summary} (the whole answer)" : string.Join("\n\n", new[] { summary, op.Description }.Where(s => !string.IsNullOrEmpty(s)));
                Naming.Doc(sb, "        ", text, extra);
                foreach (var (_, name, _, doc) in plan.Parameters) sb.Append($"        /// <param name=\"{name.TrimStart('@')}\">{Naming.Xml(doc.Replace("\n", " ", StringComparison.Ordinal))}</param>\n");
                sb.Append("        /// <param name=\"options\">Per-call options: the idempotency key, the business (<c>Rewloy-Merchant</c>), the timeout, the retries.</param>\n");
                sb.Append("        /// <param name=\"cancellationToken\">Cancels the call and any retry still waiting.</param>\n");
            }

            sb.Append('\n');
            if (op.Response == "stream")
            {
                WriteDoc(op.Summary, false);
                sb.Append(obsolete);
                sb.Append($"        public EventStream {op.Type}Async({signature})\n");
                sb.Append($"            => OpenStream(RewloyOperations.{op.Type}, {pathValues}, {plan.QueryExpr ?? "null"}, options, cancellationToken);\n");
                continue;
            }

            // The whole-answer variant first: the plain one is written over it.
            string respType, plain, call;
            switch (op.Response)
            {
                case "none":
                    respType = "RewloyResponse";
                    plain = "Task";
                    call = $"InvokeNoContentAsync(RewloyOperations.{op.Type}, {pathValues}, {plan.QueryExpr ?? "null"}, {plan.BodyExpr ?? "null"}, options, cancellationToken)";
                    break;
                case "blob":
                    respType = "RewloyResponse<RewloyFile>";
                    plain = "Task<RewloyFile>";
                    call = $"InvokeFileAsync(RewloyOperations.{op.Type}, {pathValues}, {plan.QueryExpr ?? "null"}, {plan.BodyExpr ?? "null"}, options, cancellationToken)";
                    break;
                default:
                    respType = $"RewloyResponse<{plan.DataType}>";
                    plain = op.Paged ? $"Task<Page<{plan.ItemType}>>" : $"Task<{plan.DataType}>";
                    call = $"InvokeAsync<{plan.DataType}>(RewloyOperations.{op.Type}, {pathValues}, {plan.QueryExpr ?? "null"}, {plan.BodyExpr ?? "null"}, options, cancellationToken)";
                    break;
            }

            WriteDoc(op.Summary, false);
            sb.Append(obsolete);
            if (op.Response == "none")
            {
                sb.Append($"        public async Task {op.Type}Async({signature})\n");
                sb.Append($"            => await {op.Type}WithResponseAsync({callArgs}options, cancellationToken).ConfigureAwait(false);\n");
            }
            else if (op.Paged)
            {
                sb.Append($"        public async {plain} {op.Type}Async({signature})\n        {{\n");
                sb.Append($"            var response = await {op.Type}WithResponseAsync({callArgs}options, cancellationToken).ConfigureAwait(false);\n");
                sb.Append($"            return Page<{plan.ItemType}>.From(response);\n        }}\n");
            }
            else
            {
                sb.Append($"        public async {plain} {op.Type}Async({signature})\n        {{\n");
                sb.Append($"            var response = await {op.Type}WithResponseAsync({callArgs}options, cancellationToken).ConfigureAwait(false);\n");
                sb.Append("            return response.Data;\n        }\n");
            }

            sb.Append('\n');
            WriteDoc(op.Summary, true);
            sb.Append(obsolete);
            sb.Append($"        public Task<{respType}> {op.Type}WithResponseAsync({signature})\n            => {call};\n");

            if (op.Paged)
            {
                if (op.Body is not null) throw new GeneratorException($"{op.Id} is a paged list with a request body: not supported");
                var q = plan.QueryType!;
                var queryRequired = plan.Parameters.First(p => p.Name == "query").Required;
                var pathParams = plan.Parameters.Where(p => p.Name != "query").ToList();
                var parameters = pathParams.Select(p => $"{p.Type} {p.Name}")
                    .Append(queryRequired ? $"{q} query" : $"{q}? query = null")
                    .Concat(["RequestOptions? options = null", "CancellationToken cancellationToken = default"]);
                var pathArgs = string.Concat(pathParams.Select(p => p.Name + ", "));
                var queryNow = queryRequired ? "query" : $"(query ?? new {q}())";
                sb.Append('\n');
                var extra = new List<string>
                {
                    "Walks every page: it asks for the next one (<c>page</c>) while the answer's <c>meta</c> says there is one. <c>Page</c> in the query sets where to start and <c>Limit</c> the page size.",
                };
                Naming.Doc(sb, "        ", $"Every item of `{op.Id}`, page after page.", extra);
                foreach (var (_, name, _, doc) in pathParams) sb.Append($"        /// <param name=\"{name.TrimStart('@')}\">{Naming.Xml(doc.Replace("\n", " ", StringComparison.Ordinal))}</param>\n");
                sb.Append("        /// <param name=\"query\">The query parameters.</param>\n");
                sb.Append("        /// <param name=\"options\">Per-call options, applied to every page's request.</param>\n");
                sb.Append("        /// <param name=\"cancellationToken\">Cancels the walk.</param>\n");
                sb.Append(obsolete);
                sb.Append($"        public IAsyncEnumerable<{plan.ItemType}> {op.Type}AllAsync({string.Join(", ", parameters)})\n");
                sb.Append($"            => PaginateAsync<{plan.ItemType}>({(queryRequired ? "query.Page" : "query?.Page")} ?? 1, (page, token) => {op.Type}WithResponseAsync({pathArgs}{queryNow}.ForPage(page), options, token), cancellationToken);\n");
            }
        }
        sb.Append("    }\n}\n");
        return sb.ToString();
    }

    private static string DeprecationMessage(Op op, (string? Sunset, string? Use) d) =>
        $"{op.Id} is deprecated" + (d.Sunset is null ? "." : $"; the API stops answering it after {d.Sunset}.") + (d.Use is null ? string.Empty : $" Use {d.Use} instead.");

    private static string DeprecationNote(Op op, (string? Sunset, string? Use) d) =>
        string.Join(' ', new[]
        {
            d.Sunset is null ? "The API will stop answering this operation." : $"The API stops answering this operation after {d.Sunset}.",
            d.Use is null ? null : $"Use {d.Use} instead.",
            $"{Changelog}#{op.Id}",
        }.Where(s => s is not null));
}
