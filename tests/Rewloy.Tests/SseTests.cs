using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    public class SseParserTests
    {
        private static List<ServerSentEvent> All(SseParser parser, params string[] pieces) =>
            pieces.SelectMany(parser.Push).ToList();

        [Fact]
        public void Parses_the_apis_own_stream()
        {
            var parser = new SseParser();
            var events = All(parser, "retry: 5000\n\n", ": hb\n\n", "event: event\ndata: {\"kind\":\"earn\",\"delta\":2}\n\n", ": hb\n\n");

            var ev = Assert.Single(events);
            Assert.Equal("event", ev.Event);
            Assert.Equal("{\"kind\":\"earn\",\"delta\":2}", ev.Data);
            Assert.Equal(5000, parser.RetryMilliseconds);
            Assert.Equal(2, ev.Json().GetProperty("delta").GetInt32());
        }

        [Fact]
        public void Takes_lf_cr_and_crlf_line_endings_wherever_a_piece_ends()
        {
            var wire = "event: a\r\ndata: 1\r\n\r\ndata: 2\rdata: 3\r\r\ndata: 4\n\n";
            var whole = All(new SseParser(), wire);
            Assert.Equal(new[] { "1", "2\n3", "4" }, whole.Select(e => e.Data));
            Assert.Equal("a", whole[0].Event);
            Assert.Equal("message", whole[1].Event);

            // The same, cut at every possible place (a CRLF may be split between two pieces).
            for (var cut = 0; cut <= wire.Length; cut++)
            {
                var pieces = All(new SseParser(), wire.Substring(0, cut), wire.Substring(cut));
                Assert.Equal(whole.Select(e => (e.Event, e.Data)), pieces.Select(e => (e.Event, e.Data)));
            }
            // And one character at a time.
            var singles = All(new SseParser(), wire.Select(c => c.ToString()).ToArray());
            Assert.Equal(whole.Select(e => (e.Event, e.Data)), singles.Select(e => (e.Event, e.Data)));
        }

        [Fact]
        public void Reads_fields_as_the_standard_says()
        {
            var parser = new SseParser();
            var events = All(parser, "﻿data:no space\n\n", "data:  two spaces\n\n", "data\n\n", "unknown: x\ndata: y\n\n", "retry: abc\nretry: 250\ndata: z\n\n", ": comment only\n\n");
            Assert.Equal(new[] { "no space", " two spaces", "", "y", "z" }, events.Select(e => e.Data));
            Assert.Equal(250, parser.RetryMilliseconds);
        }

        [Fact]
        public void Dispatches_an_event_whose_data_is_empty_but_not_one_without_data()
        {
            var events = All(new SseParser(), "event: ping\ndata:\n\n", "event: nothing\n\n");
            var ev = Assert.Single(events);
            Assert.Equal("ping", ev.Event);
            Assert.Equal(string.Empty, ev.Data);
        }

        [Fact]
        public void Starts_from_a_last_event_id_it_is_given_and_takes_the_id_at_a_blank_line_even_without_data()
        {
            var parser = new SseParser("41");
            var events = All(parser, "data: a\n\n", "id: 42\ndata: b\n\n", "id: 43\n\n", "data: c\n\n", "id: with\0null\ndata: d\n\n");
            Assert.Equal(new[] { "41", "42", "43", "43" }, events.Take(4).Select(e => e.Id));
            Assert.Equal("43", events[3].Id);
            Assert.Equal("43", parser.LastEventId);
        }

        [Fact]
        public void Drops_an_event_cut_off_at_the_end()
        {
            var parser = new SseParser();
            Assert.Empty(All(parser, "data: half"));
            parser.End();
            Assert.Empty(All(parser, "\n"));
        }
    }

    public class StreamTests
    {
        private const string Event1 = "event: event\ndata: {\"kind\":\"earn\",\"name\":\"Ayşe ğüşıöç\"}\n\n";

        private static async Task<List<ServerSentEvent>> ReadAsync(EventStream stream, int count, CancellationToken ct = default)
        {
            var list = new List<ServerSentEvent>();
            await foreach (var ev in stream.WithCancellation(ct))
            {
                list.Add(ev);
                if (list.Count == count) break;
            }
            return list;
        }

        [Fact]
        public async Task Decodes_utf8_split_across_chunks_and_lines_and_events_split_anywhere()
        {
            var bytes = Encoding.UTF8.GetBytes("retry: 5000\n\n" + Event1 + ": hb\n\nevent: event\r\ndata: {\"kind\":\"spend\"}\r\n\r\n");
            // A chunk per byte: every multi-byte character, line ending and event is split.
            var chunks = bytes.Select(b => (object)new[] { b }).ToArray();
            var stub = new StubHandler().Then(() => Reply.Sse(new ChunkStream(chunks)));
            using var client = Clients.Make(stub);

            var stream = client.LiveFeedAsync(new RequestOptions { Reconnect = false });
            var events = new List<ServerSentEvent>();
            await foreach (var ev in stream) events.Add(ev);

            Assert.Equal(2, events.Count);
            Assert.Equal("Ayşe ğüşıöç", events[0].Json().GetProperty("name").GetString());
            Assert.Equal("spend", events[1].Json().GetProperty("kind").GetString());
            Assert.Equal(TimeSpan.FromSeconds(5), stream.RetryDelay);
        }

        [Fact]
        public async Task Sends_the_credential_merchant_and_accept_and_reports_request_id_and_mode()
        {
            var stub = new StubHandler().Then(() => Reply.Sse(new ChunkStream(Event1), ("x-request-id", "req_sse"), ("Rewloy-Mode", "test")));
            using var client = Clients.Make(stub, configure: o =>
            {
                o.ApiKey = null;
                o.StaffSession = Clients.Staff;
                o.Merchant = Clients.MerchantA;
            });

            var stream = client.LiveFeedAsync(new RequestOptions { Reconnect = false });
            var events = await ReadAsync(stream, 1);

            var req = Assert.Single(stub.Requests);
            Assert.Equal("GET", req.Method);
            Assert.Equal("/v1/live", req.PathAndQuery);
            Assert.Equal("text/event-stream", req.Header("Accept"));
            Assert.Equal("no-cache", req.Header("Cache-Control"));
            Assert.Equal("identity", req.Header("Accept-Encoding"));
            Assert.Equal("Bearer " + Clients.Staff, req.Header("Authorization"));
            Assert.Equal(Clients.MerchantA.ToString(), req.Header("Rewloy-Merchant"));
            Assert.Null(req.Header("Last-Event-ID"));
            Assert.Single(events);
            Assert.Equal("req_sse", stream.RequestId);
            Assert.Equal("test", stream.Mode);
        }

        [Fact]
        public async Task Reconnects_after_the_servers_retry_delay_with_last_event_id()
        {
            var stub = new StubHandler()
                .Then(() => Reply.Sse(new ChunkStream("retry: 1500\n\nid: 7\nevent: event\ndata: 1\n\n")))
                .Then(() => Reply.Sse(new ChunkStream("id: 8\ndata: 2\n\n")));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            var events = await ReadAsync(client.LiveFeedAsync(), 2);

            Assert.Equal(new[] { "1", "2" }, events.Select(e => e.Data));
            Assert.Equal(new[] { "7", "8" }, events.Select(e => e.Id));
            Assert.Equal(2, stub.Count);
            Assert.Null(stub.Requests[0].Header("Last-Event-ID"));
            Assert.Equal("7", stub.Requests[1].Header("Last-Event-ID"));
            Assert.Equal(new[] { TimeSpan.FromMilliseconds(1500) }, sleeps.Delays);
        }

        [Fact]
        public async Task Ends_when_the_connection_ends_if_reconnect_is_off()
        {
            var stub = new StubHandler().Then(() => Reply.Sse(new ChunkStream(Event1)));
            using var client = Clients.Make(stub);
            var all = new List<ServerSentEvent>();
            await foreach (var ev in client.LiveFeedAsync(new RequestOptions { Reconnect = false })) all.Add(ev);
            Assert.Single(all);
            Assert.Equal(1, stub.Count);
        }

        [Fact]
        public async Task Ends_quietly_when_the_token_cancels_and_closes_the_connection_on_break()
        {
            var first = new ChunkStream(Event1, ChunkStream.Hang);
            var second = new ChunkStream(Event1, ChunkStream.Hang);
            var stub = new StubHandler().Then(() => Reply.Sse(first)).Then(() => Reply.Sse(second));
            using var client = Clients.Make(stub);

            // break
            var stream = client.LiveFeedAsync();
            await ReadAsync(stream, 1);
            await Task.Delay(50);
            Assert.True(first.Disposed);

            // cancellation: the iteration ends without an exception
            using var cts = new CancellationTokenSource();
            var seen = 0;
            var run = Task.Run(async () =>
            {
                await foreach (var _ in client.LiveFeedAsync(cancellationToken: cts.Token))
                {
                    seen++;
                    cts.Cancel();
                }
            });
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, seen);
            await Task.Delay(50);
            Assert.True(second.Disposed);
        }

        [Fact]
        public async Task Closes_with_close()
        {
            var body = new ChunkStream(Event1, ChunkStream.Hang);
            var stub = new StubHandler().Then(() => Reply.Sse(body));
            using var client = Clients.Make(stub);
            var stream = client.LiveFeedAsync();
            var seen = 0;
            var run = Task.Run(async () =>
            {
                await foreach (var _ in stream)
                {
                    seen++;
                    stream.Close();
                }
            });
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, seen);
        }

        [Fact]
        public async Task Can_be_iterated_once()
        {
            var stub = new StubHandler().Then(() => Reply.Sse(new ChunkStream(Event1)));
            using var client = Clients.Make(stub);
            var stream = client.LiveFeedAsync(new RequestOptions { Reconnect = false });
            await ReadAsync(stream, 1);
            Assert.Throws<InvalidOperationException>(() => stream.GetAsyncEnumerator());
        }

        [Theory]
        [InlineData(401, "UNAUTHENTICATED")]
        [InlineData(403, "FORBIDDEN")]
        [InlineData(404, "PASS_NOT_FOUND")]
        public async Task Ends_with_the_error_a_reconnection_cannot_fix(int status, string code)
        {
            var stub = new StubHandler().Then(Reply.Error(status, code));
            using var client = Clients.Make(stub);

            var ex = await Assert.ThrowsAsync<RewloyException>(async () => { await foreach (var _ in client.HolderCardEventsAsync("ABCD-EFGH-JKLM")) { } });

            Assert.Equal(code, ex.Code);
            Assert.Equal("holderCardEvents", ex.Operation);
            Assert.Equal(1, stub.Count);
        }

        [Fact]
        public async Task Reconnects_through_transient_failures()
        {
            var stub = new StubHandler()
                .Then(Reply.Raw(HttpStatusCode.BadGateway, ""))      // retried inside the connection attempt...
                .Then(Reply.Raw(HttpStatusCode.BadGateway, ""))
                .Then(Reply.Raw(HttpStatusCode.BadGateway, ""))      // ...and, when those run out, by the stream itself
                .Then(() => Reply.Sse(new ChunkStream(new IOExceptionFromRead())))
                .Then(() => Reply.Sse(new ChunkStream(Event1)));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            var events = await ReadAsync(client.LiveFeedAsync(), 1);

            Assert.Single(events);
            Assert.Equal(5, stub.Count);
            Assert.True(sleeps.Delays.Count >= 3);
            Assert.All(sleeps.Delays.Skip(2), d => Assert.True(d >= TimeSpan.FromSeconds(1)));
        }

        private sealed class IOExceptionFromRead : System.IO.IOException
        {
            public IOExceptionFromRead()
                : base("connection reset by peer")
            {
            }
        }

        [Fact]
        public async Task Treats_a_silent_connection_as_dropped()
        {
            var stub = new StubHandler().Then(() => Reply.Sse(new ChunkStream(ChunkStream.Hang)));
            using var client = Clients.Make(stub);

            var ex = await Assert.ThrowsAsync<RewloyTimeoutException>(async () =>
            {
                await foreach (var _ in client.LiveFeedAsync(new RequestOptions { Reconnect = false, IdleTimeout = TimeSpan.FromMilliseconds(100) })) { }
            });

            Assert.Equal("TIMEOUT", ex.Code);
            Assert.Equal("liveFeed", ex.Operation);
        }

        [Fact]
        public async Task Reconnects_after_a_silent_connection_when_reconnect_is_on()
        {
            var silent = new ChunkStream(ChunkStream.Hang);
            var stub = new StubHandler().Then(() => Reply.Sse(silent)).Then(() => Reply.Sse(new ChunkStream(Event1)));
            var sleeps = new Sleeps();
            using var client = Clients.Make(stub, sleeps);

            var events = await ReadAsync(client.LiveFeedAsync(new RequestOptions { IdleTimeout = TimeSpan.FromMilliseconds(300) }), 1);

            Assert.Single(events);
            Assert.Equal(2, stub.Count);
            Assert.True(silent.Disposed);
            Assert.Single(sleeps.Delays);
        }

        [Fact]
        public async Task Heartbeats_keep_a_quiet_connection_alive()
        {
            // The idle timer is reset by every chunk, comments included.
            var body = new ChunkStream(": hb\n\n", ": hb\n\n", Event1);
            var stub = new StubHandler().Then(() => Reply.Sse(body));
            using var client = Clients.Make(stub);
            var events = await ReadAsync(client.LiveFeedAsync(new RequestOptions { IdleTimeout = TimeSpan.FromSeconds(30) }), 1);
            Assert.Single(events);
        }
    }
}
