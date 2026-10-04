using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    public class ModelTests
    {
        [Fact]
        public void Optional_tells_a_value_from_null_from_left_out()
        {
            Optional<string> undefined = default;
            Optional<string> nothing = Optional<string>.Null;
            Optional<string> text = "x";
            Optional<string> fromNull = new Optional<string>(null!);

            Assert.True(undefined.IsUndefined);
            Assert.True(nothing.IsNull);
            Assert.True(fromNull.IsNull);
            Assert.True(text.HasValue);
            Assert.Equal("x", text.Value);
            Assert.Throws<InvalidOperationException>(() => nothing.Value);
            Assert.Throws<InvalidOperationException>(() => undefined.Value);
            Assert.Equal(undefined, Optional<string>.Undefined);
            Assert.NotEqual(undefined, nothing);
            Assert.NotEqual(nothing, text);
            Assert.Equal(text, (Optional<string>)"x");
            Assert.True(text == "x");
            Assert.True(text != "y");
            Assert.Equal("(undefined)", undefined.ToString());
            Assert.Equal("null", nothing.ToString());
            Assert.Equal(text.GetHashCode(), ((Optional<string>)"x").GetHashCode());
        }

        [Fact]
        public void Optional_reads_back_what_it_writes()
        {
            var body = JsonSerializer.Serialize(new CorrectHolderProfileBody { FirstName = "Ayşe", LastName = Optional<string>.Null }, RewloyJson.Options);
            Assert.Equal("{\"firstName\":\"Ayşe\",\"lastName\":null}", body);
            var back = JsonSerializer.Deserialize<CorrectHolderProfileBody>(body, RewloyJson.Options)!;
            Assert.Equal("Ayşe", back.FirstName.Value);
            Assert.True(back.LastName.IsNull);
            Assert.True(back.Phone.IsUndefined);
        }

        [Fact]
        public void Reads_uuids_dates_numbers_lists_and_nested_objects()
        {
            var json = "{\"serial\":\"ABCD-EFGH-JKLM\",\"programId\":\"0192F7C1-0000-7000-8000-000000000002\",\"updatedAt\":\"2026-10-03T15:00:00+03:00\",\"balance\":12.5,\"rewardsReady\":2,\"nextReward\":{\"label\":\"Kahve\"},\"nextTier\":null}";
            var pass = JsonSerializer.Deserialize<GetPassData>(json, RewloyJson.Options)!;
            Assert.Equal(Guid.Parse("0192f7c1-0000-7000-8000-000000000002"), pass.ProgramId);
            Assert.Equal(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), pass.UpdatedAt.ToUniversalTime());
            Assert.Equal(12.5, pass.Balance);
            Assert.Equal(2, pass.RewardsReady);
            Assert.Equal("Kahve", pass.NextReward!.Value.GetProperty("label").GetString());
            Assert.Null(pass.NextTier); // a JSON null is a C# null
            Assert.Null(pass.Tier);
        }

        [Fact]
        public void Required_lists_are_empty_not_null_when_the_answer_leaves_them_out()
        {
            var item = JsonSerializer.Deserialize<ListCustomersItem>("{\"displayName\":\"Ayşe\"}", RewloyJson.Options)!;
            Assert.NotNull(item.Cards);
            Assert.Empty(item.Cards);
        }

        [Fact]
        public void Query_writer_formats_every_type_the_api_takes()
        {
            var w = new QueryWriter();
            w.Add("s", "a b&c=ç");
            w.Add("none", (string?)null);
            w.Add("t", (bool?)true);
            w.Add("f", (bool?)false);
            w.Add("i", (int?)-5);
            w.Add("l", (long?)12345678901);
            w.Add("d", (double?)1.5);
            w.Add("g", (Guid?)Guid.Parse("0192F7C1-0000-7000-8000-000000000002"));
            w.Add("at", (DateTimeOffset?)new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
            w.AddMany("many", new[] { "x", "y z" });
            w.AddMany("nums", new[] { 1, 2 });
            w.AddMany("none", (IReadOnlyList<string>?)null);

            Assert.Equal(
                "s=a%20b%26c%3D%C3%A7&t=true&f=false&i=-5&l=12345678901&d=1.5&g=0192f7c1-0000-7000-8000-000000000002&at=2026-10-03T12%3A00%3A00.0000000%2B00%3A00&many=x&many=y%20z&nums=1&nums=2",
                w.ToQueryString());
        }

        [Fact]
        public void Headers_are_case_insensitive_and_hold_response_and_content_headers()
        {
            var response = new HttpResponseMessage { Content = new StringContent("x") };
            response.Headers.TryAddWithoutValidation("X-Request-Id", "req_1");
            response.Headers.TryAddWithoutValidation("Vary", new[] { "a", "b" });
            var h = RewloyHeaders.From(response);
            Assert.Equal("req_1", h.Get("x-request-id"));
            Assert.Equal(new[] { "a", "b" }, h.GetAll("VARY"));
            Assert.Equal("text/plain; charset=utf-8", h.Get("content-type"));
            Assert.True(h.Contains("Content-Type"));
            Assert.Null(h.Get("missing"));
            Assert.Empty(h.GetAll("missing"));
            Assert.Equal(h.Count, h.Count());
        }
    }

    public class ClientBehaviourTests
    {
        [Fact]
        public async Task Is_safe_to_share_between_threads()
        {
            var stub = new StubHandler();
            for (var i = 0; i < 40; i++) stub.Then(Reply.Ok("{\"serial\":\"S\"}"));
            using var client = Clients.Make(stub);

            var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => client.GetPassAsync("S"))));

            Assert.All(results, r => Assert.Equal("S", r.Serial));
            Assert.Equal(40, stub.Count);
        }

        [Fact]
        public async Task Works_behind_a_proxy_path_prefix()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub, configure: o => o.BaseUrl = "https://proxy.test/rewloy/");
            await client.GetPassAsync("S");
            Assert.Equal("https://proxy.test/rewloy/v1/passes/S", stub.Requests[0].Uri.ToString());
        }

        [Fact]
        public void Says_the_same_version_in_the_project_the_changelog_and_the_user_agent()
        {
            var project = Repo.Read("src/Rewloy/Rewloy.csproj");
            var inProject = Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;
            var changelog = Repo.Read("CHANGELOG.md");
            var inChangelog = Regex.Match(changelog, @"^## (\d+\.\d+\.\d+)", RegexOptions.Multiline).Groups[1].Value;
            Assert.Equal("0.1.0", RewloyVersion.Current);
            Assert.Equal(RewloyVersion.Current, inProject);
            Assert.Equal(RewloyVersion.Current, inChangelog);
            var assemblyVersion = typeof(RewloyClient).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
            Assert.StartsWith(RewloyVersion.Current, assemblyVersion);
        }

        [Fact]
        public void Has_the_package_metadata_the_brief_asks_for()
        {
            var project = Repo.Read("src/Rewloy/Rewloy.csproj");
            Assert.Contains("<PackageId>Rewloy</PackageId>", project);
            Assert.Contains("<PackageLicenseExpression>MIT</PackageLicenseExpression>", project);
            Assert.Matches("<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>", project);
            Assert.True(File.Exists(Path.Combine(Repo.Root, "LICENSE")));
        }
    }
}
