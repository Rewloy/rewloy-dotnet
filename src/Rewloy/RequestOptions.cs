using System;
using System.Collections.Generic;

namespace Rewloy
{
    /// <summary>Options for one call; every method takes them as its last argument before the cancellation token.</summary>
    public sealed class RequestOptions
    {
        /// <summary>
        /// The <c>Idempotency-Key</c> of an operation that takes one (a till action, a campaign, issuing a card):
        /// 8-64 printable ASCII characters (0x21-0x7E), otherwise the call throws an <see cref="ArgumentException"/>
        /// before anything is sent. Where the API requires the key (<c>RecordSaleAsync</c>, <c>PassActionAsync</c>,
        /// <c>SendCampaignAsync</c>, <c>RefundShopRedemptionAsync</c>) it is required here too: left out, the call
        /// throws an <see cref="ArgumentException"/> before sending, and the client never makes one up, because a
        /// generated key would not survive a restart of your program. Where it is optional (<c>IssuePassAsync</c>…)
        /// and left out, the client generates a UUID and sends the same one on every retry of the call. At the till
        /// use your own, such as register + Z number + receipt number: the same receipt is then never processed
        /// twice, even after the program restarts.
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>
        /// The business a staff session acts for (<c>Rewloy-Merchant</c>), when the person has seats in several.
        /// Defaults to the client's <see cref="RewloyClientOptions.Merchant"/>.
        /// </summary>
        public Guid? Merchant { get; set; }

        /// <summary>
        /// Time allowed for one attempt, until the whole answer has arrived (for a stream: until its headers
        /// have). <see cref="TimeSpan.Zero"/> or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for none.
        /// Overrides the client's.
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>Retries after the first attempt, when retrying is safe. Overrides the client's.</summary>
        public int? MaxRetries { get; set; }

        /// <summary>
        /// Streams only: reconnect when the connection drops or the server ends the stream, as a browser's
        /// <c>EventSource</c> does. Errors a reconnection cannot fix (401, 403, 404) end the stream with a
        /// <see cref="RewloyException"/>. Default <c>true</c>.
        /// </summary>
        public bool? Reconnect { get; set; }

        /// <summary>
        /// Streams only: treat the connection as dead after this long without a byte (the API sends a heartbeat
        /// every 25 seconds). <see cref="TimeSpan.Zero"/> turns the check off. Default 60 seconds.
        /// </summary>
        public TimeSpan? IdleTimeout { get; set; }

        /// <summary>Extra request headers. They are sent as given, after the client's own, and can replace them.</summary>
        public IDictionary<string, string>? Headers { get; set; }
    }
}
