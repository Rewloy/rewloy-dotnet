using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Rewloy.Generator;

namespace Rewloy.Tests
{
    public static class Repo
    {
        public static string Root { get; } = FindRoot();

        private static string FindRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Rewloy.sln"))) return dir.FullName;
            }
            throw new InvalidOperationException("Rewloy.sln not found above " + AppContext.BaseDirectory);
        }

        public static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative), Encoding.UTF8);

        public static JsonDocument Snapshot() => JsonDocument.Parse(Read("openapi/openapi.json"));
    }

    public class GenerationTests
    {
        [Fact]
        public void Is_deterministic_and_the_committed_output_is_current()
        {
            using var doc = Repo.Snapshot();
            var first = ApiGenerator.Generate(doc.RootElement);
            var second = ApiGenerator.Generate(doc.RootElement);

            Assert.Equal(first.Select(f => f.Path), second.Select(f => f.Path));
            for (var i = 0; i < first.Count; i++) Assert.True(first[i].Content == second[i].Content, first[i].Path + " differs between two runs");
            Assert.Equal(
                new[] { "src/Rewloy/Generated/Models.g.cs", "src/Rewloy/Generated/Operations.g.cs", "src/Rewloy/Generated/ErrorCode.g.cs", "src/Rewloy/Generated/RewloyClient.g.cs" },
                first.Select(f => f.Path));
            foreach (var f in first)
            {
                var committed = Repo.Read(f.Path);
                Assert.True(committed == f.Content, f.Path + " is out of date: run `dotnet run --project tools/Rewloy.Generator -- --file openapi/openapi.json`");
                Assert.DoesNotContain("\r", f.Content);
            }
        }

        [Fact]
        public void Does_not_depend_on_the_order_the_document_is_parsed_in_or_the_culture()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                using var doc = Repo.Snapshot();
                var turkish = ApiGenerator.Generate(doc.RootElement);
                Assert.True(Repo.Read(turkish[3].Path) == turkish[3].Content, "output changes with the culture (Turkish i/I)");
                Assert.True(Repo.Read(turkish[0].Path) == turkish[0].Content);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = old;
            }
        }

        [Fact]
        public void Keeps_the_snapshot_in_the_format_the_other_libraries_keep()
        {
            using var doc = Repo.Snapshot();
            Assert.True(JsonSnapshot.Write(doc.RootElement) == Repo.Read("openapi/openapi.json"), "the snapshot is not what JSON.stringify(doc, null, 2) writes");
        }

        [Fact]
        public void Makes_one_method_per_operation_of_the_snapshot()
        {
            using var doc = Repo.Snapshot();
            var ids = doc.RootElement.GetProperty("paths").EnumerateObject()
                .SelectMany(p => p.Value.EnumerateObject().Where(m => m.Value.ValueKind == JsonValueKind.Object && m.Value.TryGetProperty("operationId", out _)))
                .Select(m => m.Value.GetProperty("operationId").GetString()!)
                .ToList();

            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.True(ids.Count >= 237);
            Assert.Equal(ids.Count, RewloyOperations.All.Count);
            var methods = typeof(RewloyClient).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToHashSet();
            foreach (var id in ids)
            {
                var name = char.ToUpperInvariant(id[0]) + id.Substring(1) + "Async";
                Assert.Contains(name, methods);
                Assert.True(RewloyOperations.All.ContainsKey(id), id);
                var op = RewloyOperations.All[id];
                if (op.Response != ResponseKind.Stream) Assert.Contains(char.ToUpperInvariant(id[0]) + id.Substring(1) + "WithResponseAsync", methods);
                if (op.IsPaged) Assert.Contains(char.ToUpperInvariant(id[0]) + id.Substring(1) + "AllAsync", methods);
            }
        }

        [Fact]
        public void Reads_the_metadata_table_right_for_known_operations()
        {
            var passAction = RewloyOperations.PassAction;
            Assert.Equal("POST", passAction.Method);
            Assert.Equal("/v1/passes/{serial}/actions", passAction.Path);
            Assert.Equal(new[] { "serial" }, passAction.PathParameters);
            Assert.Equal(CredentialKinds.Key | CredentialKinds.Staff, passAction.Credentials);
            Assert.True(passAction.AcceptsMerchant);
            Assert.Equal(IdempotencyMode.Required, passAction.Idempotency);
            Assert.True(passAction.HasBody);
            Assert.Equal(ResponseKind.Json, passAction.Response);

            Assert.Equal(IdempotencyMode.Optional, RewloyOperations.IssuePass.Idempotency);
            Assert.Equal(CredentialKinds.Public, RewloyOperations.Login.Credentials);
            Assert.Equal(ResponseKind.RawJson, RewloyOperations.Openapi.Response);
            Assert.Equal(ResponseKind.None, RewloyOperations.ArchiveSegment.Response);
            Assert.Equal(ResponseKind.File, RewloyOperations.ProgramJoinQr.Response);
            Assert.True(RewloyOperations.LiveFeed.IsStream);
            Assert.True(RewloyOperations.ListCustomers.IsPaged);
            Assert.False(RewloyOperations.GetPass.IsPaged);
            Assert.All(RewloyOperations.All.Values, op => Assert.StartsWith("/v1/", op.Path));
            Assert.Equal(RewloyOperations.All.Count, RewloyOperations.All.Values.Select(o => o.Id).Distinct().Count());
        }

        // ------------------------------------------------------------------ the generator on a fixture

        private const string Fixture = @"{
          ""openapi"": ""3.1.0"",
          ""info"": { ""title"": ""Fixture"", ""version"": ""9.9.9"" },
          ""paths"": {
            ""/v1/things/{id}"": {
              ""get"": {
                ""operationId"": ""getThing"", ""tags"": [""Şeyler""], ""summary"": ""Bir şey"", ""deprecated"": true,
                ""description"": ""**Kullanımdan kalkıyor:** 1 Nisan 2027 tarihine kadar çalışır; yerine `getThingV2`.\n\nYorum <b>&</b> kapanmasın.\n@internal bir etiket değil."",
                ""x-credentials"": [""key"", ""staff""],
                ""parameters"": [
                  { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"", ""format"": ""uuid"" } },
                  { ""name"": ""Rewloy-Merchant"", ""in"": ""header"", ""required"": false, ""schema"": { ""type"": ""string"" } }
                ],
                ""responses"": {
                  ""200"": { ""content"": { ""application/json"": { ""schema"": { ""type"": ""object"", ""required"": [""data""], ""properties"": { ""data"": {
                    ""type"": ""object"", ""additionalProperties"": false, ""required"": [""state"", ""weird-name"", ""class""],
                    ""properties"": {
                      ""state"": { ""type"": [""string"", ""null""], ""enum"": [""on"", ""off"", null] },
                      ""weird-name"": { ""oneOf"": [ { ""const"": ""all"" }, { ""type"": ""array"", ""items"": { ""type"": ""string"" } } ] },
                      ""class"": { ""type"": ""integer"" },
                      ""note"": { ""type"": ""string"", ""description"": ""Tırnak \"" ve ters bölü \\ içerir"" },
                      ""extra"": { ""type"": ""object"", ""additionalProperties"": { ""type"": ""integer"" } },
                      ""when"": { ""anyOf"": [ { ""type"": ""string"", ""pattern"": ""^x$"" }, { ""type"": ""string"", ""format"": ""date-time"" } ] },
                      ""sum"": { ""type"": ""number"" },
                      ""big"": { ""type"": ""integer"" },
                      ""small"": { ""type"": ""integer"", ""minimum"": 0, ""maximum"": 10 },
                      ""old"": { ""type"": [""string"", ""null""], ""deprecated"": true, ""x-deprecation"": { ""since"": ""2026-10-03"", ""sunset"": ""2027-04-05"", ""use"": ""newer"" } },
                      ""free"": { ""type"": ""object"", ""additionalProperties"": true },
                      ""child"": { ""type"": ""object"", ""properties"": { ""a"": { ""type"": ""boolean"" } } },
                      ""kids"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""b"": { ""type"": ""string"" } } } }
                    } } } } } } },
                  ""404"": { ""description"": ""x"", ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Error"" }, ""examples"": { ""NOT_FOUND"": { ""summary"": ""Bulunamadı"", ""value"": {} } } } } }
                }
              }
            },
            ""/v1/things/{id}/events"": {
              ""get"": {
                ""operationId"": ""thingEvents"", ""tags"": [""Şeyler""], ""summary"": ""Akış"", ""x-credentials"": [""holder""],
                ""parameters"": [ { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } } ],
                ""responses"": { ""200"": { ""description"": ""x"", ""content"": { ""text/event-stream"": { ""schema"": { ""type"": ""string"", ""format"": ""binary"" } } } } }
              },
              ""delete"": {
                ""operationId"": ""forgetThing"", ""tags"": [""Şeyler""], ""summary"": ""Sil"", ""security"": [ { ""apiKey"": [] }, {} ],
                ""parameters"": [ { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }, { ""name"": ""Idempotency-Key"", ""in"": ""header"", ""required"": false, ""schema"": { ""type"": ""string"" } } ],
                ""responses"": { ""204"": { ""description"": ""Tamam"" } }
              }
            },
            ""/v1/things"": {
              ""get"": {
                ""operationId"": ""listThings"", ""tags"": [""Şeyler""], ""summary"": ""Liste"", ""x-credentials"": [""key""],
                ""parameters"": [ { ""name"": ""page"", ""in"": ""query"", ""required"": false, ""schema"": { ""type"": ""integer"", ""minimum"": 1 } }, { ""name"": ""tag"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"" } } ],
                ""responses"": { ""200"": { ""content"": { ""application/json"": { ""schema"": { ""type"": ""object"", ""properties"": {
                  ""data"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""properties"": { ""n"": { ""type"": ""integer"" } } } },
                  ""meta"": { ""$ref"": ""#/components/schemas/PageMeta"" } } } } } } }
              },
              ""patch"": {
                ""operationId"": ""patchThing"", ""tags"": [""Şeyler""], ""summary"": ""Yama"", ""x-credentials"": [""key""],
                ""requestBody"": { ""required"": true, ""content"": { ""application/json"": { ""schema"": { ""type"": ""object"", ""properties"": { ""note"": { ""type"": [""string"", ""null""] }, ""count"": { ""type"": ""integer"" } } } } } },
                ""responses"": { ""200"": { ""content"": { ""application/json"": { ""schema"": { ""type"": ""object"", ""properties"": { ""data"": { ""type"": ""object"", ""properties"": { ""ok"": { ""type"": ""boolean"" } } } } } } } } }
              }
            }
          },
          ""components"": { ""schemas"": {
            ""Error"": { ""type"": ""object"", ""required"": [""error""], ""properties"": { ""error"": { ""type"": ""object"", ""required"": [""code"", ""message"", ""requestId""], ""properties"": {
              ""code"": { ""type"": ""string"", ""enum"": [""NOT_FOUND"", ""INTERNAL""] }, ""message"": { ""type"": ""string"" }, ""requestId"": { ""type"": ""string"" } } } } },
            ""PageMeta"": { ""type"": ""object"", ""required"": [""page"", ""pageSize"", ""total""], ""properties"": { ""page"": { ""type"": ""integer"" }, ""pageSize"": { ""type"": ""integer"" }, ""total"": { ""type"": ""integer"" } } }
          } }
        }";

        private static Dictionary<string, string> FixtureFiles()
        {
            using var doc = JsonDocument.Parse(Fixture);
            return ApiGenerator.Generate(doc.RootElement).ToDictionary(f => Path.GetFileName(f.Path), f => f.Content);
        }

        private static string Generate(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return string.Concat(ApiGenerator.Generate(doc.RootElement).Select(f => f.Content));
        }

        [Fact]
        public void Marks_a_deprecated_operation_with_its_sunset_and_replacement()
        {
            Assert.Equal(("2027-04-01", "getThingV2"), ApiGenerator.ParseDeprecation("**Kullanımdan kalkıyor:** 1 Nisan 2027 tarihine kadar çalışır; yerine `getThingV2`."));
            Assert.Equal(("2026-12-15", (string?)null), ApiGenerator.ParseDeprecation("**Kullanımdan kalkıyor:** 15 Aralık 2026 tarihine kadar çalışır."));
            var files = FixtureFiles();
            Assert.Contains("[Obsolete(\"getThing is deprecated; the API stops answering it after 2027-04-01. Use getThingV2 instead.\")]", files["RewloyClient.g.cs"]);
            Assert.Contains("The API stops answering this operation after 2027-04-01. Use getThingV2 instead.", files["RewloyClient.g.cs"]);
            Assert.Contains("deprecation: new Deprecation(\"2027-04-01\", \"getThingV2\")", files["Operations.g.cs"]);
        }

        [Fact]
        public void Keeps_comments_well_formed_xml_and_text_out_of_tags()
        {
            var client = FixtureFiles()["RewloyClient.g.cs"];
            Assert.Contains("Yorum &lt;b&gt;&amp;&lt;/b&gt; kapanmasın.", client);
            Assert.Contains("@internal bir etiket değil.", client);
            // Every generated file's doc comments parse as XML.
            foreach (var content in FixtureFiles().Values)
            {
                var docs = string.Join("\n", content.Split('\n').Where(l => l.TrimStart().StartsWith("///", StringComparison.Ordinal)).Select(l => l.TrimStart().Substring(3)));
                System.Xml.Linq.XDocument.Parse("<root>" + docs + "</root>");
            }
            using var doc = Repo.Snapshot();
            foreach (var f in ApiGenerator.Generate(doc.RootElement))
            {
                var docs = string.Join("\n", f.Content.Split('\n').Where(l => l.TrimStart().StartsWith("///", StringComparison.Ordinal)).Select(l => l.TrimStart().Substring(3)));
                System.Xml.Linq.XDocument.Parse("<root>" + docs + "</root>");
            }
        }

        [Fact]
        public void Writes_the_types_the_schemas_say()
        {
            var models = FixtureFiles()["Models.g.cs"];
            Assert.Contains("public string? State { get; set; }", models);          // enum with null: nullable string, values in the doc
            Assert.Contains("One of: `on`, `off`, null.", models);
            Assert.Contains("public JsonElement WeirdName { get; set; }", models);  // a real union stays JsonElement
            Assert.Contains("[JsonPropertyName(\"weird-name\")]", models);
            Assert.Contains("public long Class { get; set; }", models);             // a keyword is fine as a PascalCase property
            Assert.Contains("public string? Note { get; set; }", models);
            Assert.Contains("public IReadOnlyDictionary<string, long>? Extra { get; set; }", models);
            Assert.Contains("public string? When { get; set; }", models);           // a date and a date-time are strings alike
            Assert.Contains("public double? Sum { get; set; }", models);
            Assert.Contains("public long? Big { get; set; }", models);
            Assert.Contains("public int? Small { get; set; }", models);             // bounded inside Int32
            Assert.Contains("[Obsolete(\"old is deprecated; the API stops sending it after 2027-04-05. Use newer instead.\")]", models);
            Assert.Contains("public JsonElement? Free { get; set; }", models);
            Assert.Contains("public GetThingDataChild? Child { get; set; }", models);
            Assert.Contains("public IReadOnlyList<GetThingDataKidsItem>? Kids { get; set; }", models);
            Assert.Contains("public sealed class PageMeta : RewloyObject", models);
            // A request body keeps null apart from left out.
            Assert.Contains("public Optional<string> Note { get; set; }", models.Substring(models.IndexOf("class PatchThingBody", StringComparison.Ordinal)));
            Assert.Contains("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]", models);
            var errors = FixtureFiles()["ErrorCode.g.cs"];
            Assert.Contains("public const string NotFound = \"NOT_FOUND\";", errors);
            Assert.Contains("[\"NOT_FOUND\"] = \"Bulunamadı\",", errors);
        }

        [Fact]
        public void Reads_streams_empty_answers_paging_and_credentials_from_security()
        {
            var files = FixtureFiles();
            var ops = files["Operations.g.cs"];
            Assert.Contains("CredentialKinds.Holder,", ops);
            Assert.Contains("response: ResponseKind.Stream", ops);
            Assert.Contains("CredentialKinds.Key | CredentialKinds.Public", ops);   // security: apiKey or none
            Assert.Contains("idempotency: IdempotencyMode.Optional", ops);
            Assert.Contains("response: ResponseKind.None", ops);
            Assert.Contains("isPaged: true", ops);
            Assert.Contains("public const string ApiVersion = \"9.9.9\";", ops);
            var client = files["RewloyClient.g.cs"];
            Assert.Contains("public EventStream ThingEventsAsync(string id, RequestOptions? options = null, CancellationToken cancellationToken = default)", client);
            Assert.Contains("public async Task ForgetThingAsync(string id, RequestOptions? options = null, CancellationToken cancellationToken = default)", client);
            Assert.Contains("public Task<RewloyResponse> ForgetThingWithResponseAsync(", client);
            // A required query parameter makes the query required, and the paged walk follows.
            Assert.Contains("public async Task<Page<ListThingsItem>> ListThingsAsync(ListThingsQuery query, RequestOptions? options = null", client);
            Assert.Contains("public IAsyncEnumerable<ListThingsItem> ListThingsAllAsync(ListThingsQuery query, RequestOptions? options = null", client);
            Assert.Contains("public async Task<PatchThingData> PatchThingAsync(PatchThingBody? body = null,", client);
        }

        [Fact]
        public void Refuses_what_it_cannot_generate()
        {
            Assert.Throws<GeneratorException>(() => Generate("{}"));
            Assert.Throws<GeneratorException>(() => Generate("{\"openapi\":\"3.1.0\",\"paths\":{}}"));
            var dup = Fixture.Replace("\"operationId\": \"thingEvents\"", "\"operationId\": \"getThing\"");
            Assert.Contains("used twice", Assert.Throws<GeneratorException>(() => Generate(dup)).Message);
            var reserved = Fixture.Replace("\"operationId\": \"thingEvents\"", "\"operationId\": \"paginate\"");
            Assert.Contains("collides", Assert.Throws<GeneratorException>(() => Generate(reserved)).Message);
            var caseOnly = Fixture.Replace("\"operationId\": \"thingEvents\"", "\"operationId\": \"GetThing\"");
            Assert.Throws<GeneratorException>(() => Generate(caseOnly));
            var header = Fixture.Replace("\"name\": \"Rewloy-Merchant\"", "\"name\": \"X-Other\"");
            Assert.Contains("X-Other", Assert.Throws<GeneratorException>(() => Generate(header)).Message);
            var allOf = Fixture.Replace("\"sum\": { \"type\": \"number\" }", "\"sum\": { \"allOf\": [ { \"type\": \"number\" } ] }");
            Assert.Contains("allOf", Assert.Throws<GeneratorException>(() => Generate(allOf)).Message);
            var noPage = Fixture.Replace("{ \"name\": \"page\", \"in\": \"query\", \"required\": false, \"schema\": { \"type\": \"integer\", \"minimum\": 1 } }, ", string.Empty);
            Assert.Contains("page", Assert.Throws<GeneratorException>(() => Generate(noPage)).Message);
            var unknownRef = Fixture.Replace("\"sum\": { \"type\": \"number\" }", "\"sum\": { \"$ref\": \"#/components/schemas/Nope\" }");
            Assert.Contains("$ref", Assert.Throws<GeneratorException>(() => Generate(unknownRef)).Message);
            var xmlBody = Fixture.Replace("\"requestBody\": { \"required\": true, \"content\": { \"application/json\"", "\"requestBody\": { \"required\": true, \"content\": { \"application/xml\"");
            Assert.Contains("application/json", Assert.Throws<GeneratorException>(() => Generate(xmlBody)).Message);
        }
    }
}
