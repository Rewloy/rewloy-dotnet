using System;
using System.Collections.Generic;
using Rewloy.Models;

namespace Rewloy
{
    /// <summary>
    /// The whole answer to a call: what the <c>…WithResponseAsync</c> methods
    /// return. The plain methods give only the data.
    /// </summary>
    public class RewloyResponse
    {
        internal RewloyResponse(int statusCode, RewloyHeaders headers)
        {
            StatusCode = statusCode;
            Headers = headers;
        }

        /// <summary>The HTTP status: 200, 201, 202 or 204. Some operations answer 200 when they found what they would have created.</summary>
        public int StatusCode { get; }

        /// <summary>The answer's headers, response and content together.</summary>
        public RewloyHeaders Headers { get; }

        /// <summary><c>x-request-id</c>: quote it to Rewloy support.</summary>
        public string? RequestId => Headers.Get("x-request-id");

        /// <summary>
        /// <c>Rewloy-Mode</c>: which mode answered (<c>test</c> for a test key's calls, which reach no customer);
        /// <c>null</c> when the answer does not say.
        /// </summary>
        public string? Mode => Headers.Get("rewloy-mode");

        /// <summary>The answer came from a test key (<c>Rewloy-Mode: test</c>).</summary>
        public bool IsTestMode => string.Equals(Mode, "test", StringComparison.OrdinalIgnoreCase);

        /// <summary><c>Idempotent-Replayed: true</c>: the API replayed the first answer to this <c>Idempotency-Key</c>.</summary>
        public bool Replayed => string.Equals(Headers.Get("idempotent-replayed"), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The whole answer, with its data.</summary>
    public sealed class RewloyResponse<T> : RewloyResponse
    {
        internal RewloyResponse(int statusCode, RewloyHeaders headers, T data, PageMeta? meta)
            : base(statusCode, headers)
        {
            Data = data;
            Meta = meta;
        }

        /// <summary>What the answer's <c>data</c> held.</summary>
        public T Data { get; }

        /// <summary>Paging, on paged lists.</summary>
        public PageMeta? Meta { get; }
    }

    /// <summary>One page of a paged list.</summary>
    public sealed class Page<T>
    {
        /// <summary>Makes a page.</summary>
        /// <param name="data">The page's items.</param>
        /// <param name="meta">Which page it is, its size and the total.</param>
        public Page(IReadOnlyList<T> data, PageMeta meta)
        {
            Data = data;
            Meta = meta;
        }

        /// <summary>This page's items.</summary>
        public IReadOnlyList<T> Data { get; }

        /// <summary>Which page this is, its size and the total number of items.</summary>
        public PageMeta Meta { get; }

        internal static Page<T> From(RewloyResponse<IReadOnlyList<T>> response)
        {
            var data = response.Data ?? Array.Empty<T>();
            return new Page<T>(data, response.Meta ?? new PageMeta { Page = 1, PageSize = data.Count, Total = data.Count });
        }
    }

    /// <summary>A file the API answers with: a QR image, a map, a CSV, a <c>.pkpass</c>.</summary>
    public sealed class RewloyFile
    {
        /// <summary>Makes a file answer.</summary>
        /// <param name="content">The bytes.</param>
        /// <param name="contentType">The media type.</param>
        /// <param name="fileName">The name the answer suggests.</param>
        public RewloyFile(byte[] content, string? contentType, string? fileName)
        {
            Content = content;
            ContentType = contentType;
            FileName = fileName;
        }

        /// <summary>The file's bytes.</summary>
        public byte[] Content { get; }

        /// <summary>The media type, e.g. <c>image/png</c>.</summary>
        public string? ContentType { get; }

        /// <summary>The name the answer suggests (<c>Content-Disposition</c>), when it does.</summary>
        public string? FileName { get; }
    }
}
