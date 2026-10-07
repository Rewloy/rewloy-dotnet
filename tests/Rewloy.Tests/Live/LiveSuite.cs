using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Rewloy.Models;
using Xunit.Abstractions;

namespace Rewloy.Tests.Live
{
    /// <summary>
    /// The live integration suite: the library against a real Rewloy DEV server, with a test-mode key.
    /// Run it with one command (README, "Live tests"):
    /// <c>REWLOY_BASE_URL=… REWLOY_API_KEY=rwk_test_… dotnet test tests/Rewloy.Tests -f net10.0 --filter Category=Live --logger "console;verbosity=detailed"</c>.
    /// It refuses anything but a dev server and a test key, creates only what it can clean up, and ends with the test reset.
    /// </summary>
    [Trait("Category", "Live")]
    public class LiveSuite
    {
        private readonly ITestOutputHelper _out;
        public LiveSuite(ITestOutputHelper output) { _out = output; }

        [LiveFact]
        public async Task Library_works_end_to_end_against_a_dev_server()
        {
            var baseUrl = LiveEnv.BaseUrl!;
            var key = LiveEnv.ApiKey!;

            // Guard 1, before any request leaves: only a test-mode key.
            if (!key.StartsWith("rwk_test_", StringComparison.Ordinal))
                throw new InvalidOperationException("Live tests refuse to run: REWLOY_API_KEY is not a test key (rwk_test_…). They never use a live key.");

            var log = new Action<string>(s => _out.WriteLine(s));
            var run = new LiveRunner(log);
            var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            var cleanup = new List<(string Name, Func<Task> Run)>();
            using var rw = new RewloyClient(new RewloyClientOptions { ApiKey = key, BaseUrl = baseUrl, Timeout = TimeSpan.FromSeconds(30) });

            // Guard 2: GET /v1/meta must say environment "dev". Anything else, or no field at all: stop.
            // (Asked without the key: /v1/meta is public, so a wrong server never sees the key.)
            RewloyResponse<GetMetaData> meta;
            using (var anon = new RewloyClient(new RewloyClientOptions { BaseUrl = baseUrl, Timeout = TimeSpan.FromSeconds(30) }))
                meta = await anon.GetMetaWithResponseAsync();
            var environment = EnvironmentOf(meta.Data);
            if (environment != "dev")
                throw new InvalidOperationException("Live tests refuse to run: GET /v1/meta says environment " + (environment == null ? "(missing)" : "\"" + environment + "\"") + ", not \"dev\". They only ever run against a dev server.");
            // Guard 3: the key must really be a test key as the server sees it (Rewloy-Mode: test) and the business a test environment.
            var biz = await rw.GetBusinessWithResponseAsync();
            if (!biz.IsTestMode || !biz.Data.Name.EndsWith("Test", StringComparison.Ordinal))
                throw new InvalidOperationException("Live tests refuse to run: the key does not answer in test mode (Rewloy-Mode: " + (biz.Mode ?? "(none)") + ").");
            log("Live tests against " + baseUrl + " (" + biz.Data.Name + ", dev, test key), run " + tag);

            Guid locationId = Guid.Empty;
            Guid stampId = Guid.Empty, giftId = Guid.Empty, batchProgramId = Guid.Empty;
            string stampName = "LT damga " + tag, giftName = "LT hediye " + tag, batchProgName = "LT kod " + tag;
            string stampSerial = "", stampSerial2 = "", giftSerial = "";
            string saleKey = "lt-" + tag + "-sale-1", spendKey = "lt-" + tag + "-spend-1";
            string email1 = "lt-" + tag + "-1@example.com", email2 = "lt-" + tag + "-2@example.com", email3 = "lt-" + tag + "-3@example.com";

            try
            {
                // ---------------------------------------------------------------- meta and business
                await run.Check("meta+business", "meta: version, apiVersion, environment dev", async () =>
                {
                    Assert.False(string.IsNullOrEmpty(meta.Data.Version));
                    Assert.Equal("v1", meta.Data.ApiVersion);
                    Assert.Equal("dev", environment);
                });
                await run.Check("meta+business", "business: test environment in TRY", () =>
                {
                    Assert.NotEqual(Guid.Empty, biz.Data.Id);
                    Assert.Equal("TRY", biz.Data.Currency);
                    Assert.True(biz.IsTestMode);
                    return Task.CompletedTask;
                });
                await run.Check("meta+business", "locations: the test business has a branch", async () =>
                {
                    var locs = await rw.ListLocationsAsync();
                    Assert.NotEmpty(locs);
                    locationId = locs[0].Id;
                });

                // ---------------------------------------------------------------- rate-limit headers
                await run.Check("ratelimit", "RateLimit-* headers are read", async () =>
                {
                    var r = await rw.GetBusinessWithResponseAsync();
                    Assert.NotNull(r.RateLimit);
                    Assert.True(r.RateLimit!.Limit > 0);
                    Assert.InRange(r.RateLimit.Remaining, 0, r.RateLimit.Limit);
                    Assert.True(r.RateLimit.ResetSeconds >= 0);
                    Assert.False(string.IsNullOrEmpty(r.RequestId));
                    Assert.Equal("test", r.Mode);
                });

                // ---------------------------------------------------------------- programs
                await run.Check("programs", "create a stamp program", async () =>
                {
                    var p = await rw.CreateProgramAsync(new CreateProgramBody { Type = "stamp", BusinessName = "LT Kafe", ProgramName = stampName, MaxStamps = 4, RewardName = "Bedava kahve" });
                    stampId = p.Id;
                    cleanup.Add(("program " + stampName, () => RemoveProgram(rw, stampId, stampName)));
                    Assert.Equal("stamp", p.Type);
                    Assert.Equal("active", p.Status);
                    Assert.False(string.IsNullOrEmpty(p.JoinUrl));
                });
                await run.Check("programs", "create a gift card program", async () =>
                {
                    var p = await rw.CreateProgramAsync(new CreateProgramBody { Type = "giftcard", BusinessName = "LT Kafe", ProgramName = giftName });
                    giftId = p.Id;
                    cleanup.Add(("program " + giftName, () => RemoveProgram(rw, giftId, giftName)));
                    Assert.Equal("giftcard", p.Type);
                });
                await run.Check("programs", "create a second gift card program for the code (batch) flows", async () =>
                {
                    var p = await rw.CreateProgramAsync(new CreateProgramBody { Type = "giftcard", BusinessName = "LT Kafe", ProgramName = batchProgName });
                    batchProgramId = p.Id;
                    cleanup.Add(("program " + batchProgName, () => RemoveProgram(rw, batchProgramId, batchProgName)));
                });
                await run.Check("programs", "list contains both; get returns the same", async () =>
                {
                    var list = await rw.ListProgramsAsync();
                    Assert.Contains(list, p => p.Id == stampId && p.Type == "stamp");
                    Assert.Contains(list, p => p.Id == giftId && p.Type == "giftcard");
                    var one = await rw.GetProgramAsync(stampId);
                    Assert.Equal(stampName, one.ProgramName);
                    Assert.Equal(4, one.Config.GetProperty("maxStamps").GetInt32());
                });

                // ---------------------------------------------------------------- passes
                await run.Check("passes", "issue a stamp card", async () =>
                {
                    var r = await rw.IssuePassWithResponseAsync(new IssuePassBody { ProgramId = stampId, Email = email1, FirstName = "Ayşe", KvkkConsent = true });
                    Assert.Equal(201, r.StatusCode);
                    Assert.True(r.Data.Created);
                    Assert.False(string.IsNullOrEmpty(r.Data.CardUrl));
                    stampSerial = r.Data.Serial;
                });
                await run.Check("passes", "issue with IfExists=return gives the same card", async () =>
                {
                    var again = await rw.IssuePassAsync(new IssuePassBody { ProgramId = stampId, Email = email1, KvkkConsent = true, IfExists = "return" });
                    Assert.False(again.Created);
                    Assert.Equal(stampSerial, again.Serial);
                });
                await run.Check("passes", "issue a second stamp card (for paging and search)", async () =>
                {
                    stampSerial2 = (await rw.IssuePassAsync(new IssuePassBody { ProgramId = stampId, Email = email2, FirstName = "Ali", KvkkConsent = true })).Serial;
                    Assert.NotEqual(stampSerial, stampSerial2);
                });
                await run.Check("passes", "issue a gift card with a face value", async () =>
                {
                    var r = await rw.IssuePassAsync(new IssuePassBody { ProgramId = giftId, Email = email3, FirstName = "Veli", KvkkConsent = true, FaceMinor = 10000 });
                    giftSerial = r.Serial;
                });
                await run.Check("passes", "get: stamp card state and ready actions", async () =>
                {
                    var p = await rw.GetPassAsync(stampSerial);
                    Assert.Equal("stamp", p.Type);
                    Assert.Equal("active", p.Status);
                    Assert.Equal(0, p.Stamps!.Count);
                    Assert.Equal(4, p.Stamps.Max);
                    Assert.False(p.RewardReady);
                    Assert.Contains(p.Actions, a => a.Action == "earn-stamps" && a.Ready);
                });
                await run.Check("passes", "get: gift card balance", async () =>
                {
                    var p = await rw.GetPassAsync(giftSerial);
                    Assert.Equal("giftcard", p.Type);
                    Assert.Equal(10000, p.Money!.AmountMinor);
                    Assert.Equal("TRY", p.Money.Currency);
                });
                await run.Check("passes", "till view: allowed at the branch", async () =>
                {
                    var till = await rw.GetPassTillAsync(stampSerial, new GetPassTillQuery { LocationId = locationId });
                    Assert.True(till.Allowed);
                    Assert.NotNull(till.Notices);
                });

                // ---------------------------------------------------------------- sales
                await run.Check("sales", "recordSale with location and receipt reference", async () =>
                {
                    var r = await rw.RecordSaleWithResponseAsync(stampSerial,
                        new RecordSaleBody { LocationId = locationId, AmountMinor = 5000, Reference = "fis-" + tag },
                        new RequestOptions { IdempotencyKey = saleKey });
                    Assert.Equal(200, r.StatusCode);
                    Assert.Equal("stamp", r.Data.Type);
                    Assert.False(r.Data.Duplicate);
                    Assert.True(r.Data.Credited >= 1);
                    Assert.Equal(1, r.Data.Card!.Stamps!.Count);
                });
                await run.Check("sales", "recordSale without receipt reference and without a branch (online)", async () =>
                {
                    var r = await rw.RecordSaleAsync(stampSerial2, new RecordSaleBody { AmountMinor = 2500 }, new RequestOptions { IdempotencyKey = "lt-" + tag + "-sale-online" });
                    Assert.False(r.Duplicate);
                    Assert.True(r.Credited >= 1);
                });
                await run.Check("sales", "recordSale without an Idempotency-Key is refused by the library before sending (it never makes one up)", () =>
                {
                    var ex = Assert.ThrowsAsync<ArgumentException>(() => rw.RecordSaleAsync(stampSerial2, new RecordSaleBody { AmountMinor = 100 }));
                    return ex;
                });

                // ---------------------------------------------------------------- pass actions
                await run.Check("actions", "earn-stamps reaches the reward", async () =>
                {
                    var r = await rw.PassActionAsync(stampSerial, new PassActionBody { Action = "earn-stamps", LocationId = locationId, Count = 3 }, new RequestOptions { IdempotencyKey = "lt-" + tag + "-earn-1" });
                    Assert.False(r.Duplicate);
                    Assert.Equal(4, r.Card!.Stamps!.Count);
                    Assert.True(r.Card.RewardReady);
                });
                await run.Check("actions", "redeem-stamps pays the reward out", async () =>
                {
                    var r = await rw.PassActionAsync(stampSerial, new PassActionBody { Action = "redeem-stamps", LocationId = locationId }, new RequestOptions { IdempotencyKey = "lt-" + tag + "-redeem-1" });
                    Assert.False(r.Card!.RewardReady);
                    Assert.Equal(0, r.Card.Stamps!.Count);
                });
                await run.Check("actions", "spend from the gift card", async () =>
                {
                    var r = await rw.PassActionAsync(giftSerial, new PassActionBody { Action = "spend", LocationId = locationId, AmountMinor = 2500 }, new RequestOptions { IdempotencyKey = spendKey });
                    Assert.Equal(7500, (await rw.GetPassAsync(giftSerial)).Money!.AmountMinor);
                    Assert.False(r.Duplicate);
                });
                await run.Check("actions", "an action the card cannot do is refused with a code", async () =>
                {
                    var e = await Refused(() => rw.PassActionAsync(giftSerial, new PassActionBody { Action = "spend", LocationId = locationId, AmountMinor = 99999999 }, new RequestOptions { IdempotencyKey = "lt-" + tag + "-spend-big" }));
                    Assert.True(e.Status >= 400 && e.Status < 500);
                    Assert.False(string.IsNullOrEmpty(e.Code));
                });

                // ---------------------------------------------------------------- operations and reversals
                await run.Check("operations", "the operations list shows sale, stamps and redeem", async () =>
                {
                    var page = await rw.ListPassOperationsAsync(stampSerial);
                    Assert.True(page.Meta.Total >= 3, "total " + page.Meta.Total);
                    Assert.Contains(page.Data, o => o.SaleKey == saleKey);
                    Assert.All(page.Data, o => Assert.NotEqual(Guid.Empty, o.Id));
                });
                await run.Check("operations", "reverseSale of a stamp already redeemed is refused: SALE_ALREADY_SPENT", async () =>
                {
                    var e = await Refused(() => rw.ReverseSaleAsync(stampSerial, new ReverseSaleBody { SaleKey = saleKey }));
                    Assert.Equal(409, e.Status);
                    Assert.Equal("SALE_ALREADY_SPENT", e.Code);
                });
                await run.Check("operations", "reverseSale takes an unspent sale back once", async () =>
                {
                    var onlineKey = "lt-" + tag + "-sale-online";
                    var r = await rw.ReverseSaleAsync(stampSerial2, new ReverseSaleBody { SaleKey = onlineKey });
                    Assert.True(r.Reversed >= 1);
                    Assert.False(r.Duplicate);
                    var again = await rw.ReverseSaleAsync(stampSerial2, new ReverseSaleBody { SaleKey = onlineKey });
                    Assert.True(again.Duplicate);
                    var ops = await rw.ListPassOperationsAsync(stampSerial2);
                    Assert.Contains(ops.Data, o => o.SaleKey == onlineKey && o.ReversedAt != null);
                });
                await run.Check("operations", "reverseAction takes the gift card spend back", async () =>
                {
                    var ops = await rw.ListPassOperationsAsync(giftSerial);
                    var spend = ops.Data.FirstOrDefault(o => o.ActionKey == spendKey);
                    Assert.NotNull(spend);
                    Assert.True(spend!.Reversible);
                    var r = await rw.ReverseActionAsync(giftSerial, new ReverseActionBody { ActionKey = spendKey });
                    Assert.True(r.Restored >= 2500);
                    Assert.Equal(10000, (await rw.GetPassAsync(giftSerial)).Money!.AmountMinor);
                    var after = await rw.ListPassOperationsAsync(giftSerial);
                    Assert.Contains(after.Data, o => o.ActionKey == spendKey && o.ReversedAt != null);
                });

                // ---------------------------------------------------------------- customers
                await run.Check("customers", "search by e-mail finds the customer and card", async () =>
                {
                    var found = await rw.ListCustomersAsync(new ListCustomersQuery { Q = email1 });
                    Assert.Single(found.Data);
                    var c = found.Data[0];
                    Assert.Equal("Ayşe", c.FirstName);
                    Assert.Contains(c.Cards, card => card.Serial == stampSerial);
                });
                await run.Check("customers", "search for nobody returns an empty page", async () =>
                {
                    var none = await rw.ListCustomersAsync(new ListCustomersQuery { Q = "nobody-" + tag + "@example.com" });
                    Assert.Empty(none.Data);
                    Assert.Equal(0, none.Meta.Total);
                });

                // ---------------------------------------------------------------- pagination
                await run.Check("pagination", "limit 1 pages through every customer", async () =>
                {
                    var p1 = await rw.ListCustomersAsync(new ListCustomersQuery { Limit = 1, Page = 1 });
                    Assert.Single(p1.Data);
                    Assert.Equal(1, p1.Meta.PageSize);
                    Assert.True(p1.Meta.Total >= 3, "total " + p1.Meta.Total);
                    var p2 = await rw.ListCustomersAsync(new ListCustomersQuery { Limit = 1, Page = 2 });
                    Assert.NotEqual(p1.Data[0].PersonId, p2.Data[0].PersonId);
                    var all = new List<Guid>();
                    await foreach (var c in rw.ListCustomersAllAsync(new ListCustomersQuery { Limit = 1 })) all.Add(c.PersonId);
                    Assert.Equal(p1.Meta.Total, all.Count);
                    Assert.Equal(all.Count, all.Distinct().Count());
                });
                await run.Check("pagination", "operations paged one by one equal the full list", async () =>
                {
                    var full = await rw.ListPassOperationsAsync(stampSerial);
                    var walked = new List<Guid>();
                    await foreach (var o in rw.ListPassOperationsAllAsync(stampSerial, new ListPassOperationsQuery { Limit = 1 })) walked.Add(o.Id);
                    Assert.Equal(full.Meta.Total, walked.Count);
                });

                // ---------------------------------------------------------------- batches
                Guid openBatch = Guid.Empty, closedBatch = Guid.Empty, lastBatch = Guid.Empty;
                await run.Check("batches", "create a code, list it per program and in the all-batches list", async () =>
                {
                    var b = await rw.CreateBatchAsync(batchProgramId, new CreateBatchBody { Name = "LT kod A " + tag, ValueMinor = 5000, Capacity = 2 });
                    openBatch = b.Id;
                    Assert.Equal("open", b.Status);
                    Assert.Equal(2, b.Capacity);
                    Assert.False(string.IsNullOrEmpty(b.ClaimUrl));
                    var per = await rw.ListBatchesAsync(batchProgramId);
                    Assert.Contains(per, x => x.Id == openBatch);
                    var all = new List<ListAllBatchesItem>();
                    await foreach (var x in rw.ListAllBatchesAllAsync(new ListAllBatchesQuery { Status = "open", Limit = 1 })) all.Add(x);
                    Assert.Contains(all, x => x.Id == openBatch && x.State == "open");
                });
                await run.Check("batches", "an invalid code (no value on a gift card) is refused with INVALID_BATCH", async () =>
                {
                    var e = await Refused(() => rw.CreateBatchAsync(batchProgramId, new CreateBatchBody { Name = "LT eksik" }));
                    Assert.Equal(ErrorCode.InvalidBatch, e.Code);
                    Assert.Equal(422, e.Status);
                });
                await run.Check("batches", "send link on an open code is queued (nothing leaves a test environment)", async () =>
                {
                    var r = await rw.SendBatchLinkAsync(openBatch, new SendBatchLinkBody { Email = "lt-" + tag + "-send@example.com" });
                    Assert.Equal("queued", r.Result);
                });
                await run.Check("batches", "send link on a closed code is refused: BATCH_CLOSED", async () =>
                {
                    closedBatch = (await rw.CreateBatchAsync(batchProgramId, new CreateBatchBody { Name = "LT kod B " + tag, ValueMinor = 1000 })).Id;
                    var closed = await rw.CloseBatchAsync(closedBatch);
                    Assert.Equal("closed", closed.Status);
                    var e = await Refused(() => rw.SendBatchLinkAsync(closedBatch, new SendBatchLinkBody { Email = "lt-" + tag + "-send@example.com" }));
                    Assert.Equal(ErrorCode.BatchClosed, e.Code);
                    Assert.Equal(410, e.Status);
                });
                await run.Check("batches", "closed codes show in the status filter", async () =>
                {
                    var closed = await rw.ListAllBatchesAsync(new ListAllBatchesQuery { Status = "closed" });
                    Assert.Contains(closed.Data, x => x.Id == closedBatch);
                });
                await run.Check("batches", "send link on a code of an archived program is refused: PROGRAM_ARCHIVED (create too)", async () =>
                {
                    lastBatch = (await rw.CreateBatchAsync(batchProgramId, new CreateBatchBody { Name = "LT kod C " + tag, ValueMinor = 1000 })).Id;
                    await rw.ArchiveProgramAsync(batchProgramId, new ArchiveProgramBody { ConfirmName = batchProgName });
                    var send = await Refused(() => rw.SendBatchLinkAsync(lastBatch, new SendBatchLinkBody { Email = "lt-" + tag + "-send@example.com" }));
                    // Archiving closes the program's open codes, and the server checks "closed" before "archived": the refusal
                    // a client sees is BATCH_CLOSED (410) here; PROGRAM_ARCHIVED (409) is the documented one for the archived-but-open case.
                    Assert.True((send.Code == ErrorCode.ProgramArchived && send.Status == 409) || (send.Code == ErrorCode.BatchClosed && send.Status == 410), send.Code + " " + send.Status);
                    var create = await Refused(() => rw.CreateBatchAsync(batchProgramId, new CreateBatchBody { Name = "LT kod D", ValueMinor = 1000 }));
                    Assert.Equal(ErrorCode.ProgramArchived, create.Code);
                    var row = (await rw.ListAllBatchesAsync(new ListAllBatchesQuery { Q = "LT kod C " + tag })).Data.FirstOrDefault(x => x.Id == lastBatch);
                    Assert.True(row?.State == "archived" || row?.State == "closed", "state " + row?.State); // archiving closes open codes
                });

                // ---------------------------------------------------------------- webhooks
                await run.Check("webhooks", "events catalogue is readable", async () =>
                {
                    var ev = await rw.WebhookEventsAsync();
                    Assert.Contains(ev.Events, e => e.Event == "pass.issued");
                });
                Guid hookId = Guid.Empty;
                string hookSecret = "";
                await run.Check("webhooks", "create to an https URL that does not resolve publicly: refused or created as documented", async () =>
                {
                    var url = "https://lt-" + tag + ".invalid/hook";
                    try
                    {
                        var h = await rw.CreateWebhookAsync(new CreateWebhookBody { Url = url, Events = new[] { "pass.issued" } });
                        hookId = h.Webhook.Id;
                        hookSecret = h.Secret;
                        cleanup.Add(("webhook " + hookId, async () => { try { await rw.DeleteWebhookAsync(hookId); } catch (RewloyException x) when (x.Status == 404) { } }));
                        Assert.StartsWith("whsec_", h.Secret);
                        Assert.Equal("active", h.Webhook.Status);
                    }
                    catch (RewloyException e) when (e.Code == ErrorCode.BadWebhookUrl)
                    {
                        Assert.Equal(422, e.Status); // documented refusal
                    }
                });
                await run.Check("webhooks", "an internal address is refused (BAD_WEBHOOK_URL) or, on a dev server, accepted with warnings", async () =>
                {
                    Guid made = Guid.Empty;
                    try
                    {
                        var h = await rw.CreateWebhookAsync(new CreateWebhookBody { Url = "http://127.0.0.1:9/lt-" + tag, Events = new[] { "pass.issued" } });
                        made = h.Webhook.Id;
                        var id = made;
                        cleanup.Add(("webhook " + id, async () => { try { await rw.DeleteWebhookAsync(id); } catch (RewloyException x) when (x.Status == 404) { } }));
                        Assert.NotNull(h.Warnings);
                        Assert.NotEmpty(h.Warnings!);
                    }
                    catch (RewloyException e) when (e.Code == ErrorCode.BadWebhookUrl)
                    {
                        Assert.Equal(422, e.Status);
                    }
                    finally
                    {
                        if (made != Guid.Empty) { try { await rw.DeleteWebhookAsync(made); } catch (RewloyException) { } }
                    }
                });
                if (hookId == Guid.Empty)
                    run.Skip("webhooks", "list, get, rotate secret, delete", "the server refused the https test URL, so there is no webhook to manage");
                else
                {
                    await run.Check("webhooks", "list and get show it", async () =>
                    {
                        var list = await rw.ListWebhooksAsync();
                        Assert.Contains(list, w => w.Id == hookId);
                        var one = await rw.GetWebhookAsync(hookId);
                        Assert.Equal(hookId, one.Id);
                    });
                    await run.Check("webhooks", "rotate secret gives a new one and a grace period", async () =>
                    {
                        var r = await rw.RotateWebhookSecretAsync(hookId);
                        Assert.StartsWith("whsec_", r.Secret);
                        Assert.NotEqual(hookSecret, r.Secret);
                        Assert.True(r.PreviousValidUntil > DateTimeOffset.UtcNow);
                    });
                    await run.Check("webhooks", "delete removes it", async () =>
                    {
                        await rw.DeleteWebhookAsync(hookId);
                        var list = await rw.ListWebhooksAsync();
                        Assert.DoesNotContain(list, w => w.Id == hookId);
                    });
                }

                // ---------------------------------------------------------------- idempotency
                await run.Check("idempotency", "recordSale: the same key answers the same, marked duplicate, with card", async () =>
                {
                    var key1 = "lt-" + tag + "-idem-1";
                    var body = new RecordSaleBody { LocationId = locationId, AmountMinor = 3000, Reference = "idem-" + tag };
                    var first = await rw.RecordSaleWithResponseAsync(stampSerial2, body, new RequestOptions { IdempotencyKey = key1 });
                    var second = await rw.RecordSaleWithResponseAsync(stampSerial2, body, new RequestOptions { IdempotencyKey = key1 });
                    Assert.False(first.Data.Duplicate);
                    Assert.True(second.Data.Duplicate);   // the ledger answers a repeat of a sale key itself
                    Assert.Equal(first.Data.Credited, second.Data.Credited);
                    Assert.NotNull(first.Data.Card);
                    Assert.NotNull(second.Data.Card);
                    Assert.Equal(stampSerial2, second.Data.Card!.Serial);
                    Assert.Equal(first.Data.Card!.Balance, second.Data.Card.Balance);
                });
                await run.Check("idempotency", "issuePass: the same key replays the first answer (Idempotent-Replayed)", async () =>
                {
                    var key3 = "lt-" + tag + "-idem-issue";
                    var body = new IssuePassBody { ProgramId = stampId, Email = "lt-" + tag + "-idem@example.com", KvkkConsent = true };
                    var first = await rw.IssuePassWithResponseAsync(body, new RequestOptions { IdempotencyKey = key3 });
                    var second = await rw.IssuePassWithResponseAsync(body, new RequestOptions { IdempotencyKey = key3 });
                    Assert.False(first.Replayed);
                    Assert.True(second.Replayed);
                    Assert.Equal(first.Data.Serial, second.Data.Serial);
                    Assert.True(first.Data.Created);
                });
                await run.Check("idempotency", "the same key with another body is refused: IDEMPOTENCY_KEY_REUSED", async () =>
                {
                    var key2 = "lt-" + tag + "-idem-2";
                    await rw.RecordSaleAsync(stampSerial2, new RecordSaleBody { AmountMinor = 1000 }, new RequestOptions { IdempotencyKey = key2 });
                    var e = await Refused(() => rw.RecordSaleAsync(stampSerial2, new RecordSaleBody { AmountMinor = 2000 }, new RequestOptions { IdempotencyKey = key2 }));
                    Assert.Equal(ErrorCode.IdempotencyKeyReused, e.Code);
                });

                // ---------------------------------------------------------------- error objects
                await run.Check("errors", "404: PASS_NOT_FOUND with status, request id and docs", async () =>
                {
                    var e = await Refused(() => rw.GetPassAsync("ZZZZ-ZZZZ-ZZZZ"));
                    Assert.Equal(404, e.Status);
                    Assert.Equal(ErrorCode.PassNotFound, e.Code);
                    Assert.False(string.IsNullOrEmpty(e.RequestId));
                    Assert.Contains(ErrorCode.PassNotFound, e.Docs ?? "");
                    Assert.Equal("getPass", e.Operation);
                });
                await run.Check("errors", "validation: VALIDATION 400 names the field", async () =>
                {
                    var e = await Refused(() => rw.CreateProgramAsync(new CreateProgramBody { Type = "bogus", BusinessName = "LT" }));
                    Assert.Equal(400, e.Status);
                    Assert.Equal(ErrorCode.Validation, e.Code);
                    Assert.NotNull(e.Details);
                    Assert.Contains("type", e.Details!.Value.GetRawText());
                    Assert.False(string.IsNullOrEmpty(e.RequestId));
                });
                await run.Check("errors", "a wrong key is refused before any data is returned", async () =>
                {
                    using var bad = new RewloyClient(new RewloyClientOptions { ApiKey = "rwk_test_" + new string('0', 40), BaseUrl = baseUrl, MaxRetries = 0 });
                    var e = await Refused(() => bad.GetBusinessAsync());
                    Assert.Equal(401, e.Status);
                    Assert.False(string.IsNullOrEmpty(e.Code));
                });
            }
            catch (Exception unexpected)
            {
                run.Skip("suite", "aborted", unexpected.GetType().Name + ": " + unexpected.Message);
                log(unexpected.ToString());
                throw;
            }
            finally
            {
                // ---------------------------------------------------------------- cleanup and the test reset (the very end)
                foreach (var c in cleanup.Where(c => c.Name.StartsWith("webhook", StringComparison.Ordinal)).ToList())
                    await run.Check("cleanup", c.Name, c.Run);
                cleanup.RemoveAll(c => c.Name.StartsWith("webhook", StringComparison.Ordinal));

                if (LiveEnv.Session == null)
                {
                    await run.Check("reset", "a key may not reset: CREDENTIAL_NOT_ALLOWED (a team session is needed)", async () =>
                    {
                        var e = await Refused(() => rw.ResetTestEnvironmentAsync());
                        Assert.Equal(403, e.Status);
                        Assert.Equal(ErrorCode.CredentialNotAllowed, e.Code);
                    });
                    run.Skip("reset", "reset the test environment", "set REWLOY_SESSION (rws_… of the test business) to run it; without it the suite removes what it can by hand");
                    foreach (var c in cleanup) await run.Check("cleanup", c.Name, c.Run);
                }
                else
                {
                    var bizId = biz.Data.Id;
                    using var staff = new RewloyClient(new RewloyClientOptions { StaffSession = LiveEnv.Session, BaseUrl = baseUrl, Merchant = bizId, Timeout = TimeSpan.FromSeconds(60) });
                    await run.Check("reset", "resetTestEnvironment clears customers, cards and codes and keeps the environment", async () =>
                    {
                        var r = await staff.ResetTestEnvironmentAsync();
                        Assert.Equal(bizId, r.MerchantId);
                        Assert.False(r.Created);
                        Assert.True(r.Deleted.Customers >= 3, "customers " + r.Deleted.Customers);
                        Assert.True(r.Deleted.Cards >= 3, "cards " + r.Deleted.Cards);
                        Assert.True(r.Kept.Keys >= 1);
                        Assert.Null(r.Closed);
                    });
                    await run.Check("reset", "afterwards the key still works and no customers are left", async () =>
                    {
                        var c = await rw.ListCustomersAsync();
                        Assert.Equal(0, c.Meta.Total);
                        var e = await Refused(() => rw.GetPassAsync(string.IsNullOrEmpty(stampSerial) ? "ZZZZ-ZZZZ-ZZZZ" : stampSerial));
                        Assert.Equal(404, e.Status);
                    });
                    // Programs stay after a reset by design; with the cards gone they can be removed.
                    foreach (var c in cleanup) await run.Check("cleanup", c.Name, c.Run);
                }
                log(run.Summary());
            }
            Assert.True(run.FailedCount == 0, run.FailedCount + " live check(s) failed; the per-area summary is above.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>environment from GET /v1/meta: a typed property if the regenerated library has one, else the raw field.</summary>
        private static string? EnvironmentOf(GetMetaData data)
        {
            var typed = data.GetType().GetProperty("Environment", BindingFlags.Public | BindingFlags.Instance);
            if (typed != null && typed.GetValue(data) is string s) return s;
            if (data.AdditionalProperties != null && data.AdditionalProperties.TryGetValue("environment", out var el) && el.ValueKind == JsonValueKind.String) return el.GetString();
            return null;
        }

        private static async Task<RewloyException> Refused<T>(Func<Task<T>> call)
        {
            try { await call(); }
            catch (RewloyException e) { return e; }
            throw new Xunit.Sdk.XunitException("expected the API to refuse the call, but it succeeded");
        }

        /// <summary>Archives and deletes a program (cards must be gone: after the reset, or none issued).</summary>
        private static async Task RemoveProgram(RewloyClient rw, Guid id, string name)
        {
            try { await rw.ArchiveProgramAsync(id, new ArchiveProgramBody { ConfirmName = name }); } catch (RewloyException e) when (e.Status == 404 || e.Status == 409) { }
            try { await rw.DeleteProgramAsync(id, new DeleteProgramQuery { ConfirmName = name }); }
            catch (RewloyException e) when (e.Code == "PROGRAM_HAS_CARDS") { } // archived; the cards go with the next test reset
        }
    }
}
