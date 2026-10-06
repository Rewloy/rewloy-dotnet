using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    /// <summary>The operations and fields Rewloy API 1.2.0 added.</summary>
    public class V120Tests
    {
        private const string Serial = "ABCD-EFGH-JKLM";
        private static readonly Guid WebhookId = Guid.Parse("0192f7c1-0000-7000-8000-0000000000aa");
        private static readonly Guid Location = Guid.Parse("0192f7c1-0000-7000-8000-000000000003");

        [Fact]
        public void Knows_the_new_operations()
        {
            foreach (var id in new[] { "listPassOperations", "listAllBatches", "rotateWebhookSecret", "deleteWebhook" }) Assert.True(RewloyOperations.All.ContainsKey(id), id);
            Assert.True(RewloyOperations.All["listPassOperations"].IsPaged);
            Assert.True(RewloyOperations.All["listAllBatches"].IsPaged);
        }

        [Fact]
        public async Task Lists_a_cards_operations_and_pages_them()
        {
            const string items = "[{\"id\":\"0192f7c1-0000-7000-8000-0000000000e1\",\"kind\":\"earn\",\"delta\":1,\"unit\":\"stamp\",\"saleKey\":\"kasa3-z0187-fis0042\",\"undoWith\":\"sale/reverse\",\"reversible\":true,\"byCaller\":true}]";
            var stub = new StubHandler().Then(Reply.Page(items, 1, 10, 1)).Then(Reply.Page(items, 1, 50, 1));
            using var client = Clients.Make(stub);

            var page = await client.ListPassOperationsAsync(Serial, new ListPassOperationsQuery { Limit = 10 });
            var op = page.Data[0];
            Assert.Equal("sale/reverse", op.UndoWith);
            Assert.True(op.Reversible);
            Assert.Equal("kasa3-z0187-fis0042", op.SaleKey);
            Assert.Equal("/v1/passes/ABCD-EFGH-JKLM/operations", stub.Requests[0].Uri.AbsolutePath);
            Assert.Contains("limit=10", stub.Requests[0].Uri.Query);

            var seen = new List<Guid>();
            await foreach (var o in client.ListPassOperationsAllAsync(Serial)) seen.Add(o.Id);
            Assert.Equal(new[] { Guid.Parse("0192f7c1-0000-7000-8000-0000000000e1") }, seen);
        }

        [Fact]
        public async Task Lists_every_batch_with_the_archived_state()
        {
            var stub = new StubHandler().Then(Reply.Page("[{\"id\":\"0192f7c1-0000-7000-8000-0000000000bb\",\"status\":\"open\",\"state\":\"archived\"}]", 1, 50, 1));
            using var client = Clients.Make(stub);
            var page = await client.ListAllBatchesAsync(new ListAllBatchesQuery { Status = "archived" });
            Assert.Equal("archived", page.Data[0].State);
            Assert.Equal("/v1/batches", stub.Requests[0].Uri.AbsolutePath);
            Assert.Contains("status=archived", stub.Requests[0].Uri.Query);
        }

        [Fact]
        public async Task Rotates_and_deletes_a_webhook()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"secret\":\"whsec_new\",\"previousValidUntil\":\"2026-10-07T10:00:00.000Z\"}")).Then(Reply.NoContent());
            using var client = Clients.Make(stub);
            var rotated = await client.RotateWebhookSecretAsync(WebhookId);
            Assert.Equal("whsec_new", rotated.Secret);
            Assert.Equal("POST", stub.Requests[0].Method);
            Assert.Equal("/v1/developers/webhooks/" + WebhookId + "/rotate-secret", stub.Requests[0].Uri.AbsolutePath);
            await client.DeleteWebhookAsync(WebhookId);
            Assert.Equal("DELETE", stub.Requests[1].Method);
        }

        [Fact]
        public async Task Creates_a_pos_key_and_resets_the_test_environment_with_revoke_keys()
        {
            var stub = new StubHandler()
                .Then(Reply.Ok("{\"token\":\"rwk_x_y\",\"baseUrl\":\"https://app.rewloy.com\"}", System.Net.HttpStatusCode.Created))
                .Then(Reply.Ok("{\"keysRevoked\":true}"));
            using var client = Clients.Make(stub);
            var created = await client.CreateApiKeyAsync(new CreateApiKeyBody { Kind = "pos", LocationId = Location, Register = "Kasa 1", Password = "x" });
            Assert.Equal("https://app.rewloy.com", created.BaseUrl);
            var body = stub.Requests[0].Json();
            Assert.Equal("pos", body.GetProperty("kind").GetString());
            Assert.Equal("Kasa 1", body.GetProperty("register").GetString());
            Assert.False(body.TryGetProperty("roleId", out _));

            var reset = await client.ResetTestEnvironmentAsync(new ResetTestEnvironmentBody { RevokeKeys = true });
            Assert.True(reset.KeysRevoked);
            Assert.True(stub.Requests[1].Json().GetProperty("revokeKeys").GetBoolean());
        }

        [Fact]
        public async Task Reads_card_and_reversed_on_a_replayed_sale_and_card_may_be_null()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"type\":\"stamp\",\"applied\":\"stamps\",\"credited\":1,\"balance\":3,\"duplicate\":true,\"reversed\":true,\"rewardReady\":false,\"rewardsReady\":0,\"card\":null}"));
            using var client = Clients.Make(stub);
            var sale = await client.RecordSaleAsync(Serial, new RecordSaleBody { LocationId = Location, AmountMinor = 100 }, new RequestOptions { IdempotencyKey = "kasa3-z0187-fis0042" });
            Assert.True(sale.Reversed);
            Assert.Null(sale.Card);
        }

        [Fact]
        public async Task Surfaces_program_archived()
        {
            var stub = new StubHandler().Then(Reply.Error(409, "PROGRAM_ARCHIVED", "Program arşivde"));
            using var client = Clients.Make(stub);
            var ex = await Assert.ThrowsAsync<RewloyException>(() => client.CreateBatchAsync(Guid.Parse("0192f7c1-0000-7000-8000-0000000000cc"), new CreateBatchBody()));
            Assert.Equal(409, ex.Status);
            Assert.Equal(ErrorCode.ProgramArchived, ex.Code);
        }

        // A webhook object as 1.2.0 answers: pausedUntil and resumableUntil are always present.
        private static string WebhookRow(string? pausedUntil, string? resumableUntil)
        {
            static string Q(string? v) => v == null ? "null" : "\"" + v + "\"";
            return "{\"id\":\"" + WebhookId + "\",\"url\":\"https://ornek.com/rewloy/webhook\",\"events\":[\"pass.activity\"],\"status\":\"active\",\"failures\":0,\"disabledReason\":null,"
                + "\"createdAt\":\"2026-10-06T09:00:00.000Z\",\"week\":{\"delivered\":3,\"failed\":0,\"pending\":1},\"lastDelivered\":\"2026-10-06T09:30:00.000Z\",\"createdByKey\":null,"
                + "\"pausedUntil\":" + Q(pausedUntil) + ",\"resumableUntil\":" + Q(resumableUntil) + "}";
        }

        [Fact]
        public async Task Reads_the_webhook_state_fields_a_date_time_or_null()
        {
            var stub = new StubHandler()
                .Then(Reply.Ok("[" + WebhookRow("2026-10-06T10:01:00.000Z", null) + "," + WebhookRow(null, "2026-10-07T09:45:00.000Z") + "]"))
                .Then(Reply.Ok(WebhookRow(null, null)));
            using var client = Clients.Make(stub);

            var rows = await client.ListWebhooksAsync();
            DateTimeOffset? paused = rows[0].PausedUntil;
            Assert.Equal(DateTimeOffset.Parse("2026-10-06T10:01:00.000Z"), paused);
            Assert.Null(rows[0].ResumableUntil);
            Assert.Null(rows[1].PausedUntil);
            Assert.Equal(DateTimeOffset.Parse("2026-10-07T09:45:00.000Z"), rows[1].ResumableUntil);

            var turnedOn = await client.SetWebhookStatusAsync(WebhookId, new SetWebhookStatusBody { Active = true });
            Assert.Null(turnedOn.PausedUntil);
            Assert.Null(turnedOn.ResumableUntil);
            Assert.Equal("PATCH", stub.Requests[1].Method);
        }

        [Fact]
        public async Task Surfaces_what_SendBatchLink_refuses()
        {
            var stub = new StubHandler()
                .Then(Reply.Error(410, "BATCH_CLOSED")).Then(Reply.Error(410, "BATCH_EXPIRED")).Then(Reply.Error(410, "BATCH_FULL")).Then(Reply.Error(409, "PROGRAM_ARCHIVED"));
            using var client = Clients.Make(stub);
            var batch = Guid.Parse("0192f7c1-0000-7000-8000-0000000000cc");
            var expected = new[] { (410, ErrorCode.BatchClosed), (410, ErrorCode.BatchExpired), (410, ErrorCode.BatchFull), (409, ErrorCode.ProgramArchived) };
            foreach (var (status, code) in expected)
            {
                var ex = await Assert.ThrowsAsync<RewloyException>(() => client.SendBatchLinkAsync(batch, new SendBatchLinkBody { Email = "ali@ornek.com" }));
                Assert.Equal(status, ex.Status);
                Assert.Equal(code, ex.Code);
            }
            Assert.Equal("/v1/batches/" + batch + "/send", stub.Requests[0].Uri.AbsolutePath);
        }

        /// <summary>The README's 1.2.0 snippets: compiled, not run.</summary>
        private static async Task CompileOnly(RewloyClient rewloy, string seri, Guid webhookId, string hamGovde, string imzaBasligi, string eskiSir)
        {
            var kart = await rewloy.GetPassAsync(seri);
            var odul = kart.Actions.Any(a => (a.Action == "redeem-stamps" || a.Action == "redeem-reward") && a.Ready);
            Console.WriteLine($"{kart.Type} {kart.Balance} {odul}");

            var satis = await rewloy.RecordSaleAsync(seri, new RecordSaleBody { AmountMinor = 100 }, new RequestOptions { IdempotencyKey = "kasa3-z0187-fis0042" });
            if (satis.Card != null && satis.Card.Actions.Any(a => (a.Action == "redeem-stamps" || a.Action == "redeem-reward") && a.Ready))
                Console.WriteLine("Ödül hazır");

            await foreach (var islem in rewloy.ListPassOperationsAllAsync(seri))
            {
                if (!islem.Reversible) continue;
                if (islem.UndoWith == "sale/reverse") await rewloy.ReverseSaleAsync(seri, new ReverseSaleBody { SaleKey = islem.SaleKey });
                else await rewloy.ReverseActionAsync(seri, new ReverseActionBody { ActionKey = islem.ActionKey });
            }

            var yeni = (await rewloy.RotateWebhookSecretAsync(webhookId)).Secret;
            var olay = Webhook.Verify(hamGovde, imzaBasligi, new[] { yeni, eskiSir });
            Console.WriteLine(olay.Type);

            foreach (var w in await rewloy.ListWebhooksAsync())
            {
                if (w.PausedUntil is { } bekletme) Console.WriteLine($"{w.Url}: {bekletme:u} anına kadar bekletiliyor");
                if (w.ResumableUntil is { } saklanan) Console.WriteLine($"{w.Url}: {saklanan:u} öncesinde açın, kaldığı yerden sürer");
            }
            await rewloy.SetWebhookStatusAsync(webhookId, new SetWebhookStatusBody { Active = true });
        }
    }
}
