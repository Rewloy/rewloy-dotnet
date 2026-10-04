using System;

namespace Rewloy
{
    /// <summary>
    /// An answer carried the <c>Deprecation</c> header: the operation is marked for removal.
    /// <see cref="RewloyClient.Deprecated"/> raises this once per operation per process.
    /// </summary>
    public sealed class DeprecationEventArgs : EventArgs
    {
        internal DeprecationEventArgs(OperationInfo operation, string? sunset, string? link, string message)
        {
            Operation = operation;
            Sunset = sunset;
            Link = link;
            Message = message;
        }

        /// <summary>The operation that is deprecated.</summary>
        public OperationInfo Operation { get; }

        /// <summary>The answer's <c>Sunset</c> header: the date the API stops answering the operation.</summary>
        public string? Sunset { get; }

        /// <summary>The <c>Link</c> header's <c>rel="deprecation"</c> URL: what to read about the change.</summary>
        public string? Link { get; }

        /// <summary>A sentence naming the operation, the sunset and the link, ready for a log.</summary>
        public string Message { get; }
    }
}
