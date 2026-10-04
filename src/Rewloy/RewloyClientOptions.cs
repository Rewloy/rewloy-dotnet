using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Rewloy
{
    /// <summary>
    /// How to build a <see cref="RewloyClient"/>: with one credential, or none for
    /// the endpoints that need none (sign-in, joining a programme...).
    /// </summary>
    public sealed class RewloyClientOptions
    {
        /// <summary>An API key, <c>rwk_…</c>: a till, a shop, your own system.</summary>
        public string? ApiKey { get; set; }

        /// <summary>A staff session, <c>rws_…</c> (from <c>login</c>): a person's business app.</summary>
        public string? StaffSession { get; set; }

        /// <summary>A card holder's session, <c>rwh_…</c>: a Rewloy Cüzdan app.</summary>
        public string? HolderSession { get; set; }

        /// <summary>The business a staff session acts for (<c>Rewloy-Merchant</c>), when the person has seats in several. Goes with <see cref="StaffSession"/> only.</summary>
        public Guid? Merchant { get; set; }

        /// <summary>The API's origin, without <c>/v1</c>. Default <c>https://app.rewloy.com</c>.</summary>
        public string? BaseUrl { get; set; }

        /// <summary>
        /// Time allowed for one attempt, in all: <see cref="TimeSpan.Zero"/> or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
        /// for none. Default 60 seconds. Applied by the client itself; an <see cref="HttpClient"/> you pass keeps
        /// its own <see cref="HttpClient.Timeout"/> (100 seconds unless you changed it) on top.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Retries after a failed attempt, when retrying is safe. Default 2.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// The <see cref="System.Net.Http.HttpClient"/> to use (from <c>IHttpClientFactory</c>, with a proxy, a
        /// handler for tests...). The client does not dispose it. When left out the client makes its own, which
        /// follows no redirects (the API does not redirect, and a redirect could carry the token elsewhere),
        /// and disposes it with itself.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>Added to the <c>User-Agent</c> this client sends, e.g. <c>"KasaPOS/4.2"</c>.</summary>
        public string? UserAgent { get; set; }

        /// <summary>Replaces the wait between retries and reconnections (tests, custom schedulers).</summary>
        public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }
    }
}
