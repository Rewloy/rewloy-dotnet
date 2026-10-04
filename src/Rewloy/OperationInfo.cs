using System;
using System.Collections.Generic;

namespace Rewloy
{
    /// <summary>
    /// The credential kinds of the Rewloy API, as the OpenAPI document's
    /// <c>x-credentials</c> names them.
    /// </summary>
    [Flags]
    public enum CredentialKinds
    {
        /// <summary>No credential (also: the operation names none).</summary>
        None = 0,

        /// <summary>An API key, <c>rwk_…</c>: a till, a shop, your own system.</summary>
        Key = 1,

        /// <summary>A staff session, <c>rws_…</c>: a person's business app.</summary>
        Staff = 2,

        /// <summary>A card holder's session, <c>rwh_…</c>: a Rewloy Cüzdan app.</summary>
        Holder = 4,

        /// <summary>The operation works without a credential.</summary>
        Public = 8,
    }

    /// <summary>How a successful answer is read.</summary>
    public enum ResponseKind
    {
        /// <summary>The <c>{ "data": … }</c> envelope (with <c>meta</c> on paged lists).</summary>
        Json,

        /// <summary>204: no body.</summary>
        None,

        /// <summary>A file (an image, a CSV, a pass), returned as a <see cref="RewloyFile"/>.</summary>
        File,

        /// <summary>JSON without the envelope (the OpenAPI document itself).</summary>
        RawJson,

        /// <summary>Server-sent events.</summary>
        Stream,
    }

    /// <summary>Whether an operation takes an <c>Idempotency-Key</c>.</summary>
    public enum IdempotencyMode
    {
        /// <summary>It takes none.</summary>
        None,

        /// <summary>It takes one and works without.</summary>
        Optional,

        /// <summary>It requires one.</summary>
        Required,
    }

    /// <summary>An operation marked for removal.</summary>
    public sealed class Deprecation
    {
        /// <summary>Makes a deprecation note.</summary>
        /// <param name="sunset">The last day it works.</param>
        /// <param name="use">The operation that replaces it.</param>
        public Deprecation(string? sunset, string? use)
        {
            Sunset = sunset;
            Use = use;
        }

        /// <summary>The last day it works (<c>YYYY-MM-DD</c>), when the document says so.</summary>
        public string? Sunset { get; }

        /// <summary>The operation that replaces it, when the document says so.</summary>
        public string? Use { get; }
    }

    /// <summary>One row of the metadata table (<see cref="RewloyOperations"/>): what the client needs to call an operation.</summary>
    public sealed class OperationInfo
    {
        /// <summary>Makes a row of the table; the generated <see cref="RewloyOperations"/> do.</summary>
        public OperationInfo(
            string id, string method, string path, IReadOnlyList<string> pathParameters, CredentialKinds credentials,
            bool acceptsMerchant, IdempotencyMode idempotency, bool hasBody, ResponseKind response, bool isPaged, Deprecation? deprecation)
        {
            Id = id;
            Method = method;
            Path = path;
            PathParameters = pathParameters;
            Credentials = credentials;
            AcceptsMerchant = acceptsMerchant;
            Idempotency = idempotency;
            HasBody = hasBody;
            Response = response;
            IsPaged = isPaged;
            Deprecation = deprecation;
        }

        /// <summary>The operationId, e.g. <c>passAction</c>.</summary>
        public string Id { get; }

        /// <summary><c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>.</summary>
        public string Method { get; }

        /// <summary>The path with <c>{name}</c> placeholders, <c>/v1</c> included.</summary>
        public string Path { get; }

        /// <summary>The placeholders' names, in the order the path has them.</summary>
        public IReadOnlyList<string> PathParameters { get; }

        /// <summary>The credential kinds the operation accepts.</summary>
        public CredentialKinds Credentials { get; }

        /// <summary>Takes the <c>Rewloy-Merchant</c> header (staff sessions with seats in several businesses).</summary>
        public bool AcceptsMerchant { get; }

        /// <summary>Whether the operation takes an <c>Idempotency-Key</c>.</summary>
        public IdempotencyMode Idempotency { get; }

        /// <summary>Has a JSON request body.</summary>
        public bool HasBody { get; }

        /// <summary>How a successful answer is read.</summary>
        public ResponseKind Response { get; }

        /// <summary>A paged list: <c>page</c>/<c>limit</c> in, <c>meta</c> out.</summary>
        public bool IsPaged { get; }

        /// <summary>Answers with server-sent events.</summary>
        public bool IsStream => Response == ResponseKind.Stream;

        /// <summary>Set when the operation is marked for removal.</summary>
        public Deprecation? Deprecation { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Id} ({Method} {Path})";
    }
}
