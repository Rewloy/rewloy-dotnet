using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rewloy
{
    /// <summary>Collects query parameters and writes them as a query string (RFC 3986 encoding, a list repeats its key).</summary>
    internal sealed class QueryWriter
    {
        private readonly List<KeyValuePair<string, string>> _pairs = new List<KeyValuePair<string, string>>();

        public int Count => _pairs.Count;

        public void Add(string name, string? value)
        {
            if (value != null) _pairs.Add(new KeyValuePair<string, string>(name, value));
        }

        public void Add(string name, bool? value) => Add(name, value is null ? null : value.Value ? "true" : "false");

        public void Add(string name, int? value) => Add(name, value?.ToString(CultureInfo.InvariantCulture));

        public void Add(string name, long? value) => Add(name, value?.ToString(CultureInfo.InvariantCulture));

        public void Add(string name, double? value) => Add(name, value?.ToString("R", CultureInfo.InvariantCulture));

        public void Add(string name, Guid? value) => Add(name, value?.ToString("D"));

        public void Add(string name, DateTimeOffset? value) => Add(name, value?.ToString("o", CultureInfo.InvariantCulture));

        public void AddMany<T>(string name, IReadOnlyList<T>? values)
        {
            if (values == null) return;
            foreach (var v in values)
            {
                switch (v)
                {
                    case null: break;
                    case bool b: Add(name, (bool?)b); break;
                    case Guid g: Add(name, (Guid?)g); break;
                    case DateTimeOffset d: Add(name, (DateTimeOffset?)d); break;
                    case double x: Add(name, (double?)x); break;
                    case IFormattable f: Add(name, f.ToString(null, CultureInfo.InvariantCulture)); break;
                    default: Add(name, v.ToString()); break;
                }
            }
        }

        public string ToQueryString()
        {
            var sb = new StringBuilder();
            foreach (var p in _pairs)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(p.Key)).Append('=').Append(Uri.EscapeDataString(p.Value));
            }
            return sb.ToString();
        }
    }
}
