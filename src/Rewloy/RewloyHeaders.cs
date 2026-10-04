using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Rewloy
{
    /// <summary>The headers of an answer: names are case-insensitive, response and content headers together.</summary>
    public sealed class RewloyHeaders : IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>No headers.</summary>
        public static RewloyHeaders Empty { get; } = new RewloyHeaders();

        internal static RewloyHeaders From(HttpResponseMessage response)
        {
            var h = new RewloyHeaders();
            foreach (var p in response.Headers) h._map[p.Key] = p.Value.ToList();
            if (response.Content != null)
            {
                foreach (var p in response.Content.Headers) h._map[p.Key] = p.Value.ToList();
            }
            return h;
        }

        /// <summary>The header's first value, or <c>null</c> when it is absent.</summary>
        public string? Get(string name) => _map.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

        /// <summary>All of the header's values; empty when it is absent.</summary>
        public IReadOnlyList<string> GetAll(string name) => _map.TryGetValue(name, out var values) ? values : Array.Empty<string>();

        /// <summary>Whether the answer has the header.</summary>
        public bool Contains(string name) => _map.ContainsKey(name);

        /// <summary>The number of distinct headers.</summary>
        public int Count => _map.Count;

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator() => _map.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
