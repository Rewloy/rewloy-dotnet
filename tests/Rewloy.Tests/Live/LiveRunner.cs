using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rewloy.Tests.Live
{
    /// <summary>Runs named checks grouped in areas; a failing check is recorded and the run goes on.</summary>
    internal sealed class LiveRunner
    {
        private readonly Action<string> _log;
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, Area> _areas = new Dictionary<string, Area>();

        public LiveRunner(Action<string> log) { _log = log; }

        private sealed class Area
        {
            public int Passed;
            public readonly List<string> Failed = new List<string>();
            public readonly List<string> Skipped = new List<string>();
        }

        private Area Of(string area)
        {
            if (!_areas.TryGetValue(area, out var a)) { a = new Area(); _areas[area] = a; _order.Add(area); }
            return a;
        }

        public async Task Check(string area, string name, Func<Task> body)
        {
            var a = Of(area);
            try
            {
                await body().ConfigureAwait(false);
                a.Passed++;
                _log("  ok    " + area + " / " + name);
            }
            catch (Exception e)
            {
                var msg = e is RewloyException re ? re.Message : e.GetType().Name + ": " + e.Message;
                if (msg.Length > 600) msg = msg.Substring(0, 600) + " …";
                a.Failed.Add(name + " -> " + msg);
                _log("  FAIL  " + area + " / " + name + "\n        " + msg.Replace("\n", "\n        "));
            }
        }

        public void Skip(string area, string name, string why)
        {
            Of(area).Skipped.Add(name + " (" + why + ")");
            _log("  skip  " + area + " / " + name + ": " + why);
        }

        public int FailedCount => _areas.Values.Sum(a => a.Failed.Count);

        public string Summary()
        {
            var w = _order.Count == 0 ? 10 : _order.Max(n => n.Length);
            var lines = new List<string> { "", "Rewloy .NET live tests: summary" };
            foreach (var n in _order)
            {
                var a = _areas[n];
                lines.Add("  " + n.PadRight(w) + "  passed " + a.Passed + ", failed " + a.Failed.Count + (a.Skipped.Count > 0 ? ", skipped " + a.Skipped.Count : ""));
            }
            lines.Add("  " + "TOTAL".PadRight(w) + "  passed " + _areas.Values.Sum(a => a.Passed) + ", failed " + FailedCount);
            foreach (var n in _order)
                foreach (var f in _areas[n].Failed) lines.Add("  FAILED " + n + " / " + f);
            return string.Join("\n", lines);
        }
    }
}
