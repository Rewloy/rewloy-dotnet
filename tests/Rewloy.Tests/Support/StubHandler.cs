using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rewloy.Tests.Support
{
    /// <summary>What the stub saw of a request.</summary>
    public sealed class Seen
    {
        public string Method { get; init; } = string.Empty;

        public Uri Uri { get; init; } = null!;

        public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string? Body { get; init; }

        public string PathAndQuery => Uri.PathAndQuery;

        public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

        public JsonElement Json() => JsonDocument.Parse(Body ?? "null").RootElement.Clone();
    }

    public delegate Task<HttpResponseMessage> Step(Seen request, CancellationToken cancellationToken);

    /// <summary>
    /// The API, as a scripted <see cref="HttpMessageHandler"/>: each call takes the next step. No network.
    /// </summary>
    public sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Step> _script = new Queue<Step>();
        private readonly object _gate = new object();

        public List<Seen> Requests { get; } = new List<Seen>();

        public int Count => Requests.Count;

        public StubHandler Then(Step step)
        {
            lock (_gate) _script.Enqueue(step);
            return this;
        }

        public StubHandler Then(Func<HttpResponseMessage> response) => Then((_, _) => Task.FromResult(response()));

        public StubHandler Then(HttpResponseMessage response) => Then(() => response);

        public StubHandler ThenThrow(Exception error) => Then((_, _) => throw error);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // User-Agent products go on the wire separated by spaces; other headers' values by commas.
            foreach (var h in request.Headers) headers[h.Key] = string.Join(h.Key == "User-Agent" ? " " : ", ", h.Value);
            string? body = null;
            if (request.Content != null)
            {
                foreach (var h in request.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);
                body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            var seen = new Seen { Method = request.Method.Method, Uri = request.RequestUri!, Headers = headers, Body = body };
            Step step;
            lock (_gate)
            {
                Requests.Add(seen);
                if (_script.Count == 0) throw new InvalidOperationException("The stub has no step left for " + seen.Method + " " + seen.Uri);
                step = _script.Dequeue();
            }
            return await step(seen, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Answers the API gives.</summary>
    public static class Reply
    {
        public static HttpResponseMessage Raw(HttpStatusCode status, string body, string contentType = "application/json", params (string Name, string Value)[] headers)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
            foreach (var (name, value) in headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, value)) response.Content.Headers.TryAddWithoutValidation(name, value);
            }
            return response;
        }

        public static HttpResponseMessage Ok(string dataJson, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers) =>
            Raw(status, "{\"data\":" + dataJson + "}", headers: headers);

        public static HttpResponseMessage Page(string itemsJson, int page, int pageSize, int total) =>
            Raw(HttpStatusCode.OK, "{\"data\":" + itemsJson + ",\"meta\":{\"page\":" + page + ",\"pageSize\":" + pageSize + ",\"total\":" + total + "}}");

        public static HttpResponseMessage Error(int status, string code, string message = "Bir şey oldu", string requestId = "req_1", string? detailsJson = null, params (string Name, string Value)[] headers)
        {
            var details = detailsJson == null ? string.Empty : ",\"details\":" + detailsJson;
            var body = "{\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\",\"requestId\":\"" + requestId + "\",\"status\":" + status + ",\"docs\":\"https://rewloy.com/gelistiriciler/hatalar#" + code + "\"" + details + "}}";
            return Raw((HttpStatusCode)status, body, headers: headers);
        }

        public static HttpResponseMessage NoContent() => new HttpResponseMessage(HttpStatusCode.NoContent);

        public static HttpResponseMessage Sse(Stream body, params (string Name, string Value)[] headers)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "text/event-stream");
            foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
            return response;
        }
    }

    /// <summary>Waits that do not wait: they are recorded.</summary>
    public sealed class Sleeps
    {
        public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

        public Task Delay(TimeSpan delay, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (Delays) Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    public static class Clients
    {
        public const string Key = "rwk_test_abcd1234_secretsecretsecret";
        public const string Staff = "rws_sessionsessionsession";
        public const string Holder = "rwh_holderholderholder";
        public static readonly Guid MerchantA = Guid.Parse("0192f7c1-0000-7000-8000-00000000000a");
        public static readonly Guid MerchantB = Guid.Parse("0192f7c1-0000-7000-8000-00000000000b");

        public static RewloyClient Make(StubHandler handler, Sleeps? sleeps = null, Action<RewloyClientOptions>? configure = null)
        {
            var options = new RewloyClientOptions
            {
                ApiKey = Key,
                BaseUrl = "https://api.test",
                HttpClient = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan },
                Delay = (sleeps ?? new Sleeps()).Delay,
            };
            configure?.Invoke(options);
            return new RewloyClient(options);
        }
    }
}
