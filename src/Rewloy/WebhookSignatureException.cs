using System;

namespace Rewloy
{
    /// <summary>Why a delivery was refused.</summary>
    public enum WebhookFailure
    {
        /// <summary>There is no <c>Rewloy-Signature</c> header.</summary>
        Missing,

        /// <summary>The header is not <c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>.</summary>
        Malformed,

        /// <summary>The signature's time is further from now than the tolerance, either way.</summary>
        Expired,

        /// <summary>No <c>v1</c> signature matches the body and the secret.</summary>
        Mismatch,

        /// <summary>The signed body is not a JSON object.</summary>
        Payload,
    }

    /// <summary>The delivery is not a genuine one: answer it with 400 and do not act on it.</summary>
    public sealed class WebhookSignatureException : Exception
    {
        /// <summary>Makes the exception.</summary>
        /// <param name="reason">Why the delivery was refused.</param>
        /// <param name="message">What was wrong.</param>
        public WebhookSignatureException(WebhookFailure reason, string message)
            : base(message)
        {
            Reason = reason;
        }

        /// <summary>Why the delivery was refused.</summary>
        public WebhookFailure Reason { get; }
    }
}
