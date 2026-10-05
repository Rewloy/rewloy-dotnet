using System;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    /// <summary>The README's till example: the sale, the card's structured fields and the refund.</summary>
    public class ReadmeExamplesTests
    {
        private const string Pass = "{\"serial\":\"ABCD-EFGH-JKLM\",\"programId\":\"0192f7c1-0000-7000-8000-000000000002\",\"type\":\"stamp\",\"status\":\"active\",\"programName\":\"Kahve kartı\",\"currency\":\"TRY\",\"balance\":3,\"stamps\":{\"count\":3,\"max\":8},\"customer\":{\"name\":\"Ayşe\"},\"rewardReady\":false,\"rewardsReady\":0,\"updatedAt\":\"2026-10-05T10:00:00.000Z\",\"actions\":[],\"sale\":{\"writes\":\"stamps\"}}";
        private const string Sale = "{\"type\":\"stamp\",\"applied\":\"stamps\",\"credited\":1,\"balance\":4,\"duplicate\":false,\"rewardReady\":false,\"rewardsReady\":0}";
        private const string Reversal = "{\"type\":\"stamp\",\"applied\":\"stamps\",\"reversed\":1,\"balance\":3,\"duplicate\":false,\"rewardReady\":false,\"rewardsReady\":0}";

        [Fact]
        public async Task Writes_a_sale_to_a_card_and_takes_it_back()
        {
            var stub = new StubHandler().Then(Reply.Ok(Pass)).Then(Reply.Ok(Sale)).Then(Reply.Ok(Reversal));
            using var rewloy = Clients.Make(stub);
            var seri = "ABCD-EFGH-JKLM";
            var subeId = Guid.Parse("0192f7c1-0000-7000-8000-0000000000aa");
            var fisNo = 42;

            var kart = await rewloy.GetPassAsync(seri);
            Assert.Equal("3 / 8", $"{kart.Stamps!.Count} / {kart.Stamps.Max}");
            Assert.Equal("Kahve kartı TRY Ayşe", $"{kart.ProgramName} {kart.Currency} {kart.Customer?.Name}");

            var anahtar = $"kasa3-z0187-fis{fisNo}";
            var satis = await rewloy.RecordSaleAsync(
                seri,
                new RecordSaleBody { LocationId = subeId, AmountMinor = 4550, Currency = kart.Currency, Reference = $"fis-{fisNo}" },
                new RequestOptions { IdempotencyKey = anahtar });
            Assert.Equal("stamps", satis.Applied);
            Assert.Equal(1, satis.Credited);
            Assert.Equal(4, satis.Balance);

            var sale = stub.Requests[1];
            Assert.Equal("POST", sale.Method);
            Assert.Equal("/v1/passes/ABCD-EFGH-JKLM/sale", sale.Uri.AbsolutePath);
            Assert.Equal(anahtar, sale.Header("Idempotency-Key"));
            Assert.Equal(4550, sale.Json().GetProperty("amountMinor").GetInt32());
            Assert.Equal("fis-42", sale.Json().GetProperty("reference").GetString());

            var geri = await rewloy.ReverseSaleAsync(seri, new ReverseSaleBody { SaleKey = anahtar, LocationId = subeId });
            Assert.Equal(1, geri.Reversed);
            Assert.Equal(3, geri.Balance);
            Assert.Equal("/v1/passes/ABCD-EFGH-JKLM/sale/reverse", stub.Requests[2].Uri.AbsolutePath);
            Assert.Equal(anahtar, stub.Requests[2].Json().GetProperty("saleKey").GetString());
        }

        /// <summary>Compiled, not run.</summary>
        private static async Task CompileOnly(string seri)
        {
            using var rewloy = new RewloyClient(new RewloyClientOptions
            {
                ApiKey = Clients.Key,
                BaseUrl = "https://rewloy-staging.ornek.com",
            });
            var yeni = await rewloy.CreateWebhookAsync(new CreateWebhookBody
            {
                Url = "https://ornek.com/rewloy/webhook",
                Events = new[] { "pass.activity", "pass.voided" },
            });
            var sir = yeni.Secret;
            await rewloy.TestWebhookAsync(yeni.Webhook.Id);
            var yanit = await rewloy.GetPassWithResponseAsync(seri);
            Console.WriteLine($"{yanit.IsTestMode} {sir}");
            var kart = yanit.Data;
            if (kart.Points != null) Console.WriteLine($"{kart.Points} puan");
            if (kart.Money != null) Console.WriteLine($"{kart.Money.AmountMinor / 100m} {kart.Money.Currency}");
        }
    }
}
