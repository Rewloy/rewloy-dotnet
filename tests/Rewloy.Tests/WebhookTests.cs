using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Xunit.Sdk;

namespace Rewloy.Tests
{
    /// <summary>
    /// The vectors are made the way the platform signs a delivery (its src/modules/webhooks/service.ts,
    /// <c>sign</c>), written out again here, not imported:
    /// <c>`t=${t},v1=${createHmac('sha256', secret).update(`${t}.${body}`).digest('hex')}`</c>
    /// with <c>body = JSON.stringify(payload)</c> and the secret <c>whsec_…</c> as the key.
    /// They are the Node and PHP libraries' two fixed vectors (their webhook tests), so that all three
    /// libraries are pinned to the same bytes.
    /// </summary>
    public class WebhookTests
    {
        private const string Secret = "whsec_dGVzdC1zZWNyZXQtZm9yLXJld2xveS1ub2RlLXRlc3Rz";
        private const long T = 1790000000;
        private const string Body = "{\"id\":\"0192f7c1-8b2e-7a31-9c1d-2e4f5a6b7c8d\",\"type\":\"pass.activity\",\"created_at\":\"2026-10-03T12:00:00.000Z\",\"data\":{\"kind\":\"earn\",\"card\":\"ABCD-EFGH-JKLM\",\"program_id\":\"0192f7c1-0000-7000-8000-000000000002\",\"location_id\":\"0192f7c1-0000-7000-8000-000000000003\",\"customer_id\":\"0192f7c1-0000-7000-8000-000000000004\",\"unit\":\"stamp\",\"delta\":2}}";

        /// <summary>Computed once with the platform's formula; pins it against both sides changing together.</summary>
        private const string Fixed = "t=1790000000,v1=b17b337b887316b1e0e19c3f16bdc4936c03e93d16fbec3da121aacc0ec1eda7";
        private const string TestBody = "{\"type\":\"webhook.test\",\"created_at\":\"2026-10-03T12:00:00.000Z\",\"data\":{\"message\":\"Rewloy webhook testi — ğüşıöç\"}}";
        private const string TestFixed = "t=1790000000,v1=b06a92a7fb131aed835f2564fdf06a93461e0edb9216b4464598812f47704b1e";

        private static DateTimeOffset At(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

        /// <summary>The platform's <c>sign</c>, in C#.</summary>
        private static string ServerSign(string secret, string body, long t)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(t + "." + body));
            return "t=" + t + ",v1=" + BitConverter.ToString(mac).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void Refused(WebhookFailure reason, string payload, string? header, string secret = Secret, long now = T, int tolerance = 300)
        {
            var ex = Assert.Throws<WebhookSignatureException>(() => Webhook.Verify(payload, header, secret, tolerance, At(now)));
            Assert.Equal(reason, ex.Reason);
        }

        [Fact]
        public void Accepts_what_the_platform_signs_as_text_or_bytes()
        {
            Assert.Equal(Fixed, ServerSign(Secret, Body, T));
            Assert.Equal(TestFixed, ServerSign(Secret, TestBody, T));

            foreach (var ev in new[]
            {
                Webhook.Verify(Body, Fixed, Secret, now: At(T + 10)),
                Webhook.Verify(Encoding.UTF8.GetBytes(Body), Fixed, Secret, now: At(T + 10)),
            })
            {
                Assert.Equal("pass.activity", ev.Type);
                Assert.Equal("0192f7c1-8b2e-7a31-9c1d-2e4f5a6b7c8d", ev.Id);
                Assert.Equal(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), ev.CreatedAt);
                var data = ev.PassData!;
                Assert.Equal("earn", data.Kind);
                Assert.Equal("ABCD-EFGH-JKLM", data.Card);
                Assert.Equal("0192f7c1-0000-7000-8000-000000000004", data.CustomerId);
                Assert.Equal("stamp", data.Unit);
                Assert.Equal(2, data.Delta);
                Assert.Equal(2, ev.Data.GetProperty("delta").GetInt32());
            }

            var test = Webhook.Verify(Encoding.UTF8.GetBytes(TestBody), TestFixed, Secret, now: At(T));
            Assert.Equal("webhook.test", test.Type);
            Assert.Null(test.Id);
            Assert.Null(test.PassData);
            Assert.Equal("Rewloy webhook testi — ğüşıöç", test.Data.GetProperty("message").GetString());
        }

        [Fact]
        public void Accepts_other_entries_around_v1()
        {
            Assert.Equal("pass.activity", Webhook.Verify(Body, " v0=abc, " + Fixed.Replace(",", " , ") + " ", Secret, now: At(T)).Type);
        }

        [Fact]
        public void Accepts_any_of_several_v1_signatures_and_any_of_several_secrets()
        {
            var other = ServerSign("whsec_other", Body, T).Split(',')[1];
            Assert.Equal("pass.activity", Webhook.Verify(Body, Fixed + "," + other, Secret, now: At(T)).Type);
            Assert.Equal("pass.activity", Webhook.Verify(Body, "t=" + T + "," + other + "," + Fixed.Split(',')[1], Secret, now: At(T)).Type);
            Assert.Equal("pass.activity", Webhook.Verify(Body, Fixed, new[] { "whsec_new", Secret }, now: At(T)).Type);
            Assert.Equal("pass.activity", Webhook.Verify(Encoding.UTF8.GetBytes(Body), Fixed, new List<string> { "whsec_new", Secret }, now: At(T)).Type);
        }

        [Fact]
        public void Refuses_a_changed_body_the_wrong_secret_and_a_v1_for_another_time()
        {
            Refused(WebhookFailure.Mismatch, Body.Replace("\"delta\":2", "\"delta\":20"), Fixed);
            Refused(WebhookFailure.Mismatch, Body + "\n", Fixed);
            Refused(WebhookFailure.Mismatch, Body, Fixed, "whsec_wrong");
            Refused(WebhookFailure.Mismatch, Body, Fixed.Replace("t=1790000000", "t=1790000001"));
            // The prefix is part of the key.
            Refused(WebhookFailure.Mismatch, Body, Fixed, Secret.Substring("whsec_".Length));
        }

        [Fact]
        public void Refuses_a_time_outside_the_tolerance_either_way()
        {
            Assert.Equal("pass.activity", Webhook.Verify(Body, Fixed, Secret, now: At(T + 300)).Type);
            Assert.Equal("pass.activity", Webhook.Verify(Body, Fixed, Secret, now: At(T - 300)).Type);
            Refused(WebhookFailure.Expired, Body, Fixed, now: T + 301);
            Refused(WebhookFailure.Expired, Body, Fixed, now: T - 301);
            Assert.Equal("pass.activity", Webhook.Verify(Body, Fixed, Secret, 3600, At(T + 3600)).Type);
            // Real time: a 2026 signature is long expired.
            var ex = Assert.Throws<WebhookSignatureException>(() => Webhook.Verify(Body, "t=1000,v1=" + new string('a', 64), Secret));
            Assert.Equal(WebhookFailure.Expired, ex.Reason);
        }

        [Fact]
        public void Refuses_a_missing_or_malformed_header()
        {
            foreach (var header in new string?[] { null, "", "  " }) Refused(WebhookFailure.Missing, Body, header);
            foreach (var header in new[]
            {
                "v1=" + new string('a', 64), "t=1790000000", "t=abc,v1=" + new string('a', 64), "t=1790000000,v1=xyz",
                "t=1790000000,v1=" + new string('a', 63), "t=99999999999999999999999,v1=" + new string('a', 64), "t=-5,v1=" + new string('a', 64), "garbage",
            })
            {
                Refused(WebhookFailure.Malformed, Body, header);
            }
        }

        [Fact]
        public void Refuses_a_signed_body_that_is_not_a_json_object_and_an_empty_secret()
        {
            Refused(WebhookFailure.Payload, "not json", ServerSign(Secret, "not json", T));
            Refused(WebhookFailure.Payload, "[1,2]", ServerSign(Secret, "[1,2]", T));
            Refused(WebhookFailure.Payload, "\"text\"", ServerSign(Secret, "\"text\"", T));
            Assert.Throws<ArgumentException>(() => Webhook.Verify(Body, Fixed, "", now: At(T)));
            Assert.Throws<ArgumentException>(() => Webhook.Verify(Body, Fixed, new string[0], now: At(T)));
            Assert.Throws<ArgumentNullException>(() => Webhook.Verify((string)null!, Fixed, Secret));
        }

        [Fact]
        public void Signs_as_the_platform_does_for_testing_your_own_handler()
        {
            Assert.Equal(Fixed, Webhook.Sign(Body, Secret, At(T)));
            Assert.Equal(TestFixed, Webhook.Sign(Encoding.UTF8.GetBytes(TestBody), Secret, At(T)));
            var header = Webhook.Sign(Body, Secret);
            Assert.Equal("pass.activity", Webhook.Verify(Body, header, Secret).Type);
        }

        [Fact]
        public void Compares_in_constant_time_with_the_framework_primitive_where_there_is_one()
        {
            // Not a timing test (those are flaky); it pins that a v1 of the wrong length never matches and never throws.
            Refused(WebhookFailure.Malformed, Body, "t=1790000000,v1=" + new string('a', 66));
        }
    }
}
