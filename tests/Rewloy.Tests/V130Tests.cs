using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    /// <summary>The operations, fields and errors Rewloy API 1.3.0 added.</summary>
    public class V130Tests
    {
        private const string Serial = "ABCD-EFGH-JKLM";
        private static readonly Guid Program = Guid.Parse("0192f7c1-0000-7000-8000-0000000000a1");
        private static readonly Guid Group = Guid.Parse("0192f7c1-0000-7000-8000-0000000000a2");
        private static readonly Guid Location = Guid.Parse("0192f7c1-0000-7000-8000-000000000003");

        [Fact]
        public void Knows_the_new_operations_and_the_api_version()
        {
            Assert.Equal("1.3.0", RewloyOperations.ApiVersion);
            Assert.Equal(298, RewloyOperations.All.Count);
            foreach (var id in new[]
            {
                "listEarnGroups", "createEarnGroup", "getEarnGroup", "updateEarnGroup", "deleteEarnGroup", "listSeenLines", "listEarnSources",
                "ignoreSeenLine", "unignoreSeenLine", "listEarnTemplates", "getEarnRules", "putEarnRules", "updateEarnRule", "createEarnRule",
                "deleteEarnRule", "deleteEarnRules", "listEarnRuleRevisions", "previewEarn", "previewSale", "copyProgram", "extendProgramCards",
                "updateBatch", "freezeLocation", "unfreezeLocation", "updateLocationFreeze", "cancelLocationFreeze", "listLocationFreezes",
                "locationQrSvg", "locationQrPng", "locationQrSheetPdf", "locationQrSheetSvg", "previewLocationQr", "getLocationQrItems",
                "putLocationQrItems", "addQrItems", "publicBranch", "holderBranch", "joinHolderBranch",
            })
                Assert.True(RewloyOperations.All.ContainsKey(id), id);
        }

        [Fact]
        public async Task Records_a_sale_with_receipt_lines_and_reads_the_earn_explanation()
        {
            const string answer = "{\"type\":\"stamp\",\"applied\":\"stamps\",\"credited\":2,\"balance\":2,\"duplicate\":false,\"reversed\":false,\"rewardReady\":false,\"rewardsReady\":0,"
                + "\"earn\":{\"source\":\"rules\",\"revision\":3,\"unit\":\"stamps\","
                + "\"lines\":[{\"lineId\":\"1\",\"status\":\"earned\",\"groups\":[\"" + "0192f7c1-0000-7000-8000-0000000000a2" + "\"],\"rules\":[\"r1\"],\"earned\":2},{\"lineId\":\"2\",\"status\":\"no_rule\",\"earned\":0}],"
                + "\"rules\":[{\"ruleId\":\"r1\",\"kind\":\"stamp.perUnit\",\"units\":2,\"lines\":[\"1\"],\"text\":\"Kahve başına 1 damga\"}],"
                + "\"total\":{\"beforeRounding\":\"2\",\"rounded\":2,\"caps\":[],\"credited\":2}}}";
            var stub = new StubHandler().Then(Reply.Ok(answer));
            using var client = Clients.Make(stub);
            var sale = await client.RecordSaleAsync(Serial, new RecordSaleBody
            {
                LocationId = Location,
                AmountMinor = 16000,
                ReceiptDiscountMinor = 0,
                Lines = new[]
                {
                    new RecordSaleBodyLinesItem { LineId = "1", Name = "Filtre kahve", Sku = "KAHVE", Quantity = JsonSerializer.SerializeToElement(2), UnitPriceMinor = 6000, Category = JsonSerializer.SerializeToElement(new[] { "İçecek", "Sıcak" }), Kind = "item", Tags = new[] { "kampanyali" } },
                    new RecordSaleBodyLinesItem { Name = "Kargo", UnitPriceMinor = 4000, Kind = "shipping" },
                },
            }, new RequestOptions { IdempotencyKey = "fis-0001" });

            var body = stub.Requests[0].Json();
            var lines = body.GetProperty("lines");
            Assert.Equal(2, lines.GetArrayLength());
            Assert.Equal("KAHVE", lines[0].GetProperty("sku").GetString());
            Assert.Equal(2, lines[0].GetProperty("quantity").GetInt32());
            Assert.Equal("Sıcak", lines[0].GetProperty("category")[1].GetString());
            Assert.Equal("shipping", lines[1].GetProperty("kind").GetString());
            Assert.False(lines[1].TryGetProperty("lineId", out _)); // not sent when not set
            Assert.Equal(0, body.GetProperty("receiptDiscountMinor").GetInt32());

            var earn = sale.Earn!;
            Assert.Equal("rules", earn.Source);
            Assert.Equal(3, earn.Revision);
            Assert.Equal("stamps", earn.Unit);
            Assert.Equal(Group, earn.Lines[0].Groups![0]);
            Assert.Equal(2, earn.Lines[0].Earned);
            Assert.Equal("no_rule", earn.Lines[1].Status);
            Assert.Equal("stamp.perUnit", earn.Rules[0].Kind);
            Assert.Equal("Kahve başına 1 damga", earn.Rules[0].Text);
            Assert.Equal(2, earn.Total.Credited);
            Assert.Empty(earn.Total.Caps);
        }

        [Fact]
        public async Task Previews_a_sale_and_an_earn_with_a_draft_rule_set()
        {
            var stub = new StubHandler()
                .Then(Reply.Ok("{\"type\":\"stamp\",\"applied\":\"stamps\",\"credited\":1,\"balance\":4,\"duplicate\":false,\"reversed\":false,\"rewardReady\":true,\"rewardsReady\":1,\"preview\":true}"))
                .Then(Reply.Ok("{\"credited\":3,\"unit\":\"stamps\",\"earn\":{\"source\":\"draft\",\"revision\":null,\"unit\":\"stamps\",\"lines\":[],\"rules\":[],\"total\":{\"beforeRounding\":\"3\",\"rounded\":3,\"caps\":[],\"credited\":3}}}"));
            using var client = Clients.Make(stub);

            var preview = await client.PreviewSaleAsync(Serial, new PreviewSaleBody { AmountMinor = 5000 });
            Assert.True(preview.Preview);
            Assert.True(preview.RewardReady);
            Assert.Equal("POST", stub.Requests[0].Method);
            Assert.Equal("/v1/passes/ABCD-EFGH-JKLM/sale/preview", stub.Requests[0].Uri.AbsolutePath);
            Assert.Null(stub.Requests[0].Header("Idempotency-Key"));

            var earn = await client.PreviewEarnAsync(Program, new PreviewEarnBody
            {
                AmountMinor = 12000,
                Context = new PreviewEarnBodyContext { EarnedToday = 1, Balance = 4 },
                RuleSet = new PreviewEarnBodyRuleSet { Rules = new[] { new PreviewEarnBodyRuleSetRulesItem { Kind = "stamp.perReceipt", Stamps = 3 } } },
            });
            Assert.Equal(3, earn.Credited);
            Assert.Null(earn.Earn.Revision);
            Assert.Equal("/v1/programs/" + Program + "/earn-rules/preview", stub.Requests[1].Uri.AbsolutePath);
            var sent = stub.Requests[1].Json();
            Assert.Equal("stamp.perReceipt", sent.GetProperty("ruleSet").GetProperty("rules")[0].GetProperty("kind").GetString());
            Assert.Equal(4, sent.GetProperty("context").GetProperty("balance").GetInt32());
        }

        [Fact]
        public async Task Takes_back_lines_of_a_sale_and_reads_what_is_left()
        {
            var stub = new StubHandler().Then(Reply.Ok("{\"type\":\"stamp\",\"applied\":\"stamps\",\"reversed\":1,\"balance\":1,\"duplicate\":false,\"rewardReady\":false,\"rewardsReady\":0,"
                + "\"linesLeft\":[{\"lineId\":\"1\",\"quantity\":1,\"amountMinor\":6000}]}"));
            using var client = Clients.Make(stub);
            var r = await client.ReverseSaleAsync(Serial, new ReverseSaleBody
            {
                SaleKey = "fis-0001",
                Lines = new[] { new ReverseSaleBodyLinesItem { LineId = "1", Quantity = JsonSerializer.SerializeToElement(1) } },
            }, new RequestOptions { IdempotencyKey = "iade-0001" });
            Assert.Equal(1, r.Reversed);
            Assert.Single(r.LinesLeft!);
            Assert.Equal("1", r.LinesLeft![0].LineId);
            Assert.Equal(6000, r.LinesLeft[0].AmountMinor);
            var body = stub.Requests[0].Json();
            Assert.Equal("1", body.GetProperty("lines")[0].GetProperty("lineId").GetString());
            Assert.Equal("iade-0001", stub.Requests[0].Header("Idempotency-Key"));
        }

        [Fact]
        public async Task Manages_earn_groups_and_rules()
        {
            var stub = new StubHandler()
                .Then(Reply.Ok("{\"id\":\"" + Group + "\",\"name\":\"Kahveler\",\"members\":[{\"id\":\"0192f7c1-0000-7000-8000-0000000000a3\",\"effect\":\"include\",\"match\":\"sku\",\"value\":\"KAHVE\"}],\"lines30d\":0,\"usedBy\":[],\"warnings\":[],\"createdAt\":\"2026-10-07T09:00:00.000Z\",\"updatedAt\":\"2026-10-07T09:00:00.000Z\"}", HttpStatusCode.Created))
                .Then(Reply.Ok("{\"programId\":\"" + Program + "\",\"active\":true,\"revision\":1,\"rules\":[],\"warnings\":[],\"settings\":{}}"))
                .Then(Reply.Ok("{\"programId\":\"" + Program + "\",\"active\":true,\"revision\":2,\"rules\":[],\"warnings\":[],\"settings\":{}}"))
                .Then(Reply.NoContent());
            using var client = Clients.Make(stub);

            var group = await client.CreateEarnGroupAsync(new CreateEarnGroupBody { Name = "Kahveler", Members = new[] { new CreateEarnGroupBodyMembersItem { Effect = "include", Match = "sku", Value = "KAHVE" } } });
            Assert.Equal(Group, group.Id);
            Assert.Equal("sku", group.Members[0].Match);
            Assert.Equal("/v1/earn-groups", stub.Requests[0].Uri.AbsolutePath);

            var put = await client.PutEarnRulesAsync(Program, new PutEarnRulesBody
            {
                Revision = 0,
                Settings = new PutEarnRulesBodySettings { NoLines = "none", DailyCap = Optional<int>.Null },
                Rules = new[] { new PutEarnRulesBodyRulesItem { Kind = "stamp.perUnit", GroupId = Group, Stamps = 1 } },
            });
            Assert.Equal(1, put.Revision);
            Assert.Equal("PUT", stub.Requests[1].Method);
            var sent = stub.Requests[1].Json();
            Assert.Equal(0, sent.GetProperty("revision").GetInt32());
            Assert.Equal(JsonValueKind.Null, sent.GetProperty("settings").GetProperty("dailyCap").ValueKind);
            Assert.Equal(Group.ToString(), sent.GetProperty("rules")[0].GetProperty("groupId").GetString());

            var got = await client.GetEarnRulesAsync(Program);
            Assert.Equal(2, got.Revision);
            Assert.Equal("GET", stub.Requests[2].Method);

            await client.DeleteEarnRulesAsync(Program);
            Assert.Equal("DELETE", stub.Requests[3].Method);
            Assert.Equal("/v1/programs/" + Program + "/earn-rules", stub.Requests[3].Uri.AbsolutePath);
        }

        [Fact]
        public async Task Reads_a_branch_page_and_downloads_the_branch_qr()
        {
            var stub = new StubHandler()
                .Then(Reply.Ok("{\"code\":\"abc123def456\",\"url\":\"https://rewloy.com/s/abc123def456\",\"business\":{\"name\":\"Kafe\",\"slug\":\"kafe\"},\"branch\":{\"name\":\"Kadıköy\",\"address\":null,\"state\":\"frozen\",\"reopensOn\":\"2026-11-01\",\"publicNote\":\"Tadilat\"},\"featured\":null,\"items\":[],\"otherBranches\":[],\"test\":false}"))
                .Then(Reply.Raw(HttpStatusCode.OK, "<svg/>", "image/svg+xml"))
                .Then(Reply.Raw(HttpStatusCode.OK, "PNG", "image/png"))
                .Then(Reply.Raw(HttpStatusCode.OK, "%PDF-1.7", "application/pdf"));
            using var client = Clients.Make(stub);

            var page = await client.PublicBranchAsync("abc123def456");
            Assert.Equal("frozen", page.Branch.State);
            Assert.Equal("2026-11-01", page.Branch.ReopensOn);
            Assert.Equal("/v1/public/branches/abc123def456", stub.Requests[0].Uri.AbsolutePath);

            var svg = await client.LocationQrSvgAsync(Location);
            Assert.StartsWith("image/svg+xml", svg.ContentType);
            Assert.Equal("/v1/locations/" + Location + "/qr.svg", stub.Requests[1].Uri.AbsolutePath);
            var png = await client.LocationQrPngAsync(Location, new LocationQrPngQuery { Size = 1024 });
            Assert.StartsWith("image/png", png.ContentType);
            Assert.Contains("size=1024", stub.Requests[2].Uri.Query);
            var pdf = await client.LocationQrSheetPdfAsync(Location, new LocationQrSheetPdfQuery { Form = "a4" });
            Assert.StartsWith("application/pdf", pdf.ContentType);
            Assert.Equal("/v1/locations/" + Location + "/qr/sheet.pdf", stub.Requests[3].Uri.AbsolutePath);
        }

        [Fact]
        public async Task Freezes_a_branch_and_surfaces_the_frozen_errors()
        {
            var stub = new StubHandler()
                .Then(Reply.Error(409, "ALREADY_FROZEN", "Şube zaten donuk"))
                .Then(Reply.Error(409, "LOCATION_FROZEN", "Şube donuk"))
                .Then(Reply.Error(409, "BUSINESS_FROZEN", "İşletme duraklatıldı"))
                .Then(Reply.Error(422, "NOT_AN_INSTRUMENT", "Sadakat kartı kopyalanmaz"));
            using var client = Clients.Make(stub);

            var frozen = await Assert.ThrowsAsync<RewloyException>(() => client.FreezeLocationAsync(Location, new FreezeLocationBody { Reason = "renovation", ReopensOn = "2026-11-01", ExtendCards = true, Password = "x" }));
            Assert.Equal(ErrorCode.AlreadyFrozen, frozen.Code);
            var body = stub.Requests[0].Json();
            Assert.Equal("renovation", body.GetProperty("reason").GetString());
            Assert.Equal("2026-11-01", body.GetProperty("reopensOn").GetString());
            Assert.True(body.GetProperty("extendCards").GetBoolean());
            Assert.False(body.TryGetProperty("startsOn", out _));

            var sale = await Assert.ThrowsAsync<RewloyException>(() => client.RecordSaleAsync(Serial, new RecordSaleBody { LocationId = Location, AmountMinor = 100 }, new RequestOptions { IdempotencyKey = "fis-0002" }));
            Assert.Equal(ErrorCode.LocationFrozen, sale.Code);
            Assert.Equal(409, sale.Status);
            var online = await Assert.ThrowsAsync<RewloyException>(() => client.PreviewSaleAsync(Serial, new PreviewSaleBody { AmountMinor = 100 }));
            Assert.Equal(ErrorCode.BusinessFrozen, online.Code);
            var copy = await Assert.ThrowsAsync<RewloyException>(() => client.CopyProgramAsync(Program, new CopyProgramBody { Name = "Kopya" }));
            Assert.Equal(ErrorCode.NotAnInstrument, copy.Code);
            Assert.Equal(422, copy.Status);
        }

        [Fact]
        public void Has_the_new_error_codes()
        {
            Assert.Equal("LOCATION_FROZEN", ErrorCode.LocationFrozen);
            Assert.Equal("BUSINESS_FROZEN", ErrorCode.BusinessFrozen);
            Assert.Equal("NOT_AN_INSTRUMENT", ErrorCode.NotAnInstrument);
            Assert.Equal("BRANCH_NOT_FOUND", ErrorCode.BranchNotFound);
            Assert.Equal("BRANCH_GONE", ErrorCode.BranchGone);
            Assert.Equal("REVISION_CONFLICT", ErrorCode.RevisionConflict);
            Assert.Equal("RULE_KIND_NOT_FOR_TYPE", ErrorCode.RuleKindNotForType);
            Assert.Equal("TOO_MANY_LINES", ErrorCode.TooManyLines);
            Assert.Equal("FREEZE_LIMIT", ErrorCode.FreezeLimit);
        }

        [Fact]
        public async Task Keeps_the_0_2_4_call_of_programJoinQr_compiling()
        {
            var stub = new StubHandler().Then(Reply.Raw(HttpStatusCode.OK, "<svg/>", "image/svg+xml")).Then(Reply.Raw(HttpStatusCode.OK, "<svg/>", "image/svg+xml")).Then(Reply.Raw(HttpStatusCode.OK, "<svg/>", "image/svg+xml"));
            using var client = Clients.Make(stub);
            await client.ProgramJoinQrAsync(Program);                                                   // new: no arguments after the id
            await client.ProgramJoinQrAsync(Program, new RequestOptions());                             // 0.2.4's positional options
            await client.ProgramJoinQrAsync(Program, new ProgramJoinQrQuery { BranchCode = "abc123def456", Format = "png" });
            Assert.Equal("", stub.Requests[0].Uri.Query);
            Assert.Equal("", stub.Requests[1].Uri.Query);
            Assert.Contains("branchCode=abc123def456", stub.Requests[2].Uri.Query);
            Assert.Contains("format=png", stub.Requests[2].Uri.Query);
        }

        [Fact]
        public async Task Reads_environment_from_meta_and_the_extended_count()
        {
            var stub = new StubHandler()
                .Then(Reply.Ok("{\"version\":\"1.3.0\",\"apiVersion\":\"v1\",\"environment\":\"dev\"}"))
                .Then(Reply.Ok("{\"extended\":14,\"until\":\"2027-01-01T00:00:00.000Z\"}"));
            using var client = Clients.Make(stub);
            var meta = await client.GetMetaAsync();
            Assert.Equal("dev", meta.Environment);
            var extended = await client.ExtendProgramCardsAsync(Program, new ExtendProgramCardsBody { Days = 30 });
            Assert.Equal(14, extended.Extended);
        }

        [Fact]
        public void Reads_the_new_webhook_events()
        {
            const string secret = "whsec_test";
            var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
            string Deliver(string body) => Webhook.Sign(body, secret, now);

            const string extended = "{\"id\":\"0192f7c1-0000-7000-8000-0000000000e1\",\"type\":\"pass.extended\",\"created_at\":\"2026-10-07T12:00:00.000Z\",\"data\":{\"kind\":\"expiry_extended\",\"card\":\"ABCD-EFGH-JKLM\",\"reason\":\"branch_frozen\",\"from\":\"2026-12-31T21:00:00.000Z\",\"to\":\"2027-01-14T21:00:00.000Z\"}}";
            var ev = Webhook.Verify(extended, Deliver(extended), secret, now: now);
            Assert.Equal("pass.extended", ev.Type);
            Assert.Equal("expiry_extended", ev.PassData!.Kind);
            Assert.Equal("branch_frozen", ev.PassData.Reason);
            Assert.Equal("2027-01-14T21:00:00.000Z", ev.PassData.To);
            Assert.Null(ev.LocationData);

            const string frozen = "{\"id\":\"0192f7c1-0000-7000-8000-0000000000e2\",\"type\":\"location.frozen\",\"created_at\":\"2026-10-07T12:00:00.000Z\",\"data\":{\"kind\":\"location_frozen\",\"card\":null,\"customer_id\":null,\"location_id\":\"0192f7c1-0000-7000-8000-000000000003\",\"startsOn\":\"2026-10-07\",\"reopensOn\":\"2026-11-01\"}}";
            var f = Webhook.Verify(frozen, Deliver(frozen), secret, now: now);
            Assert.Null(f.PassData);
            Assert.Equal("location_frozen", f.LocationData!.Kind);
            Assert.Equal("2026-11-01", f.LocationData.ReopensOn);
            Assert.Equal("0192f7c1-0000-7000-8000-000000000003", f.LocationData.LocationId);
        }

        /// <summary>The README's 1.3.0 snippets: compiled, not run.</summary>
        private static async Task CompileOnly(RewloyClient rewloy, string seri, Guid programId, Guid subeId, string anahtar)
        {
            var grup = await rewloy.CreateEarnGroupAsync(new CreateEarnGroupBody
            {
                Name = "Kahveler",
                Members = new[] { new CreateEarnGroupBodyMembersItem { Effect = "include", Match = "sku", Value = "KAHVE" } },
            });
            var kurallar = await rewloy.GetEarnRulesAsync(programId);
            await rewloy.PutEarnRulesAsync(programId, new PutEarnRulesBody
            {
                Revision = (int)kurallar.Revision,
                Rules = new[] { new PutEarnRulesBodyRulesItem { Kind = "stamp.perUnit", GroupId = grup.Id, Stamps = 1 } },
            });
            var fis = await rewloy.RecordSaleAsync(
                seri,
                new RecordSaleBody
                {
                    LocationId = subeId,
                    AmountMinor = 16000,
                    Lines = new[]
                    {
                        new RecordSaleBodyLinesItem { LineId = "1", Name = "Filtre kahve", Sku = "KAHVE", Quantity = JsonSerializer.SerializeToElement(2), UnitPriceMinor = 6000 },
                        new RecordSaleBodyLinesItem { LineId = "2", Name = "Kek", UnitPriceMinor = 4000 },
                    },
                },
                new RequestOptions { IdempotencyKey = anahtar });
            foreach (var satir in fis.Earn!.Lines) Console.WriteLine($"{satir.LineId}: {satir.Status}, {satir.Earned} {fis.Earn.Unit}");
            foreach (var kural in fis.Earn.Rules) Console.WriteLine(kural.Text);
            var iade = await rewloy.ReverseSaleAsync(
                seri,
                new ReverseSaleBody { SaleKey = anahtar, Lines = new[] { new ReverseSaleBodyLinesItem { LineId = "1", Quantity = JsonSerializer.SerializeToElement(1) } } },
                new RequestOptions { IdempotencyKey = $"{anahtar}-iade-1" });
            Console.WriteLine($"{iade.Reversed} geri alındı; kalan satırlar: {iade.LinesLeft!.Count}");

            var sube = await rewloy.GetLocationAsync(subeId);
            Console.WriteLine($"{sube.Qr.Url} ({sube.Qr.State})");
            using var herkes = new RewloyClient(new RewloyClientOptions());
            var sayfa = await herkes.PublicBranchAsync(sube.Qr.Code);
            Console.WriteLine($"{sayfa.Business.Name} · {sayfa.Branch.Name}: {sayfa.Branch.State}, {sayfa.Items.Count} kart");
            RewloyFile png = await rewloy.LocationQrPngAsync(subeId, new LocationQrPngQuery { Size = 1024 });
            File.WriteAllBytes("sube-qr.png", png.Content);
            RewloyFile afis = await rewloy.LocationQrSheetPdfAsync(subeId);
            Console.WriteLine(afis.Content.Length);
            await rewloy.ProgramJoinQrAsync(programId, new ProgramJoinQrQuery { BranchCode = sube.Qr.Code, Format = "png" });
        }
    }
}
