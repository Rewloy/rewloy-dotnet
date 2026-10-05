using System;
using System.Globalization;

namespace Rewloy
{
    /// <summary>
    /// The request budget the API reports on every answer to an authenticated call: the <c>RateLimit-Limit</c>,
    /// <c>RateLimit-Remaining</c> and <c>RateLimit-Reset</c> headers.
    /// <see cref="RewloyResponse.RateLimit"/> and <see cref="RewloyException.RateLimit"/> return it.
    /// </summary>
    public sealed class RewloyRateLimit
    {
        internal RewloyRateLimit(int limit, int remaining, int resetSeconds)
        {
            Limit = limit;
            Remaining = remaining;
            ResetSeconds = resetSeconds;
        }

        /// <summary><c>RateLimit-Limit</c>: requests allowed per minute.</summary>
        public int Limit { get; }

        /// <summary><c>RateLimit-Remaining</c>: requests left in this minute.</summary>
        public int Remaining { get; }

        /// <summary><c>RateLimit-Reset</c>: seconds until the limit renews.</summary>
        public int ResetSeconds { get; }

        /// <summary><see cref="ResetSeconds"/> as a <see cref="TimeSpan"/>.</summary>
        public TimeSpan Reset => TimeSpan.FromSeconds(ResetSeconds);

        /// <summary>Reads the three headers; <c>null</c> unless all of them are whole numbers.</summary>
        internal static RewloyRateLimit? From(RewloyHeaders? headers)
        {
            if (headers is null) return null;
            if (!TryRead(headers.Get("ratelimit-limit"), out var limit)
                || !TryRead(headers.Get("ratelimit-remaining"), out var remaining)
                || !TryRead(headers.Get("ratelimit-reset"), out var reset)) return null;
            return new RewloyRateLimit(limit, remaining, reset);
        }

        private static bool TryRead(string? value, out int number)
        {
            number = 0;
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text!)
            {
                if (c < '0' || c > '9') return false;
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }
    }
}
