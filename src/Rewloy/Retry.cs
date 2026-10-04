using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rewloy
{
    /// <summary>
    /// The client's retry rules, apart so that tests can pin them.
    /// Retried, when the request is safe to repeat (GET, HEAD, PUT, DELETE, or
    /// a POST with an <c>Idempotency-Key</c>): network errors and timeouts,
    /// 429, 502-504, Cloudflare's 520-524, and <c>409 IDEMPOTENCY_IN_PROGRESS</c>.
    /// </summary>
    internal static class Retry
    {
        /// <summary>The first wait's ceiling; it doubles with each attempt.</summary>
        public static readonly TimeSpan BackoffBase = TimeSpan.FromMilliseconds(500);

        /// <summary>The longest backoff.</summary>
        public static readonly TimeSpan BackoffMax = TimeSpan.FromSeconds(8);

        /// <summary>A <c>Retry-After</c> longer than this is not waited for: the error goes to the caller.</summary>
        public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

        private static readonly Random SharedRandom = new Random();
        private static readonly Regex Seconds = new Regex(@"^\d+(\.\d+)?$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The wait before the retry after attempt <paramref name="attempt"/> (0-based): 0.5 s, 1 s, 2 s...
        /// up to 8 s, each with jitter between half and all of it.
        /// </summary>
        /// <param name="attempt">The attempt that just failed, counted from 0.</param>
        /// <param name="random">A number in [0, 1]; random when <c>null</c>.</param>
        public static TimeSpan Backoff(int attempt, double? random = null)
        {
            var capMs = Math.Min(BackoffMax.TotalMilliseconds, BackoffBase.TotalMilliseconds * Math.Pow(2, Math.Min(Math.Max(0, attempt), 16)));
            double r;
            if (random.HasValue) r = random.Value;
            else lock (SharedRandom) r = SharedRandom.NextDouble();
            return TimeSpan.FromMilliseconds(Math.Round(capMs / 2 + r * capMs / 2));
        }

        /// <summary><c>Retry-After</c>: delta-seconds or an HTTP date; <c>null</c> when absent or unreadable.</summary>
        public static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset? now = null)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (Seconds.IsMatch(v!)) return TimeSpan.FromMilliseconds(Math.Round(double.Parse(v!, CultureInfo.InvariantCulture) * 1000));
            if (!DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) return null;
            var wait = at - (now ?? DateTimeOffset.UtcNow);
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        public static bool IsGatewayStatus(int status) => status == 502 || status == 503 || status == 504 || (status >= 520 && status <= 524);

        /// <summary>An error answer that another attempt may get past.</summary>
        public static bool IsRetryableStatus(int status, string errorCode) =>
            status == 429 || IsGatewayStatus(status) || (status == 409 && errorCode == "IDEMPOTENCY_IN_PROGRESS");

        public static bool IsSafeMethod(string method) => method == "GET" || method == "HEAD" || method == "PUT" || method == "DELETE";
    }
}
