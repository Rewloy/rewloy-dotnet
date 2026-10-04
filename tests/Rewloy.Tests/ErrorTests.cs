using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    public class ErrorTests
    {
        [Fact]
        public async Task Maps_an_api_error_body()
        {
            var stub = new StubHandler().Then(Reply.Error(422, "INSUFFICIENT_BALANCE", "Bakiye yetersiz: 20 TL eksik", "req_from_body", "{\"left\":2000}", ("x-request-id", "req_from_header")));
            using var client = Clients.Make(stub);

            var ex = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("ABCD-EFGH-JKLM"));

            Assert.Equal(422, ex.Status);
            Assert.Equal("INSUFFICIENT_BALANCE", ex.Code);
            Assert.Equal(ErrorCode.InsufficientBalance, ex.Code);
            Assert.Equal(ErrorCode.Titles["INSUFFICIENT_BALANCE"], ex.Title);
            Assert.False(string.IsNullOrEmpty(ex.Title));
            Assert.Equal("Bakiye yetersiz: 20 TL eksik", ex.Detail);
            Assert.Equal("req_from_header", ex.RequestId); // the header first
            Assert.Equal("https://rewloy.com/gelistiriciler/hatalar#INSUFFICIENT_BALANCE", ex.Docs);
            Assert.Equal(2000, ex.Details!.Value.GetProperty("left").GetInt32());
            Assert.Equal("getPass", ex.Operation);
            Assert.NotNull(ex.Headers);
            Assert.Contains("INSUFFICIENT_BALANCE", ex.Body);
            Assert.Equal("422 INSUFFICIENT_BALANCE: Bakiye yetersiz: 20 TL eksik (getPass, requestId req_from_header)", ex.Message);
        }

        [Fact]
        public async Task Falls_back_to_the_request_id_in_the_body()
        {
            var stub = new StubHandler().Then(Reply.Error(404, "PASS_NOT_FOUND", requestId: "req_body"));
            using var client = Clients.Make(stub);
            var ex = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("ABCD-EFGH-JKLM"));
            Assert.Equal("req_body", ex.RequestId);
        }

        [Fact]
        public async Task Keeps_the_validation_details()
        {
            var details = "[{\"field\":\"email\",\"rule\":\"format\",\"message\":\"Geçerli bir e-posta yazın\"}]";
            var stub = new StubHandler().Then(Reply.Error(400, "VALIDATION", "Gönderilen bilgiler geçersiz", detailsJson: details));
            using var client = Clients.Make(stub);

            var ex = await Assert.ThrowsAsync<RewloyException>(() => client.IssuePassAsync(new Models.IssuePassBody()));

            Assert.Equal(ErrorCode.Validation, ex.Code);
            var first = ex.Details!.Value[0];
            Assert.Equal("email", first.GetProperty("field").GetString());
            Assert.Equal("format", first.GetProperty("rule").GetString());
        }

        [Fact]
        public async Task Makes_429_a_rate_limit_exception_with_retry_after_from_the_header_else_from_the_details()
        {
            var stub = new StubHandler()
                .Then(Reply.Error(429, "RATE_LIMITED", headers: ("Retry-After", "1200")))
                .Then(Reply.Error(429, "RATE_LIMITED", detailsJson: "{\"retryAfterSec\":900}"))
                .Then(Reply.Error(429, "RATE_LIMITED"));
            using var client = Clients.Make(stub, configure: o => o.MaxRetries = 0);

            var byHeader = await Assert.ThrowsAsync<RateLimitException>(() => client.GetPassAsync("A"));
            var byDetails = await Assert.ThrowsAsync<RateLimitException>(() => client.GetPassAsync("A"));
            var neither = await Assert.ThrowsAsync<RateLimitException>(() => client.GetPassAsync("A"));

            Assert.Equal(TimeSpan.FromSeconds(1200), byHeader.RetryAfter);
            Assert.Equal(TimeSpan.FromSeconds(900), byDetails.RetryAfter);
            Assert.Null(neither.RetryAfter);
            Assert.Equal(429, byHeader.Status);
            Assert.IsAssignableFrom<RewloyException>(byHeader);
        }

        [Fact]
        public async Task Names_an_answer_that_is_not_rewloys_by_its_status()
        {
            var stub = new StubHandler()
                .Then(Reply.Raw(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>", "text/html"))
                .Then(Reply.Raw(HttpStatusCode.Forbidden, "", "text/plain"))
                .Then(Reply.Raw(HttpStatusCode.Moved, "moved", "text/plain"));
            using var client = Clients.Make(stub, configure: o => o.MaxRetries = 0);

            var proxy = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));
            var empty = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));
            var redirect = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));

            Assert.Equal("HTTP_502", proxy.Code);
            Assert.Null(proxy.Title);
            Assert.Contains("502 Bad Gateway", proxy.Body);
            Assert.Equal("HTTP_403", empty.Code);
            Assert.Equal("HTTP_301", redirect.Code); // redirects are never followed
        }

        [Fact]
        public async Task Refuses_a_2xx_answer_that_is_not_the_documented_json()
        {
            var stub = new StubHandler()
                .Then(Reply.Raw(HttpStatusCode.OK, "<html>login page</html>", "text/html"))
                .Then(Reply.Raw(HttpStatusCode.OK, "{\"nodata\":true}"))
                .Then(Reply.Ok("{\"serial\":5}"));
            using var client = Clients.Make(stub);

            var html = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));
            var noData = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));
            var wrongType = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));

            Assert.All(new[] { html, noData, wrongType }, ex => Assert.Equal("INVALID_RESPONSE", ex.Code));
            Assert.Equal(200, html.Status);
            Assert.IsAssignableFrom<JsonException>(wrongType.InnerException);
            Assert.Equal(3, stub.Count); // a malformed answer is not retried
        }

        [Fact]
        public async Task Reports_a_connection_that_cannot_be_made()
        {
            var stub = new StubHandler().ThenThrow(new HttpRequestException("Name or service not known", new System.Net.Sockets.SocketException()));
            using var client = Clients.Make(stub, configure: o => o.MaxRetries = 0);

            var ex = await Assert.ThrowsAsync<RewloyConnectionException>(() => client.GetPassAsync("A"));

            Assert.Equal(0, ex.Status);
            Assert.Equal("CONNECTION_ERROR", ex.Code);
            Assert.Contains("Name or service not known", ex.Detail);
            Assert.StartsWith("CONNECTION_ERROR:", ex.Message);
        }

        [Fact]
        public void Has_a_constant_for_every_code_in_the_catalogue_and_a_title_for_most()
        {
            Assert.Contains(ErrorCode.Validation, ErrorCode.All);
            Assert.Equal("VALIDATION", ErrorCode.Validation);
            Assert.Equal("IDEMPOTENCY_IN_PROGRESS", ErrorCode.IdempotencyInProgress);
            Assert.NotNull(ErrorCode.Title("VALIDATION"));
            Assert.Null(ErrorCode.Title("SOME_NEW_CODE"));
            Assert.Null(ErrorCode.Title(null));
            Assert.True(ErrorCode.All.Count > 100);
            Assert.Equal(ErrorCode.All.Count, new System.Collections.Generic.HashSet<string>(ErrorCode.All).Count);
        }
    }
}
