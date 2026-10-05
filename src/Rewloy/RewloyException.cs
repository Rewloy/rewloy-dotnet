using System;
using System.Linq;
using System.Text.Json;

namespace Rewloy
{
    /// <summary>
    /// What the client throws. Every failure to get an answer from Rewloy is a
    /// RewloyException: an error answer from the API (with its stable
    /// <see cref="Code"/>), an answer that is not what the API documents, or
    /// no answer at all.
    /// <para>
    /// Act on <see cref="Code"/> (see <see cref="ErrorCode"/>): it is stable,
    /// while <see cref="Detail"/> is a human sentence in Turkish that may
    /// change. Besides the API's codes the client uses:
    /// <c>CONNECTION_ERROR</c> and <c>TIMEOUT</c> (status 0: no answer arrived),
    /// <c>INVALID_RESPONSE</c> (a 2xx answer that is not the documented JSON) and
    /// <c>HTTP_&lt;status&gt;</c> (an error answer without Rewloy's error body, such as a proxy's 502 page).
    /// </para>
    /// </summary>
    public class RewloyException : Exception
    {
        /// <summary>Makes the exception the client throws for a failed call.</summary>
        /// <param name="status">The HTTP status; 0 when no answer arrived.</param>
        /// <param name="code">The API's stable machine code.</param>
        /// <param name="detail">What happened, in the API's words.</param>
        /// <param name="title">The code's title in the catalogue.</param>
        /// <param name="details">The API's <c>error.details</c>.</param>
        /// <param name="docs">Where the catalogue explains the code.</param>
        /// <param name="requestId">The request's id.</param>
        /// <param name="body">The answer's body as text.</param>
        /// <param name="headers">The answer's headers.</param>
        /// <param name="operation">The operationId of the call.</param>
        /// <param name="innerException">The exception behind it, for a failure without an answer.</param>
        public RewloyException(
            int status, string code, string detail, string? title = null, JsonElement? details = null, string? docs = null,
            string? requestId = null, string? body = null, RewloyHeaders? headers = null, string? operation = null, Exception? innerException = null)
            : base(BuildMessage(status, code, detail, operation, requestId), innerException)
        {
            Status = status;
            Code = code;
            Detail = detail;
            Title = title;
            Details = details;
            Docs = docs;
            RequestId = requestId;
            Body = body;
            Headers = headers;
            Operation = operation;
        }

        private static string BuildMessage(int status, string code, string detail, string? operation, string? requestId)
        {
            var where = string.Join(", ", new[] { operation, requestId is null ? null : "requestId " + requestId }.Where(s => !string.IsNullOrEmpty(s))!);
            return (status != 0 ? status + " " : string.Empty) + code + ": " + detail + (where.Length > 0 ? " (" + where + ")" : string.Empty);
        }

        /// <summary>The HTTP status; 0 when no answer arrived.</summary>
        public int Status { get; }

        /// <summary>The API's stable machine code, e.g. <c>INSUFFICIENT_BALANCE</c> (the constants of <see cref="ErrorCode"/>).</summary>
        public string Code { get; }

        /// <summary>The code's one-line title in the catalogue, e.g. "Bakiye yetersiz"; <c>null</c> for a code the catalogue lacks.</summary>
        public string? Title { get; }

        /// <summary>What happened, in the API's words (<c>error.message</c>).</summary>
        public string Detail { get; }

        /// <summary>
        /// The API's <c>error.details</c>, when it sent any: for <c>VALIDATION</c> a list of <c>{ field, rule, message }</c>,
        /// for others what the catalogue says.
        /// </summary>
        public JsonElement? Details { get; }

        /// <summary>Where the catalogue explains the code (<c>error.docs</c>).</summary>
        public string? Docs { get; }

        /// <summary><c>x-request-id</c>: quote it to Rewloy support.</summary>
        public string? RequestId { get; }

        /// <summary>The answer's body as text (the API's JSON, or a proxy's page).</summary>
        public string? Body { get; }

        /// <summary>The answer's headers; <c>null</c> when no answer arrived.</summary>
        public RewloyHeaders? Headers { get; }

        /// <summary>The <c>RateLimit-*</c> headers of the answer; <c>null</c> when it carried none.</summary>
        public RewloyRateLimit? RateLimit => RewloyRateLimit.From(Headers);

        /// <summary>The operationId of the call.</summary>
        public string? Operation { get; }
    }

    /// <summary>429 <c>RATE_LIMITED</c>: too many requests for this credential or this action.</summary>
    public sealed class RateLimitException : RewloyException
    {
        /// <summary>Makes the exception for a 429 answer.</summary>
        /// <param name="status">The HTTP status (429).</param>
        /// <param name="code">The API's code.</param>
        /// <param name="detail">What happened, in the API's words.</param>
        /// <param name="retryAfter">How long the API asked to wait.</param>
        /// <param name="title">The code's title in the catalogue.</param>
        /// <param name="details">The API's <c>error.details</c>.</param>
        /// <param name="docs">Where the catalogue explains the code.</param>
        /// <param name="requestId">The request's id.</param>
        /// <param name="body">The answer's body as text.</param>
        /// <param name="headers">The answer's headers.</param>
        /// <param name="operation">The operationId of the call.</param>
        public RateLimitException(
            int status, string code, string detail, TimeSpan? retryAfter, string? title = null, JsonElement? details = null, string? docs = null,
            string? requestId = null, string? body = null, RewloyHeaders? headers = null, string? operation = null)
            : base(status, code, detail, title, details, docs, requestId, body, headers, operation)
        {
            RetryAfter = retryAfter;
        }

        /// <summary>How long to wait before trying again (<c>Retry-After</c>), when the API said.</summary>
        public TimeSpan? RetryAfter { get; }
    }

    /// <summary>No answer arrived: the connection failed or broke (<c>CONNECTION_ERROR</c>, status 0).</summary>
    public class RewloyConnectionException : RewloyException
    {
        /// <summary>Makes the exception for a call that got no answer.</summary>
        /// <param name="detail">What went wrong.</param>
        /// <param name="operation">The operationId of the call.</param>
        /// <param name="requestId">The request's id, for a stream that had one.</param>
        /// <param name="innerException">The exception behind it.</param>
        /// <param name="code"><c>CONNECTION_ERROR</c>, or <c>TIMEOUT</c> for the subclass.</param>
        public RewloyConnectionException(string detail, string? operation = null, string? requestId = null, Exception? innerException = null, string code = "CONNECTION_ERROR")
            : base(0, code, detail, operation: operation, requestId: requestId, innerException: innerException)
        {
        }
    }

    /// <summary>No answer within the timeout (<c>TIMEOUT</c>), or a stream fell silent.</summary>
    public sealed class RewloyTimeoutException : RewloyConnectionException
    {
        /// <summary>Makes the exception for a call that timed out.</summary>
        /// <param name="detail">What timed out.</param>
        /// <param name="operation">The operationId of the call.</param>
        /// <param name="requestId">The request's id, for a stream that had one.</param>
        /// <param name="innerException">The exception behind it.</param>
        public RewloyTimeoutException(string detail, string? operation = null, string? requestId = null, Exception? innerException = null)
            : base(detail, operation, requestId, innerException, "TIMEOUT")
        {
        }
    }
}
