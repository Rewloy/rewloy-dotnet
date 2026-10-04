using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    public class BackoffTests
    {
        [Fact]
        public void Doubles_from_half_a_second_up_to_eight_with_jitter_between_half_and_all_of_it()
        {
            foreach (var (attempt, cap) in new[] { (0, 500), (1, 1000), (2, 2000), (3, 4000), (4, 8000), (5, 8000), (30, 8000) })
            {
                Assert.Equal(cap / 2, Retry.Backoff(attempt, 0).TotalMilliseconds);
                Assert.Equal(cap, Retry.Backoff(attempt, 1).TotalMilliseconds);
                Assert.Equal(cap * 0.75, Retry.Backoff(attempt, 0.5).TotalMilliseconds);
            }
            for (var i = 0; i < 200; i++)
            {
                var ms = Retry.Backoff(2).TotalMilliseconds;
                Assert.InRange(ms, 1000, 2000);
            }
        }

        [Fact]
        public void Reads_retry_after_as_seconds_or_an_http_date()
        {
            var now = new DateTimeOffset(1994, 11, 6, 8, 49, 0, TimeSpan.Zero);
            Assert.Equal(TimeSpan.FromSeconds(5), Retry.ParseRetryAfter("5"));
            Assert.Equal(TimeSpan.FromMilliseconds(1500), Retry.ParseRetryAfter(" 1.5 "));
            Assert.Equal(TimeSpan.FromSeconds(37), Retry.ParseRetryAfter("Sun, 06 Nov 1994 08:49:37 GMT", now));
            Assert.Equal(TimeSpan.Zero, Retry.ParseRetryAfter("Sun, 06 Nov 1994 08:00:00 GMT", now));
            Assert.Null(Retry.ParseRetryAfter(null));
            Assert.Null(Retry.ParseRetryAfter(""));
            Assert.Null(Retry.ParseRetryAfter("soon"));
        }

        [Fact]
        public void Retries_the_gateway_statuses_429_and_a_campaign_still_in_progress()
        {
            foreach (var status in new[] { 429, 502, 503, 504, 520, 521, 522, 523, 524 }) Assert.True(Retry.IsRetryableStatus(status, "X"), status.ToString());
            foreach (var status in new[] { 400, 401, 403, 404, 422, 500, 501, 505, 519, 525 }) Assert.False(Retry.IsRetryableStatus(status, "X"), status.ToString());
            Assert.True(Retry.IsRetryableStatus(409, "IDEMPOTENCY_IN_PROGRESS"));
            Assert.False(Retry.IsRetryableStatus(409, "IDEMPOTENCY_KEY_REUSED"));
        }
    }

    public class RetryTests
    {
        private static readonly Guid Program = Guid.Parse("0192f7c1-0000-7000-8000-000000000002");
        private static readonly Guid Location = Guid.Parse("0192f7c1-0000-7000-8000-000000000003");
        private static PassActionBody Earn() => new PassActionBody { Action = "earn-stamps", LocationId = Location };

        [Fact]
        public async Task Retries_a_get_on_502_503_504_with_backoff_then_succeeds()
        {
            var stub = new StubHandler()
                .Then(Reply.Raw(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html"))
                .Then(Reply.Error(503, "PROVIDER_UNAVAILABLE"))
                .Then(Reply.Raw(HttpStatusCode.GatewayTimeout, ""))
                .Then(Reply.Ok("{\"serial\":\"ABCD-EFGH-JKLM\"}"));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps, o => o.MaxRetries = 3);

            var pass = await client.GetPassAsync("ABCD-EFGH-JKLM");

            Assert.Equal("ABCD-EFGH-JKLM", pass.Serial);
            Assert.Equal(4, stub.Count);
            Assert.Equal(3, sleeps.Delays.Count);
            Assert.InRange(sleeps.Delays[0].TotalMilliseconds, 250, 500);
            Assert.InRange(sleeps.Delays[1].TotalMilliseconds, 500, 1000);
            Assert.InRange(sleeps.Delays[2].TotalMilliseconds, 1000, 2000);
        }

        [Theory]
        [InlineData(520)]
        [InlineData(521)]
        [InlineData(522)]
        [InlineData(523)]
        [InlineData(524)]
        public async Task Retries_cloudflares_origin_errors(int status)
        {
            var stub = new StubHandler().Then(Reply.Raw((HttpStatusCode)status, "error code: " + status, "text/plain")).Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub);
            await client.GetPassAsync("ABCD-EFGH-JKLM");
            Assert.Equal(2, stub.Count);
        }

        [Fact]
        public async Task Gives_up_after_max_retries_and_throws_the_last_answer()
        {
            var stub = new StubHandler().Then(Reply.Error(503, "PROVIDER_UNAVAILABLE", requestId: "req_1")).Then(Reply.Error(503, "PROVIDER_UNAVAILABLE", requestId: "req_2")).Then(Reply.Error(503, "PROVIDER_UNAVAILABLE", requestId: "req_3"));
            using var client = Clients.Make(stub);

            var ex = await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("ABCD-EFGH-JKLM"));

            Assert.Equal(3, stub.Count); // the first attempt and two retries
            Assert.Equal(503, ex.Status);
            Assert.Equal("req_3", ex.RequestId);
        }

        [Fact]
        public async Task Does_not_retry_when_max_retries_is_zero_per_call_or_per_client()
        {
            var stub = new StubHandler().Then(Reply.Error(503, "PROVIDER_UNAVAILABLE")).Then(Reply.Error(503, "PROVIDER_UNAVAILABLE"));
            using var client = Clients.Make(stub);
            await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("ABCD-EFGH-JKLM", new RequestOptions { MaxRetries = 0 }));
            Assert.Equal(1, stub.Count);
            using var none = Clients.Make(stub, configure: o => o.MaxRetries = 0);
            await Assert.ThrowsAsync<RewloyException>(() => none.GetPassAsync("ABCD-EFGH-JKLM"));
            Assert.Equal(2, stub.Count);
        }

        [Fact]
        public async Task Honours_retry_after_on_429()
        {
            var stub = new StubHandler()
                .Then(Reply.Error(429, "RATE_LIMITED", headers: ("Retry-After", "7")))
                .Then(Reply.Ok("{}"));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            await client.GetPassAsync("ABCD-EFGH-JKLM");

            Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, sleeps.Delays);
        }

        [Fact]
        public async Task Waits_exactly_sixty_seconds_but_not_longer_and_gives_the_caller_the_rate_limit()
        {
            var stub = new StubHandler()
                .Then(Reply.Error(429, "RATE_LIMITED", headers: ("Retry-After", "60")))
                .Then(Reply.Ok("{}"))
                .Then(Reply.Error(429, "RATE_LIMITED", headers: ("Retry-After", "900")));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            await client.GetPassAsync("ABCD-EFGH-JKLM");
            Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, sleeps.Delays);

            var ex = await Assert.ThrowsAsync<RateLimitException>(() => client.GetPassAsync("ABCD-EFGH-JKLM"));
            Assert.Equal(TimeSpan.FromSeconds(900), ex.RetryAfter);
            Assert.Single(sleeps.Delays); // a 15-minute lockout is not slept through
            Assert.Equal(3, stub.Count);
        }

        [Fact]
        public async Task Never_retries_a_post_without_an_idempotency_key_nor_a_patch()
        {
            // createSegment is a POST that takes no Idempotency-Key; updateSegment is a PATCH.
            var stub = new StubHandler().Then(Reply.Error(503, "PROVIDER_UNAVAILABLE")).Then(Reply.Error(503, "PROVIDER_UNAVAILABLE"));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            await Assert.ThrowsAsync<RewloyException>(() => client.CreateSegmentAsync(new CreateSegmentBody { Name = "A", Rule = new CreateSegmentBodyRule() }));
            await Assert.ThrowsAsync<RewloyException>(() => client.UpdateSegmentAsync("a", new UpdateSegmentBody { Name = "B" }));

            Assert.Equal(2, stub.Count);
            Assert.Empty(sleeps.Delays);
        }

        [Fact]
        public async Task Retries_put_and_delete()
        {
            var stub = new StubHandler().Then(Reply.Raw(HttpStatusCode.BadGateway, "")).Then(Reply.NoContent()).Then(Reply.Raw(HttpStatusCode.ServiceUnavailable, "")).Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub);

            await client.ArchiveSegmentAsync("vip"); // DELETE
            await client.SetEmbedHostsAsync(new SetEmbedHostsBody()); // PUT

            Assert.Equal(new[] { "DELETE", "DELETE", "PUT", "PUT" }, stub.Requests.Select(r => r.Method));
        }

        [Fact]
        public async Task Retries_a_till_action_with_the_same_idempotency_key()
        {
            var stub = new StubHandler().Then(Reply.Raw(HttpStatusCode.BadGateway, "")).Then(Reply.Raw(HttpStatusCode.ServiceUnavailable, "")).Then(Reply.Ok("{\"balance\":3,\"duplicate\":false}"));
            using var client = Clients.Make(stub);

            await client.PassActionAsync("ABCD-EFGH-JKLM", Earn());

            var keys = stub.Requests.Select(r => r.Header("Idempotency-Key")).ToList();
            Assert.Equal(3, keys.Count);
            Assert.NotNull(keys[0]);
            Assert.Single(keys.Distinct()); // the same key on every attempt
            Assert.All(stub.Requests, r => Assert.Equal(stub.Requests[0].Body, r.Body));
        }

        [Fact]
        public async Task Waits_out_idempotency_in_progress_on_a_campaign_send_then_reads_the_replayed_answer()
        {
            var stub = new StubHandler()
                .Then(Reply.Error(409, "IDEMPOTENCY_IN_PROGRESS"))
                .Then(Reply.Ok("{\"id\":\"0192f7c1-0000-7000-8000-000000000002\"}", HttpStatusCode.Created, ("Idempotent-Replayed", "true")));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            var res = await client.SendCampaignWithResponseAsync(new SendCampaignBody { Body = "x" }, new RequestOptions { IdempotencyKey = "kampanya-1" });

            Assert.True(res.Replayed);
            Assert.Equal(2, stub.Count);
            Assert.Single(sleeps.Delays);
            Assert.All(stub.Requests, r => Assert.Equal("kampanya-1", r.Header("Idempotency-Key")));
        }

        [Fact]
        public async Task Does_not_retry_other_409s()
        {
            var stub = new StubHandler().Then(Reply.Error(409, "IDEMPOTENCY_KEY_REUSED"));
            using var client = Clients.Make(stub);
            var ex = await Assert.ThrowsAsync<RewloyException>(() => client.SendCampaignAsync(new SendCampaignBody { Body = "x" }));
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", ex.Code);
            Assert.Equal(1, stub.Count);
        }

        [Fact]
        public async Task Retries_when_the_connection_breaks_for_a_get_only()
        {
            var stub = new StubHandler()
                .ThenThrow(new HttpRequestException("connection reset"))
                .Then(Reply.Ok("{\"serial\":\"ABCD-EFGH-JKLM\"}"))
                .ThenThrow(new HttpRequestException("connection reset"));
            using var client = Clients.Make(stub);

            Assert.Equal("ABCD-EFGH-JKLM", (await client.GetPassAsync("ABCD-EFGH-JKLM")).Serial);

            var ex = await Assert.ThrowsAsync<RewloyConnectionException>(() => client.CreateSegmentAsync(new CreateSegmentBody { Name = "A", Rule = new CreateSegmentBodyRule() }));
            Assert.Equal(0, ex.Status);
            Assert.Equal("CONNECTION_ERROR", ex.Code);
            Assert.IsType<HttpRequestException>(ex.InnerException);
            Assert.Equal("createSegment", ex.Operation);
            Assert.Equal(3, stub.Count);
        }

        [Fact]
        public async Task Times_out_a_silent_attempt_and_retries_it()
        {
            var stub = new StubHandler()
                .Then(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Reply.Ok("{}"); })
                .Then(Reply.Ok("{\"serial\":\"ABCD-EFGH-JKLM\"}"));
            using var client = Clients.Make(stub);

            var pass = await client.GetPassAsync("ABCD-EFGH-JKLM", new RequestOptions { Timeout = TimeSpan.FromMilliseconds(250) });

            Assert.Equal("ABCD-EFGH-JKLM", pass.Serial);
            Assert.Equal(2, stub.Count);
        }

        [Fact]
        public async Task Throws_a_timeout_when_every_attempt_times_out()
        {
            Step hang = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Reply.Ok("{}"); };
            var stub = new StubHandler().Then(hang).Then(hang).Then(hang);
            using var client = Clients.Make(stub, configure: o => o.Timeout = TimeSpan.FromMilliseconds(50));

            var ex = await Assert.ThrowsAsync<RewloyTimeoutException>(() => client.GetPassAsync("ABCD-EFGH-JKLM"));

            Assert.Equal("TIMEOUT", ex.Code);
            Assert.Equal(0, ex.Status);
            Assert.IsAssignableFrom<RewloyConnectionException>(ex);
            Assert.Equal(3, stub.Count);
        }

        [Fact]
        public async Task A_timeout_covers_the_body_too()
        {
            var slowBody = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Support.ChunkStream("{\"data\":", Support.ChunkStream.Hang)) };
            var stub = new StubHandler().Then(slowBody);
            using var client = Clients.Make(stub, configure: o => o.MaxRetries = 0);
            await Assert.ThrowsAsync<RewloyTimeoutException>(() => client.GetPassAsync("ABCD-EFGH-JKLM", new RequestOptions { Timeout = TimeSpan.FromMilliseconds(80) }));
        }

        [Fact]
        public async Task Stops_at_once_when_the_caller_cancels_during_the_request()
        {
            var started = new TaskCompletionSource<bool>();
            var stub = new StubHandler().Then(async (_, ct) => { started.SetResult(true); await Task.Delay(Timeout.Infinite, ct); return Reply.Ok("{}"); });
            using var client = Clients.Make(stub);
            using var cts = new CancellationTokenSource();

            var call = client.GetPassAsync("ABCD-EFGH-JKLM", cancellationToken: cts.Token);
            await started.Task;
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call); // not a RewloyException
            Assert.Equal(1, stub.Count); // no retry
        }

        [Fact]
        public async Task Stops_at_once_when_the_caller_cancels_during_the_wait()
        {
            var stub = new StubHandler().Then(Reply.Error(503, "PROVIDER_UNAVAILABLE")).Then(Reply.Ok("{}"));
            using var cts = new CancellationTokenSource();
            using var client = Clients.Make(stub, configure: o => o.Delay = async (delay, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPassAsync("ABCD-EFGH-JKLM", cancellationToken: cts.Token));
            Assert.Equal(1, stub.Count);
        }

        [Fact]
        public async Task Does_not_start_a_call_with_a_token_that_is_already_cancelled()
        {
            var stub = new StubHandler();
            using var client = Clients.Make(stub);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPassAsync("ABCD-EFGH-JKLM", cancellationToken: cts.Token));
            Assert.Equal(0, stub.Count);
        }
    }
}
