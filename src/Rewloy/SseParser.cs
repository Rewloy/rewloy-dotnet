using System.Collections.Generic;
using System.Text;

namespace Rewloy
{
    /// <summary>
    /// Server-sent events (<c>text/event-stream</c>) as the HTML standard parses them
    /// (https://html.spec.whatwg.org/multipage/server-sent-events.html), fed text in pieces of any
    /// size: a line, an event or a CRLF may be split anywhere between two pieces.
    /// The API's streams start with <c>retry: 5000</c>, send <c>: hb</c> every 25 seconds and events as
    /// <c>event: &lt;type&gt;</c> + <c>data: &lt;text&gt;</c>; they carry no <c>id:</c> today.
    /// </summary>
    public sealed class SseParser
    {
        private readonly StringBuilder _line = new StringBuilder();
        private readonly StringBuilder _data = new StringBuilder();
        private string _event = string.Empty;
        private string _id;
        private bool _afterCr;
        private bool _started;
        private bool _hasData;

        /// <summary>Makes a parser.</summary>
        /// <param name="lastEventId">The last event ID to start from (a reconnection's).</param>
        public SseParser(string lastEventId = "")
        {
            LastEventId = lastEventId;
            _id = lastEventId;
        }

        /// <summary>The reconnection time the stream asked for (<c>retry:</c>), in milliseconds; <c>null</c> until it does.</summary>
        public long? RetryMilliseconds { get; private set; }

        /// <summary>
        /// The last event ID: set from the <c>id:</c> buffer at every blank line, kept from one event to the
        /// next, as the standard says.
        /// </summary>
        public string LastEventId { get; private set; }

        /// <summary>Feeds decoded text; returns the events it completed.</summary>
        public IReadOnlyList<ServerSentEvent> Push(string text)
        {
            var events = new List<ServerSentEvent>();
            var i = 0;
            if (!_started && text.Length > 0)
            {
                _started = true;
                if (text[0] == '﻿') i = 1;
            }
            // A CR ended the previous piece: an LF right after it belongs to the same line ending.
            if (_afterCr && i < text.Length)
            {
                if (text[i] == '\n') i++;
                _afterCr = false;
            }
            while (i < text.Length)
            {
                var j = i;
                while (j < text.Length && text[j] != '\n' && text[j] != '\r') j++;
                if (j == text.Length)
                {
                    _line.Append(text, i, text.Length - i);
                    break;
                }
                _line.Append(text, i, j - i);
                var line = _line.ToString();
                _line.Clear();
                if (text[j] == '\r')
                {
                    if (j + 1 < text.Length)
                    {
                        if (text[j + 1] == '\n') j++;
                    }
                    else _afterCr = true;
                }
                i = j + 1;
                Take(line, events);
            }
            return events;
        }

        /// <summary>The stream ended: an event without its blank line is dropped, as the standard says.</summary>
        public void End()
        {
            _line.Clear();
            _data.Clear();
            _hasData = false;
            _event = string.Empty;
            _afterCr = false;
        }

        private void Take(string line, List<ServerSentEvent> events)
        {
            if (line.Length == 0)
            {
                LastEventId = _id;
                if (!_hasData)
                {
                    _event = string.Empty;
                    return;
                }
                var data = _data.ToString();
                if (data.EndsWith("\n", System.StringComparison.Ordinal)) data = data.Substring(0, data.Length - 1);
                events.Add(new ServerSentEvent(_event.Length > 0 ? _event : "message", data, LastEventId));
                _data.Clear();
                _hasData = false;
                _event = string.Empty;
                return;
            }
            if (line[0] == ':') return;
            var colon = line.IndexOf(':');
            var field = colon == -1 ? line : line.Substring(0, colon);
            var value = colon == -1 ? string.Empty : line.Substring(colon + 1);
            if (value.StartsWith(" ", System.StringComparison.Ordinal)) value = value.Substring(1);
            switch (field)
            {
                case "event": _event = value; break;
                case "data": _data.Append(value).Append('\n'); _hasData = true; break;
                case "id": if (value.IndexOf('\0') < 0) _id = value; break;
                case "retry":
                    if (value.Length > 0 && IsDigits(value) && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ms)) RetryMilliseconds = ms;
                    break;
            }
        }

        private static bool IsDigits(string s)
        {
            foreach (var c in s)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
