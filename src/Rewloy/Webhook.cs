using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rewloy
{
    /// <summary>
    /// What a webhook's <c>data</c> holds for the <c>pass.*</c> events: only business facts, never contact details.
    /// </summary>
    public sealed class PassEventData : RewloyObject
    {
        /// <summary>What happened on the card: <c>join</c>, <c>earn</c>, <c>redeem</c>, <c>spend</c>, <c>visit_credit</c>, <c>load</c>, <c>void</c>...</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        /// <summary>The card's serial number, XXXX-XXXX-XXXX.</summary>
        [JsonPropertyName("card")]
        public string? Card { get; set; }

        /// <summary>The programme's id.</summary>
        [JsonPropertyName("program_id")]
        public string? ProgramId { get; set; }

        /// <summary>The branch's id.</summary>
        [JsonPropertyName("location_id")]
        public string? LocationId { get; set; }

        /// <summary>The customer's id: read the person from the API.</summary>
        [JsonPropertyName("customer_id")]
        public string? CustomerId { get; set; }

        /// <summary>What <see cref="Delta"/> counts: <c>stamp</c>, <c>point</c>, <c>try_minor</c> (kuruş)...</summary>
        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        /// <summary>How much the card changed, in <see cref="Unit"/>.</summary>
        [JsonPropertyName("delta")]
        public double? Delta { get; set; }

        /// <summary>Why, for a void.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    /// <summary>
    /// A webhook delivery's body. Read the person behind <c>customer_id</c> from the API; new event types may
    /// appear, so keep a default branch on <see cref="Type"/>.
    /// </summary>
    public sealed class WebhookEvent
    {
        internal WebhookEvent(JsonElement root)
        {
            Root = root;
        }

        /// <summary>The whole body, as JSON.</summary>
        public JsonElement Root { get; }

        /// <summary>The event's id; <c>null</c> for the test delivery.</summary>
        public string? Id => Text("id");

        /// <summary>The event type: <c>pass.issued</c>, <c>pass.activity</c>, <c>pass.voided</c> or <c>webhook.test</c> (the panel's or <c>testWebhook</c>'s test delivery). Same as the <c>Rewloy-Event</c> header.</summary>
        public string Type => Text("type") ?? string.Empty;

        /// <summary>When it happened (<c>created_at</c>), when the body says and it parses.</summary>
        public DateTimeOffset? CreatedAt =>
            Text("created_at") is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : (DateTimeOffset?)null;

        /// <summary>The event's <c>data</c>.</summary>
        public JsonElement Data => Root.TryGetProperty("data", out var d) ? d : default;

        /// <summary>
        /// The <c>data</c> of a <c>pass.*</c> event; <c>null</c> for any other type or a <c>data</c> that
        /// is not an object.
        /// </summary>
        public PassEventData? PassData =>
            Type.StartsWith("pass.", StringComparison.Ordinal) && Data.ValueKind == JsonValueKind.Object ? Data.Deserialize<PassEventData>(RewloyJson.Options) : null;

        private string? Text(string name) => Root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>
    /// Webhook signatures, exactly as the platform signs a delivery:
    /// <c>Rewloy-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256(secret, "&lt;t&gt;.&lt;raw body&gt;")&gt;</c>.
    /// The key is the whole secret as shown once when the webhook was created (<c>whsec_…</c>, prefix included); the
    /// message is the timestamp, a dot and the body's bytes as they arrived. Each delivery attempt is signed anew,
    /// so a retry carries a fresh <c>t</c>.
    /// <para>
    /// Every delivery also carries <c>Rewloy-Event</c> (the event type) and <c>Rewloy-Delivery</c> (the delivery's id:
    /// the same on every retry of one delivery; deliveries are at least once, so skip an id already handled).
    /// </para>
    /// </summary>
    public static class Webhook
    {
        private static readonly Regex V1 = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex Digits = new Regex("^[0-9]+$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Checks a delivery's <c>Rewloy-Signature</c> and returns its parsed body. Throws
        /// <see cref="WebhookSignatureException"/> when the header is missing or malformed, <c>t</c> is further than
        /// <paramref name="toleranceSeconds"/> from now, or no <c>v1</c> matches; the comparison takes constant time.
        /// </summary>
        /// <param name="payload">The body exactly as it arrived, as text. Not re-serialized JSON: parsing and writing it again changes the bytes the signature covers.</param>
        /// <param name="header">The <c>Rewloy-Signature</c> header (several values are joined with commas).</param>
        /// <param name="secret">The webhook's secret (<c>whsec_…</c>).</param>
        /// <param name="toleranceSeconds">How far <c>t</c> may be from now, in seconds. Default 300.</param>
        /// <param name="now">The current time, for tests.</param>
        /// <returns>The delivery, parsed.</returns>
        public static WebhookEvent Verify(string payload, string? header, string secret, int toleranceSeconds = 300, DateTimeOffset? now = null) =>
            Verify(payload, header, new[] { secret }, toleranceSeconds, now);

        /// <summary>
        /// Checks a delivery's <c>Rewloy-Signature</c> against several secrets (while you move from one webhook
        /// to another) and returns its parsed body. See the overload with one secret.
        /// </summary>
        /// <param name="payload">The body exactly as it arrived, as text.</param>
        /// <param name="header">The <c>Rewloy-Signature</c> header.</param>
        /// <param name="secrets">The secrets: any may match.</param>
        /// <param name="toleranceSeconds">How far <c>t</c> may be from now, in seconds. Default 300.</param>
        /// <param name="now">The current time, for tests.</param>
        /// <returns>The delivery, parsed.</returns>
        public static WebhookEvent Verify(string payload, string? header, IEnumerable<string> secrets, int toleranceSeconds = 300, DateTimeOffset? now = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Verify(Encoding.UTF8.GetBytes(payload), header, secrets, toleranceSeconds, now);
        }

        /// <summary>
        /// Checks a delivery's <c>Rewloy-Signature</c> and returns its parsed body. See the overload that takes text.
        /// </summary>
        /// <param name="payload">The body's bytes exactly as they arrived (read the request stream, not a parsed model).</param>
        /// <param name="header">The <c>Rewloy-Signature</c> header.</param>
        /// <param name="secret">The webhook's secret (<c>whsec_…</c>).</param>
        /// <param name="toleranceSeconds">How far <c>t</c> may be from now, in seconds. Default 300.</param>
        /// <param name="now">The current time, for tests.</param>
        /// <returns>The delivery, parsed.</returns>
        public static WebhookEvent Verify(byte[] payload, string? header, string secret, int toleranceSeconds = 300, DateTimeOffset? now = null) =>
            Verify(payload, header, new[] { secret }, toleranceSeconds, now);

        /// <summary>
        /// Checks a delivery's <c>Rewloy-Signature</c> against several secrets and returns its parsed body. See the
        /// overload that takes text.
        /// </summary>
        /// <param name="payload">The body's bytes exactly as they arrived.</param>
        /// <param name="header">The <c>Rewloy-Signature</c> header.</param>
        /// <param name="secrets">The secrets: any may match.</param>
        /// <param name="toleranceSeconds">How far <c>t</c> may be from now, in seconds. Default 300.</param>
        /// <param name="now">The current time, for tests.</param>
        /// <returns>The delivery, parsed.</returns>
        public static WebhookEvent Verify(byte[] payload, string? header, IEnumerable<string> secrets, int toleranceSeconds = 300, DateTimeOffset? now = null)
{
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var keys = (secrets ?? throw new ArgumentNullException(nameof(secrets))).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (keys.Count == 0) throw new ArgumentException("The secret is empty: a missing setting, not a bad delivery.", nameof(secrets));

            if (header == null || header.Trim().Length == 0) throw new WebhookSignatureException(WebhookFailure.Missing, "No Rewloy-Signature header");
            string? t = null;
            var candidates = new List<byte[]>();
            foreach (var part in header.Split(','))
            {
                var eq = part.IndexOf('=');
                if (eq == -1) continue;
                var k = part.Substring(0, eq).Trim();
                var v = part.Substring(eq + 1).Trim();
                if (k == "t" && t == null) t = v;
                else if (k == "v1" && V1.IsMatch(v)) candidates.Add(FromHex(v));
            }
            if (t == null || !Digits.IsMatch(t) || candidates.Count == 0 || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var signedAt))
            {
                throw new WebhookSignatureException(WebhookFailure.Malformed, "Rewloy-Signature is not \"t=<unix seconds>,v1=<hex>\"");
            }

            var nowSeconds = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() / 1000.0;
            if (Math.Abs(nowSeconds - signedAt) > toleranceSeconds)
            {
                throw new WebhookSignatureException(WebhookFailure.Expired, "The signature's time (t=" + t + ") is more than " + toleranceSeconds.ToString(CultureInfo.InvariantCulture) + " seconds from now");
            }

            var matched = false;
            foreach (var secret in keys)
            {
                var expected = Mac(secret, t, payload);
                foreach (var candidate in candidates)
                {
                    // Every pair is compared, in constant time, whether or not one matched already.
                    if (FixedTimeEquals(candidate, expected)) matched = true;
                }
            }
            if (!matched) throw new WebhookSignatureException(WebhookFailure.Mismatch, "No v1 signature matches the body and the secret");

            try
            {
                using (var doc = JsonDocument.Parse(payload))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new WebhookSignatureException(WebhookFailure.Payload, "The signed body is not a JSON object");
                    return new WebhookEvent(doc.RootElement.Clone());
                }
            }
            catch (JsonException)
            {
                throw new WebhookSignatureException(WebhookFailure.Payload, "The signed body is not JSON");
            }
        }

        /// <summary>
        /// The <c>Rewloy-Signature</c> header the platform would send for this body: for testing your own
        /// webhook handler.
        /// </summary>
        /// <param name="payload">The body.</param>
        /// <param name="secret">The secret (<c>whsec_…</c>).</param>
        /// <param name="timestamp">The time to sign with; now when left out.</param>
        public static string Sign(string payload, string secret, DateTimeOffset? timestamp = null) =>
            Sign(Encoding.UTF8.GetBytes(payload ?? throw new ArgumentNullException(nameof(payload))), secret, timestamp);

        /// <inheritdoc cref="Sign(string, string, DateTimeOffset?)"/>
        public static string Sign(byte[] payload, string secret, DateTimeOffset? timestamp = null)
        {
            var t = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            return "t=" + t + ",v1=" + ToHex(Mac(secret, t, payload));
        }

        private static byte[] Mac(string secret, string t, byte[] body)
        {
            var prefix = Encoding.UTF8.GetBytes(t + ".");
            var message = new byte[prefix.Length + body.Length];
            Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
            Buffer.BlockCopy(body, 0, message, prefix.Length, body.Length);
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) return hmac.ComputeHash(message);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
#if NET6_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(a, b);
#else
            var diff = a.Length ^ b.Length;
            for (var i = 0; i < a.Length && i < b.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
#endif
        }

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return bytes;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
