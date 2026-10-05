using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    public class ConstructionTests
    {
        [Fact]
        public void Takes_one_credential_of_the_right_kind()
        {
            using (var key = new RewloyClient(new RewloyClientOptions { ApiKey = Clients.Key })) Assert.Equal(CredentialKinds.Key, key.Credential);
            using (var staff = new RewloyClient(new RewloyClientOptions { StaffSession = Clients.Staff, Merchant = Clients.MerchantA }))
            {
                Assert.Equal(CredentialKinds.Staff, staff.Credential);
                Assert.Equal(Clients.MerchantA, staff.Merchant);
            }
            using (var holder = new RewloyClient(new RewloyClientOptions { HolderSession = Clients.Holder })) Assert.Equal(CredentialKinds.Holder, holder.Credential);
            using (var none = new RewloyClient()) Assert.Equal(CredentialKinds.None, none.Credential);

            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { ApiKey = Clients.Key, StaffSession = Clients.Staff }));
            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { ApiKey = Clients.Staff }));
            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { StaffSession = Clients.Key }));
            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { HolderSession = "" }));
            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { ApiKey = Clients.Key, Merchant = Clients.MerchantA }));
            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { BaseUrl = "not a url" }));
            Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { BaseUrl = "ftp://app.rewloy.com" }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RewloyClient(new RewloyClientOptions { MaxRetries = -1 }));
        }

        [Fact]
        public void Never_puts_a_credential_in_a_message_or_a_string()
        {
            var ex = Assert.Throws<ArgumentException>(() => new RewloyClient(new RewloyClientOptions { ApiKey = "rws_sekret-value-that-is-mistyped" }));
            Assert.DoesNotContain("sekret", ex.Message);
            using var client = new RewloyClient(new RewloyClientOptions { ApiKey = Clients.Key });
            Assert.DoesNotContain("secretsecret", client.ToString());
        }

        [Fact]
        public void Has_defaults()
        {
            using var client = new RewloyClient();
            Assert.Equal("https://app.rewloy.com", client.BaseUrl);
            Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
            Assert.Equal(2, client.MaxRetries);
            using var trimmed = new RewloyClient(new RewloyClientOptions { BaseUrl = "https://example.test///" });
            Assert.Equal("https://example.test", trimmed.BaseUrl);
        }

        [Fact]
        public void Does_not_dispose_an_http_client_it_was_given()
        {
            var http = new HttpClient(new StubHandler());
            new RewloyClient(new RewloyClientOptions { HttpClient = http }).Dispose();
            http.Dispose();
        }
    }

    public class RequestTests
    {
        private const string PassJson = "{\"serial\":\"ABCD-EFGH-JKLM\",\"programId\":\"0192f7c1-0000-7000-8000-000000000002\",\"type\":\"stamp\",\"status\":\"active\",\"balance\":3,\"rewardReady\":true,\"rewardsReady\":1,\"updatedAt\":\"2026-10-03T12:00:00.000Z\"}";

        [Fact]
        public async Task Sends_the_api_key_and_the_client_and_no_merchant()
        {
            var stub = new StubHandler().Then(Reply.Ok(PassJson));
            using var client = Clients.Make(stub);

            var pass = await client.GetPassAsync("ABCD-EFGH-JKLM");

            var req = Assert.Single(stub.Requests);
            Assert.Equal("GET", req.Method);
            Assert.Equal("https://api.test/v1/passes/ABCD-EFGH-JKLM", req.Uri.ToString());
            Assert.Equal("Bearer " + Clients.Key, req.Header("Authorization"));
            Assert.Equal("application/json", req.Header("Accept"));
            Assert.Null(req.Header("Rewloy-Merchant"));
            Assert.Null(req.Header("Idempotency-Key"));
            Assert.Null(req.Body);
            Assert.StartsWith("rewloy-dotnet/0.2.0 ", req.Header("User-Agent"));
            Assert.Equal("ABCD-EFGH-JKLM", pass.Serial);
            Assert.Equal(Guid.Parse("0192f7c1-0000-7000-8000-000000000002"), pass.ProgramId);
            Assert.Equal(3, pass.Balance);
            Assert.True(pass.RewardReady);
            Assert.Equal(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), pass.UpdatedAt);
        }

        [Fact]
        public async Task Adds_a_suffix_to_the_user_agent()
        {
            var stub = new StubHandler().Then(Reply.Ok(PassJson));
            using var client = Clients.Make(stub, configure: o => o.UserAgent = "KasaPOS/4.2");
            await client.GetPassAsync("ABCD-EFGH-JKLM");
            Assert.EndsWith(" KasaPOS/4.2", stub.Requests[0].Header("User-Agent"));
        }

        [Fact]
        public async Task Sends_a_staff_session_with_its_merchant_overridable_per_call()
        {
            var stub = new StubHandler().Then(Reply.Ok(PassJson)).Then(Reply.Ok(PassJson));
            using var client = Clients.Make(stub, configure: o =>
            {
                o.ApiKey = null;
                o.StaffSession = Clients.Staff;
                o.Merchant = Clients.MerchantA;
            });

            await client.GetPassAsync("ABCD-EFGH-JKLM");
            await client.GetPassAsync("ABCD-EFGH-JKLM", new RequestOptions { Merchant = Clients.MerchantB });

            Assert.Equal("Bearer " + Clients.Staff, stub.Requests[0].Header("Authorization"));
            Assert.Equal(Clients.MerchantA.ToString(), stub.Requests[0].Header("Rewloy-Merchant"));
            Assert.Equal(Clients.MerchantB.ToString(), stub.Requests[1].Header("Rewloy-Merchant"));
        }

        [Fact]
        public async Task Sends_a_merchant_only_where_the_operation_takes_one()
        {
            // login takes no Rewloy-Merchant header.
            var stub = new StubHandler().Then(Reply.Ok("{\"token\":\"rws_x\",\"mfaRequired\":false}", HttpStatusCode.Created));
            using var client = Clients.Make(stub, configure: o =>
            {
                o.ApiKey = null;
                o.StaffSession = Clients.Staff;
                o.Merchant = Clients.MerchantA;
            });
            await client.LoginAsync(new LoginBody { Email = "a@b.test", Password = "x" });
            Assert.Null(stub.Requests[0].Header("Rewloy-Merchant"));
        }

        [Fact]
        public async Task Sends_a_holder_session()
        {
            var stub = new StubHandler().Then(Reply.Ok("[]"));
            using var client = Clients.Make(stub, configure: o =>
            {
                o.ApiKey = null;
                o.HolderSession = Clients.Holder;
            });
            var cards = await client.HolderCardsAsync();
            Assert.Empty(cards);
            Assert.Equal("Bearer " + Clients.Holder, stub.Requests[0].Header("Authorization"));
        }

        [Fact]
        public async Task Calls_an_operation_that_does_not_take_this_credential_but_works_without_one_without_it()
        {
            // login and openapi take no credential at all; the API refuses one it does not accept.
            var stub = new StubHandler()
                .Then(Reply.Ok("{\"token\":\"rws_x\",\"mfaRequired\":false}", HttpStatusCode.Created))
                .Then(Reply.Raw(HttpStatusCode.OK, "{\"openapi\":\"3.1.0\"}"))
                .Then(Reply.Ok("{\"id\":\"0192f7c1-0000-7000-8000-000000000002\"}"));
            using var client = Clients.Make(stub);

            await client.LoginAsync(new LoginBody { Email = "a@b.test", Password = "x" });
            await client.OpenapiAsync();
            // publicProgram takes public, holder, staff and key: the key is sent.
            await client.PublicProgramAsync(Guid.Parse("0192f7c1-0000-7000-8000-000000000002"));

            Assert.Null(stub.Requests[0].Header("Authorization"));
            Assert.Null(stub.Requests[1].Header("Authorization"));
            Assert.Equal("Bearer " + Clients.Key, stub.Requests[2].Header("Authorization"));
        }

        [Fact]
        public async Task Sends_json_bodies_in_camel_case_and_leaves_unset_fields_out()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"id\":\"0192f7c1-0000-7000-8000-000000000002\"}", HttpStatusCode.Created));
            using var client = Clients.Make(stub);

            await client.SendCampaignAsync(new SendCampaignBody { Body = "Bu hafta kahveler 2 damga! ğüşıöç", Name = "Kahve" });

            var req = stub.Requests[0];
            Assert.Equal("POST", req.Method);
            Assert.Equal("/v1/campaigns", req.PathAndQuery);
            Assert.Equal("application/json", req.Header("Content-Type"));
            Assert.Equal("{\"body\":\"Bu hafta kahveler 2 damga! ğüşıöç\",\"name\":\"Kahve\"}", req.Body);
        }

        [Fact]
        public async Task Sends_an_empty_object_when_an_all_optional_body_is_left_out()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub);
            await client.ArchiveProgramAsync(Guid.Parse("0192f7c1-0000-7000-8000-000000000002"));
            Assert.Equal("{}", stub.Requests[0].Body);
        }

        [Fact]
        public async Task Tells_null_from_left_out_with_optional()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}")).Then(Reply.Ok("{}")).Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub);
            var id = Guid.Parse("0192f7c1-0000-7000-8000-000000000002");

            await client.CorrectHolderProfileAsync(id, new CorrectHolderProfileBody()); // nothing is sent
            await client.CorrectHolderProfileAsync(id, new CorrectHolderProfileBody { FirstName = "Ayşe", Phone = Optional<string>.Null }); // sets one, clears one

            Assert.Equal("{}", stub.Requests[0].Body);
            Assert.Equal("{\"firstName\":\"Ayşe\",\"phone\":null}", stub.Requests[1].Body);
        }

        [Fact]
        public async Task Keeps_fields_it_does_not_know_both_ways()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"serial\":\"ABCD-EFGH-JKLM\",\"brandNewField\":{\"x\":1},\"another\":[1,2]}"))
                .Then(Reply.Ok("{\"id\":\"0192f7c1-0000-7000-8000-000000000002\"}", HttpStatusCode.Created));
            using var client = Clients.Make(stub);

            var pass = await client.GetPassAsync("ABCD-EFGH-JKLM");
            Assert.NotNull(pass.AdditionalProperties);
            Assert.Equal(1, pass.AdditionalProperties!["brandNewField"].GetProperty("x").GetInt32());
            Assert.Equal(2, pass.AdditionalProperties["another"].GetArrayLength());

            // A field the API has and the library does not know yet can still be sent.
            var body = new SendCampaignBody { Body = "x" };
            body.AdditionalProperties = new Dictionary<string, JsonElement> { ["newOption"] = JsonDocument.Parse("true").RootElement };
            await client.SendCampaignAsync(body);
            Assert.Equal("{\"body\":\"x\",\"newOption\":true}", stub.Requests[1].Body);
        }

        [Fact]
        public async Task Generates_an_idempotency_key_when_none_is_given_and_sends_the_given_one()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"balance\":1,\"duplicate\":false}")).Then(Reply.Ok("{\"balance\":1,\"duplicate\":true}"));
            using var client = Clients.Make(stub);
            var body = new PassActionBody { Action = "earn-stamps", LocationId = Guid.Parse("0192f7c1-0000-7000-8000-000000000003") };

            await client.PassActionAsync("ABCD-EFGH-JKLM", body);
            var second = await client.PassActionAsync("ABCD-EFGH-JKLM", body, new RequestOptions { IdempotencyKey = "fis-2026-0001" });

            Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", stub.Requests[0].Header("Idempotency-Key")!);
            Assert.Equal("fis-2026-0001", stub.Requests[1].Header("Idempotency-Key"));
            Assert.True(second.Duplicate);
        }

        [Fact]
        public async Task Sends_no_idempotency_key_where_the_operation_takes_none()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"slug\":\"a\"}", HttpStatusCode.Created));
            using var client = Clients.Make(stub);
            await client.CreateSegmentAsync(new CreateSegmentBody { Name = "A", Rule = new CreateSegmentBodyRule() }, new RequestOptions { IdempotencyKey = "ignored-12345" });
            Assert.Null(stub.Requests[0].Header("Idempotency-Key"));
        }

        [Fact]
        public async Task Encodes_path_parameters_and_the_query()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}")).Then(Reply.Page("[]", 1, 5, 0));
            using var client = Clients.Make(stub);

            await client.UpdateSegmentAsync("a b/ç");
            await client.ListCustomersAsync(new ListCustomersQuery { Q = "ali veli&x=1", Consent = "yes", Blocked = false, Limit = 5, ProgramId = Guid.Parse("0192f7c1-0000-7000-8000-000000000002") });

            Assert.Equal("/v1/segments/a%20b%2F%C3%A7", stub.Requests[0].Uri.AbsolutePath.Replace("a%20b%2F%C3%A7", "a%20b%2F%C3%A7"));
            Assert.Equal("https://api.test/v1/segments/a%20b%2F%C3%A7", stub.Requests[0].Uri.OriginalString);
            Assert.Equal("https://api.test/v1/customers?q=ali%20veli%26x%3D1&programId=0192f7c1-0000-7000-8000-000000000002&consent=yes&blocked=false&limit=5", stub.Requests[1].Uri.OriginalString);
        }

        [Fact]
        public async Task Refuses_an_empty_path_parameter_before_sending()
        {
            var stub = new StubHandler();
            using var client = Clients.Make(stub);
            await Assert.ThrowsAsync<ArgumentException>(() => client.GetPassAsync(""));
            Assert.Equal(0, stub.Count);
        }

        [Fact]
        public async Task Reads_each_kind_of_answer()
        {
            var stub = new StubHandler()
                .Then(Reply.NoContent())
                .Then(Reply.Raw(HttpStatusCode.OK, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "image/svg+xml", ("Content-Disposition", "attachment; filename=\"join qr.svg\"")))
                .Then(Reply.Raw(HttpStatusCode.OK, "{\"openapi\":\"3.1.0\",\"paths\":{}}"))
                .Then(Reply.Page("[{\"personId\":\"0192f7c1-0000-7000-8000-000000000004\",\"displayName\":\"Ayşe\",\"passCount\":2}]", 2, 1, 5));
            using var client = Clients.Make(stub);

            await client.ArchiveSegmentAsync("vip"); // 204: no body, nothing returned
            var file = await client.ProgramJoinQrAsync(Guid.Parse("0192f7c1-0000-7000-8000-000000000002"));
            var document = await client.OpenapiAsync();
            var page = await client.ListCustomersAsync(new ListCustomersQuery { Page = 2, Limit = 1 });

            Assert.Equal("DELETE", stub.Requests[0].Method);
            Assert.StartsWith("image/svg+xml", file.ContentType);
            Assert.Equal("join qr.svg", file.FileName);
            Assert.StartsWith("<svg", System.Text.Encoding.UTF8.GetString(file.Content));
            Assert.Equal("3.1.0", document.GetProperty("openapi").GetString());
            Assert.Equal("*/*", stub.Requests[1].Header("Accept"));
            var customer = Assert.Single(page.Data);
            Assert.Equal("Ayşe", customer.DisplayName);
            Assert.Equal(2, page.Meta.Page);
            Assert.Equal(5, page.Meta.Total);
        }

        [Fact]
        public async Task Gives_the_whole_answer_through_with_response()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"id\":\"0192f7c1-0000-7000-8000-000000000002\"}", HttpStatusCode.Created,
                ("x-request-id", "req_abc"), ("Rewloy-Mode", "test"), ("Idempotent-Replayed", "true")));
            using var client = Clients.Make(stub);

            var res = await client.SendCampaignWithResponseAsync(new SendCampaignBody { Body = "x" }, new RequestOptions { IdempotencyKey = "kampanya-2026-10-03" });

            Assert.Equal(201, res.StatusCode);
            Assert.True(res.Replayed);
            Assert.Equal("req_abc", res.RequestId);
            Assert.Equal("test", res.Mode);
            Assert.True(res.IsTestMode);
            Assert.Equal(Guid.Parse("0192f7c1-0000-7000-8000-000000000002"), res.Data.Id);
            Assert.Equal("req_abc", res.Headers.Get("X-Request-Id"));
        }

        [Fact]
        public async Task Says_nothing_of_the_mode_when_the_answer_does_not()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}")).Then(Reply.NoContent());
            using var client = Clients.Make(stub);
            var res = await client.GetPassWithResponseAsync("ABCD-EFGH-JKLM");
            Assert.Null(res.Mode);
            Assert.False(res.IsTestMode);
            Assert.False(res.Replayed);
            var none = await client.ArchiveSegmentWithResponseAsync("vip");
            Assert.Equal(204, none.StatusCode);
        }

        [Fact]
        public async Task Takes_extra_headers_from_the_options()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub);
            await client.GetPassAsync("ABCD-EFGH-JKLM", new RequestOptions { Headers = new Dictionary<string, string> { ["X-Trace"] = "abc", ["Accept-Language"] = "tr" } });
            Assert.Equal("abc", stub.Requests[0].Header("X-Trace"));
            Assert.Equal("tr", stub.Requests[0].Header("Accept-Language"));
        }

        [Fact]
        public async Task Opens_streams_lazily_and_sends_nothing_until_iterated()
        {
            var stub = new StubHandler();
            using var client = Clients.Make(stub);
            var stream = client.LiveFeedAsync();
            Assert.Equal(0, stub.Count);
            await stream.DisposeAsync();
        }
    }
}
