using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Rewloy.Tests.Support;

namespace Rewloy.Tests
{
    /// <summary>The warning is per process, so these tests share state: they run one at a time.</summary>
    [CollectionDefinition(nameof(DeprecationCollection), DisableParallelization = true)]
    public sealed class DeprecationCollection
    {
    }

    [Collection(nameof(DeprecationCollection))]
    public class DeprecationTests : IDisposable
    {
        private readonly List<DeprecationEventArgs> _seen = new List<DeprecationEventArgs>();
        private readonly EventHandler<DeprecationEventArgs> _handler;

        public DeprecationTests()
        {
            RewloyClient.ResetDeprecationWarnings();
            _handler = (_, e) => { lock (_seen) _seen.Add(e); };
            RewloyClient.Deprecated += _handler;
        }

        public void Dispose()
        {
            RewloyClient.Deprecated -= _handler;
            RewloyClient.ResetDeprecationWarnings();
        }

        private static System.Net.Http.HttpResponseMessage DeprecatedOk(string data = "{}", string? sunset = "Mon, 05 Apr 2027 00:00:00 GMT") =>
            Reply.Ok(data, HttpStatusCode.OK,
                new[] { ("Deprecation", "true"), ("Link", "<https://rewloy.com/gelistiriciler/degisiklikler#getPass>; rel=\"deprecation\"") }
                    .Concat(sunset == null ? Array.Empty<(string, string)>() : new[] { ("Sunset", sunset) }).ToArray());

        [Fact]
        public async Task Warns_once_per_operation_naming_the_sunset_and_the_link()
        {
            var stub = new StubHandler().Then(DeprecatedOk()).Then(DeprecatedOk()).Then(DeprecatedOk()).Then(DeprecatedOk("[]"));
            using var client = Clients.Make(stub);
            using var another = Clients.Make(stub); // whatever the number of clients

            await client.GetPassAsync("A");
            await client.GetPassAsync("A");
            await another.GetPassAsync("A");
            await client.HolderCardsAsync(); // another operation: its own warning

            var warning = _seen.First();
            Assert.Equal("getPass", warning.Operation.Id);
            Assert.Equal("Mon, 05 Apr 2027 00:00:00 GMT", warning.Sunset);
            Assert.Equal("https://rewloy.com/gelistiriciler/degisiklikler#getPass", warning.Link);
            Assert.Contains("getPass (GET /v1/passes/{serial}) is deprecated", warning.Message);
            Assert.Contains("Sunset: Mon, 05 Apr 2027 00:00:00 GMT", warning.Message);
            Assert.Equal(new[] { "getPass", "holderCards" }, _seen.Select(e => e.Operation.Id));
        }

        [Fact]
        public async Task Says_nothing_for_an_operation_that_is_not_deprecated()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}"));
            using var client = Clients.Make(stub);
            await client.GetPassAsync("A");
            Assert.Empty(_seen);
        }

        [Fact]
        public async Task Warns_on_an_error_answer_too()
        {
            var stub = new StubHandler().Then(Reply.Error(404, "PASS_NOT_FOUND", headers: new[] { ("Deprecation", "true") }));
            using var client = Clients.Make(stub);
            await Assert.ThrowsAsync<RewloyException>(() => client.GetPassAsync("A"));
            Assert.Single(_seen);
            Assert.Null(_seen[0].Sunset);
            Assert.Null(_seen[0].Link);
        }

        [Fact]
        public async Task A_subscriber_that_throws_does_not_fail_an_answered_call()
        {
            EventHandler<DeprecationEventArgs> boom = (_, _) => throw new InvalidOperationException("boom");
            RewloyClient.Deprecated += boom;
            try
            {
                var stub = new StubHandler().Then(DeprecatedOk());
                using var client = Clients.Make(stub);
                var pass = await client.GetPassAsync("A");
                Assert.NotNull(pass);
            }
            finally
            {
                RewloyClient.Deprecated -= boom;
            }
        }

        [Fact]
        public async Task Takes_the_link_marked_as_deprecation_even_when_it_is_not_the_first()
        {
            var stub = new StubHandler().Then(Reply.Ok("{}", HttpStatusCode.OK, ("Deprecation", "true"),
                ("Link", "<https://example.test/other>; rel=\"alternate\", <https://rewloy.com/gelistiriciler/degisiklikler#x>; rel=\"deprecation\"")));
            using var client = Clients.Make(stub);
            await client.GetPassAsync("A");
            Assert.Equal("https://rewloy.com/gelistiriciler/degisiklikler#x", _seen.Single().Link);
        }
    }
}
