using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rewloy.Models;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    public class PaginateTests
    {
        private static string Customers(params int[] numbers) =>
            "[" + string.Join(",", numbers.Select(n => "{\"personId\":\"0192f7c1-0000-7000-8000-0000000000" + n.ToString("00") + "\",\"displayName\":\"Müşteri " + n + "\"}")) + "]";

        private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source, CancellationToken ct = default)
        {
            var list = new List<T>();
            await foreach (var item in source.WithCancellation(ct)) list.Add(item);
            return list;
        }

        [Fact]
        public async Task Walks_every_page_and_stops_at_the_total()
        {
            var stub = new StubHandler()
                .Then(Reply.Page(Customers(1, 2), 1, 2, 5))
                .Then(Reply.Page(Customers(3, 4), 2, 2, 5))
                .Then(Reply.Page(Customers(5), 3, 2, 5));
            using var client = Clients.Make(stub);

            var all = await ToListAsync(client.ListCustomersAllAsync(new ListCustomersQuery { Consent = "yes", Limit = 2 }));

            Assert.Equal(new[] { "Müşteri 1", "Müşteri 2", "Müşteri 3", "Müşteri 4", "Müşteri 5" }, all.Select(c => c.DisplayName));
            Assert.Equal(new[] { 1, 2, 3 }, stub.Requests.Select(r => int.Parse(System.Text.RegularExpressions.Regex.Match(r.Uri.Query, "page=(\\d+)").Groups[1].Value)));
            Assert.All(stub.Requests, r => Assert.Contains("consent=yes", r.Uri.Query));
            Assert.All(stub.Requests, r => Assert.Contains("limit=2", r.Uri.Query));
        }

        [Fact]
        public async Task Does_not_ask_past_a_full_last_page()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(1, 2), 1, 2, 4)).Then(Reply.Page(Customers(3, 4), 2, 2, 4));
            using var client = Clients.Make(stub);
            var all = await ToListAsync(client.ListCustomersAllAsync());
            Assert.Equal(4, all.Count);
            Assert.Equal(2, stub.Count);
        }

        [Fact]
        public async Task Starts_at_the_page_given_and_handles_an_empty_list()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(3, 4), 2, 2, 6)).Then(Reply.Page(Customers(5, 6), 3, 2, 6)).Then(Reply.Page("[]", 1, 20, 0));
            using var client = Clients.Make(stub);

            var tail = await ToListAsync(client.ListCustomersAllAsync(new ListCustomersQuery { Page = 2, Limit = 2 }));
            Assert.Equal(4, tail.Count);
            Assert.Contains("page=2", stub.Requests[0].Uri.Query);

            Assert.Empty(await ToListAsync(client.ListCustomersAllAsync()));
        }

        [Fact]
        public async Task Does_not_change_the_query_it_was_given()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(1), 1, 1, 2)).Then(Reply.Page(Customers(2), 2, 1, 2));
            using var client = Clients.Make(stub);
            var query = new ListCustomersQuery { Limit = 1 };
            await ToListAsync(client.ListCustomersAllAsync(query));
            Assert.Null(query.Page);
        }

        [Fact]
        public async Task Stops_asking_when_the_caller_stops_reading()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(1, 2), 1, 2, 10)).Then(Reply.Page(Customers(3, 4), 2, 2, 10));
            using var client = Clients.Make(stub);

            var seen = 0;
            await foreach (var _ in client.ListCustomersAllAsync(new ListCustomersQuery { Limit = 2 }))
            {
                if (++seen == 2) break;
            }

            Assert.Equal(1, stub.Count);
        }

        [Fact]
        public async Task Stops_when_the_token_is_cancelled()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(1, 2), 1, 2, 10)).Then(Reply.Page(Customers(3, 4), 2, 2, 10));
            using var client = Clients.Make(stub);
            using var cts = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in client.ListCustomersAllAsync(new ListCustomersQuery { Limit = 2 }, cancellationToken: cts.Token))
                {
                    cts.Cancel();
                }
            });
        }

        [Fact]
        public async Task Walks_a_paged_list_that_has_path_parameters()
        {
            var stub = new StubHandler()
                .Then(Reply.Page("[{\"kind\":\"earn\"}]", 1, 1, 2))
                .Then(Reply.Page("[{\"kind\":\"redeem\"}]", 2, 1, 2));
            using var client = Clients.Make(stub);
            var id = Guid.Parse("0192f7c1-0000-7000-8000-000000000004");

            var events = await ToListAsync(client.CustomerTimelineAllAsync(id, new CustomerTimelineQuery { Limit = 1 }));

            Assert.Equal(2, events.Count);
            Assert.All(stub.Requests, r => Assert.StartsWith("/v1/customers/0192f7c1-0000-7000-8000-000000000004/timeline", r.PathAndQuery));
        }

        [Fact]
        public async Task Gives_a_page_with_its_meta_through_the_method_itself()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(1, 2), 3, 2, 7));
            using var client = Clients.Make(stub);

            var page = await client.ListCustomersAsync(new ListCustomersQuery { Page = 3, Limit = 2 });

            Assert.Equal(2, page.Data.Count);
            Assert.Equal(3, page.Meta.Page);
            Assert.Equal(2, page.Meta.PageSize);
            Assert.Equal(7, page.Meta.Total);
        }

        [Fact]
        public async Task Surfaces_a_failure_on_a_later_page_as_a_rewloy_exception()
        {
            var stub = new StubHandler().Then(Reply.Page(Customers(1, 2), 1, 2, 4)).Then(Reply.Error(403, "FORBIDDEN"));
            using var client = Clients.Make(stub);
            var got = new List<string>();
            var ex = await Assert.ThrowsAsync<RewloyException>(async () =>
            {
                await foreach (var c in client.ListCustomersAllAsync(new ListCustomersQuery { Limit = 2 })) got.Add(c.DisplayName);
            });
            Assert.Equal(2, got.Count);
            Assert.Equal("FORBIDDEN", ex.Code);
        }
    }
}
