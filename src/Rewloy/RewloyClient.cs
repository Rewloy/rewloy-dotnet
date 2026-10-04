using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Rewloy.Models;

namespace Rewloy
{
    /// <summary>
    /// A client of the Rewloy API (<c>https://app.rewloy.com/v1</c>).
    /// <code>
    /// using var rewloy = new RewloyClient(new RewloyClientOptions { ApiKey = Environment.GetEnvironmentVariable("REWLOY_API_KEY") });
    /// var card = await rewloy.GetPassAsync("ABCD-EFGH-JKLM");
    /// </code>
    /// <para>
    /// Every operation of the API is a method named by its operationId, with <c>Async</c> after it (the generated
    /// half of this class). Its arguments are the path parameters, then the body and the query the operation takes,
    /// then <see cref="RequestOptions"/> and a <see cref="CancellationToken"/>. A method gives the answer's data;
    /// <c>…WithResponseAsync</c> gives the whole answer. Paged lists have <c>…AllAsync</c>, which walks every page.
    /// </para>
    /// <para>
    /// The client is safe to share between threads and meant to live as long as your program: make one and
    /// reuse it (or hand it an <see cref="HttpClient"/> from <c>IHttpClientFactory</c>).
    /// </para>
    /// </summary>
    public sealed partial class RewloyClient : IDisposable
    {
        /// <summary>The API's origin when none is given.</summary>
        public const string DefaultBaseUrl = "https://app.rewloy.com";

        private const int DefaultMaxRetries = 2;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Operations already warned about: one warning per operation per process.</summary>
        private static readonly ConcurrentDictionary<string, bool> Warned = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string? _token;
        private readonly string _userAgent;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        /// <summary>
        /// Raised once per deprecated operation per process, when an answer carries the <c>Deprecation</c> header
        /// (whatever the number of clients). Without a subscriber the warning goes to
        /// <see cref="System.Diagnostics.Trace.TraceWarning(string)"/> only. A subscriber that throws does not make
        /// the call fail: the server has already acted, and losing the answer would be worse than losing the notice.
        /// <code>
        /// RewloyClient.Deprecated += (_, e) => logger.LogWarning("{Message}", e.Message);
        /// </code>
        /// </summary>
        public static event EventHandler<DeprecationEventArgs>? Deprecated;

        /// <summary>Makes a client from options: one credential, or none for the endpoints that need none.</summary>
        /// <exception cref="ArgumentException">More than one credential, a credential of the wrong form, or a merchant without a staff session.</exception>
        public RewloyClient(RewloyClientOptions? options = null)
        {
            options ??= new RewloyClientOptions();
            var given = new List<string>();
            if (options.ApiKey != null) given.Add(nameof(RewloyClientOptions.ApiKey));
            if (options.StaffSession != null) given.Add(nameof(RewloyClientOptions.StaffSession));
            if (options.HolderSession != null) given.Add(nameof(RewloyClientOptions.HolderSession));
            if (given.Count > 1) throw new ArgumentException("Give one credential, not " + string.Join(" and ", given) + ".", nameof(options));

            if (options.ApiKey != null)
            {
                RequirePrefix(options.ApiKey, "rwk_", nameof(RewloyClientOptions.ApiKey));
                _token = options.ApiKey;
                Credential = CredentialKinds.Key;
            }
            else if (options.StaffSession != null)
            {
                RequirePrefix(options.StaffSession, "rws_", nameof(RewloyClientOptions.StaffSession));
                _token = options.StaffSession;
                Credential = CredentialKinds.Staff;
            }
            else if (options.HolderSession != null)
            {
                RequirePrefix(options.HolderSession, "rwh_", nameof(RewloyClientOptions.HolderSession));
                _token = options.HolderSession;
                Credential = CredentialKinds.Holder;
            }
            if (options.Merchant != null && Credential != CredentialKinds.Staff) throw new ArgumentException("Merchant goes with a StaffSession.", nameof(options));
            if (options.MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries cannot be negative.");

            Merchant = options.Merchant;
            var baseUrl = (options.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("BaseUrl must be an http or https address.", nameof(options));
            }
            BaseUrl = baseUrl;
            Timeout = options.Timeout;
            MaxRetries = options.MaxRetries;
            _delay = options.Delay ?? ((delay, token) => Task.Delay(delay, token));
            _userAgent = BuildUserAgent(options.UserAgent);

            if (options.HttpClient != null)
            {
                _http = options.HttpClient;
            }
            else
            {
#if NET5_0_OR_GREATER
                var handler = new SocketsHttpHandler
                {
                    // Redirects are not followed: the API does not redirect, and a redirect could carry the token elsewhere.
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                };
#else
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                };
#endif
                // The client times each attempt itself, so that a timeout is a RewloyTimeoutException and can be retried.
                _http = new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                _ownsHttp = true;
            }
        }

        private static void RequirePrefix(string token, string prefix, string option)
        {
            // The message never carries the value: a mistyped credential is still a credential.
            if (!token.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException(option + " must start with \"" + prefix + "\".", "options");
        }

        /// <summary>The API's origin, without <c>/v1</c> and without a trailing slash.</summary>
        public string BaseUrl { get; }

        /// <summary>Time allowed for one attempt.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>Retries after a failed attempt, when retrying is safe.</summary>
        public int MaxRetries { get; }

        /// <summary>The kind of credential this client sends: <see cref="CredentialKinds.None"/> when it has none.</summary>
        public CredentialKinds Credential { get; }

        /// <summary>The default <c>Rewloy-Merchant</c> of a staff session.</summary>
        public Guid? Merchant { get; }

        /// <inheritdoc />
        public override string ToString() => "RewloyClient(" + BaseUrl + ", " + Credential + ")";

        /// <summary>Releases the <see cref="HttpClient"/> the client made itself; one passed in is left alone.</summary>
        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ------------------------------------------------------------------ what the generated half calls

        private static string PathValue(string value) => value;

        private static string PathValue(Guid value) => value.ToString("D");

        private static string PathValue(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string PathValue(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>One answer read: the status, the headers and the body.</summary>
        private sealed class Reply
        {
            public Reply(int status, RewloyHeaders headers, byte[] body)
            {
                Status = status;
                Headers = headers;
                Body = body;
            }

            public int Status { get; }

            public RewloyHeaders Headers { get; }

            public byte[] Body { get; }
        }

        private sealed class StreamLink
        {
            public StreamLink(string lastEventId, CancellationToken stop)
            {
                LastEventId = lastEventId;
                Stop = stop;
            }

            public string LastEventId { get; }

            public CancellationToken Stop { get; }
        }

        /// <summary>For a stream the answer is left open (<see cref="Response"/>); otherwise it is read (<see cref="Reply"/>).</summary>
        private sealed class Exchange
        {
            public HttpResponseMessage? Response { get; set; }

            public Reply? Reply { get; set; }
        }

        private async Task<RewloyResponse<T>> InvokeAsync<T>(OperationInfo op, string[] pathValues, RewloyQuery? query, object? body, RequestOptions? options, CancellationToken cancellationToken)
        {
            var reply = (await ExchangeAsync(op, pathValues, query, body, options, cancellationToken, null).ConfigureAwait(false)).Reply!;
            if (reply.Status == 204)
            {
                return new RewloyResponse<T>(reply.Status, reply.Headers, default!, null);
            }
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(reply.Body);
            }
            catch (JsonException)
            {
                throw Invalid(op, reply, "the answer is not the JSON the API documents (" + (reply.Headers.Get("content-type") ?? "no content type") + ")");
            }
            using (doc)
            {
                var root = doc.RootElement;
                try
                {
                    if (op.Response == ResponseKind.RawJson) return new RewloyResponse<T>(reply.Status, reply.Headers, root.Deserialize<T>(RewloyJson.Options)!, null);
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data))
                    {
                        throw Invalid(op, reply, "the answer is not the JSON the API documents (no data)");
                    }
                    PageMeta? meta = root.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object ? m.Deserialize<PageMeta>(RewloyJson.Options) : null;
                    return new RewloyResponse<T>(reply.Status, reply.Headers, data.Deserialize<T>(RewloyJson.Options)!, meta);
                }
                catch (JsonException ex)
                {
                    throw Invalid(op, reply, "the answer does not fit the documented type: " + ex.Message, ex);
                }
            }
        }

        private async Task<RewloyResponse> InvokeNoContentAsync(OperationInfo op, string[] pathValues, RewloyQuery? query, object? body, RequestOptions? options, CancellationToken cancellationToken)
        {
            var reply = (await ExchangeAsync(op, pathValues, query, body, options, cancellationToken, null).ConfigureAwait(false)).Reply!;
            return new RewloyResponse(reply.Status, reply.Headers);
        }

        private async Task<RewloyResponse<RewloyFile>> InvokeFileAsync(OperationInfo op, string[] pathValues, RewloyQuery? query, object? body, RequestOptions? options, CancellationToken cancellationToken)
        {
            var reply = (await ExchangeAsync(op, pathValues, query, body, options, cancellationToken, null).ConfigureAwait(false)).Reply!;
            var file = new RewloyFile(reply.Body, reply.Headers.Get("content-type"), FileName(reply.Headers.Get("content-disposition")));
            return new RewloyResponse<RewloyFile>(reply.Status, reply.Headers, file, null);
        }

        private static string? FileName(string? disposition)
        {
            if (disposition == null) return null;
            var m = Regex.Match(disposition, "filename\\*?=(?:UTF-8'')?\"?([^\";]+)\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) return null;
            try
            {
                return Uri.UnescapeDataString(m.Groups[1].Value.Trim());
            }
            catch (UriFormatException)
            {
                return m.Groups[1].Value.Trim();
            }
        }

        private EventStream OpenStream(OperationInfo op, string[] pathValues, RewloyQuery? query, RequestOptions? options, CancellationToken cancellationToken)
        {
            var idle = options?.IdleTimeout ?? DefaultIdleTimeout;
            return new EventStream(new EventStream.Source(
                op.Id,
                async (lastEventId, stop) =>
                {
                    var exchange = await ExchangeAsync(op, pathValues, query, null, options, cancellationToken, new StreamLink(lastEventId, stop)).ConfigureAwait(false);
                    return exchange.Response!;
                },
                options?.Reconnect ?? true,
                idle,
                _delay,
                cancellationToken));
        }

        /// <summary>
        /// Walks a paged list item by item, asking for the next page while <c>meta</c> says there is one.
        /// </summary>
        private static async IAsyncEnumerable<T> PaginateAsync<T>(
            long startPage, Func<long, CancellationToken, Task<RewloyResponse<IReadOnlyList<T>>>> fetch, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var page = startPage;
            for (; ; )
            {
                var response = await fetch(page, cancellationToken).ConfigureAwait(false);
                var items = response.Data ?? Array.Empty<T>();
                foreach (var item in items) yield return item;
                var meta = response.Meta;
                if (meta == null || items.Count == 0 || items.Count < meta.PageSize || meta.Page * meta.PageSize >= meta.Total) yield break;
                page = meta.Page + 1;
            }
        }

        // ------------------------------------------------------------------ the request

        private string BuildUrl(OperationInfo op, string[] pathValues, RewloyQuery? query)
        {
            var i = 0;
            var path = Regex.Replace(op.Path, "\\{([^}]+)\\}", match =>
            {
                var name = match.Groups[1].Value;
                var value = i < pathValues.Length ? pathValues[i] : null;
                i++;
                if (string.IsNullOrEmpty(value)) throw new ArgumentException(op.Id + " needs " + name + ".", name);
                return Uri.EscapeDataString(value);
            });
            string qs = string.Empty;
            if (query != null)
            {
                var writer = new QueryWriter();
                query.WriteTo(writer);
                qs = writer.ToQueryString();
            }
            return BaseUrl + path + (qs.Length > 0 ? "?" + qs : string.Empty);
        }

        private HttpRequestMessage BuildRequest(OperationInfo op, string url, byte[]? body, string? idempotencyKey, RequestOptions? options, string? lastEventId)
        {
            var request = new HttpRequestMessage(new HttpMethod(op.Method), url);
            var h = request.Headers;
            h.TryAddWithoutValidation("Accept", op.IsStream ? "text/event-stream" : op.Response == ResponseKind.Json || op.Response == ResponseKind.RawJson ? "application/json" : "*/*");
            h.TryAddWithoutValidation("User-Agent", _userAgent);
            if (op.IsStream)
            {
                // As a browser's EventSource asks: no cache, and no compression, which could hold events back.
                h.TryAddWithoutValidation("Cache-Control", "no-cache");
                h.TryAddWithoutValidation("Accept-Encoding", "identity");
            }
            // An operation that takes no credential of this kind but works without one is called without it:
            // the API refuses a credential an operation does not accept (CREDENTIAL_NOT_ALLOWED).
            if (_token != null && Credential != CredentialKinds.None && ((op.Credentials & Credential) != 0 || (op.Credentials & CredentialKinds.Public) == 0))
            {
                h.TryAddWithoutValidation("Authorization", "Bearer " + _token);
            }
            var merchant = options?.Merchant ?? (Credential == CredentialKinds.Staff ? Merchant : null);
            if (op.AcceptsMerchant && merchant != null) h.TryAddWithoutValidation("Rewloy-Merchant", merchant.Value.ToString("D"));
            if (idempotencyKey != null) h.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            if (body != null)
            {
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                request.Content = content;
            }
            if (!string.IsNullOrEmpty(lastEventId)) h.TryAddWithoutValidation("Last-Event-ID", lastEventId);
            if (options?.Headers != null)
            {
                foreach (var pair in options.Headers)
                {
                    if (pair.Value == null) continue;
                    h.Remove(pair.Key);
                    if (!h.TryAddWithoutValidation(pair.Key, pair.Value) && request.Content != null)
                    {
                        request.Content.Headers.Remove(pair.Key);
                        request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                    }
                }
            }
            return request;
        }

        private static string BuildUserAgent(string? suffix)
        {
            var runtime = RuntimeInformation.FrameworkDescription; // ".NET 8.0.11", ".NET Framework 4.8.9300.0"
            string product;
            if (runtime.StartsWith(".NET Framework ", StringComparison.Ordinal)) product = "netfx/" + runtime.Substring(".NET Framework ".Length);
            else if (runtime.StartsWith(".NET Core ", StringComparison.Ordinal)) product = "netcore/" + runtime.Substring(".NET Core ".Length);
            else if (runtime.StartsWith(".NET ", StringComparison.Ordinal)) product = "dotnet/" + runtime.Substring(".NET ".Length);
            else product = "dotnet/" + Environment.Version;
            product = product.Replace(' ', '-');
            return string.Join(" ", new[] { "rewloy-dotnet/" + RewloyVersion.Current, product, suffix?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
        }

        /// <summary>
        /// One call: attempts until an answer settles it. For a stream it resolves once the headers are in, the
        /// body unread; otherwise with the body read.
        /// </summary>
        private async Task<Exchange> ExchangeAsync(OperationInfo op, string[] pathValues, RewloyQuery? query, object? body, RequestOptions? options, CancellationToken cancellationToken, StreamLink? stream)
        {
            var url = BuildUrl(op, pathValues, query);
            byte[]? json = null;
            if (op.HasBody)
            {
                json = body == null ? Encoding.UTF8.GetBytes("{}") : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), RewloyJson.Options);
            }
            // Generated once: every retry of this call sends the same key.
            var idempotencyKey = op.Idempotency != IdempotencyMode.None ? options?.IdempotencyKey ?? Guid.NewGuid().ToString("D") : null;
            var retryable = Retry.IsSafeMethod(op.Method) || idempotencyKey != null
                || (options?.Headers != null && options.Headers.Keys.Any(k => string.Equals(k, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)));
            var maxRetries = Math.Max(0, options?.MaxRetries ?? MaxRetries);
            var timeout = options?.Timeout ?? Timeout;
            var timed = timeout > TimeSpan.Zero && timeout.TotalMilliseconds < int.MaxValue;

            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RewloyException failure;
                TimeSpan? wait = null;
                HttpResponseMessage? response = null;
                var keep = false;
                using (var timeoutSource = new CancellationTokenSource())
                using (var linked = stream == null
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token)
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token, stream.Stop))
                {
                    if (timed) timeoutSource.CancelAfter(timeout);
                    var token = linked.Token;
                    try
                    {
                        using (var request = BuildRequest(op, url, json, idempotencyKey, options, stream?.LastEventId))
                        {
                            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                        }
                        Notice(op, response);
                        var status = (int)response.StatusCode;
                        if (status >= 200 && status < 300)
                        {
                            if (stream != null)
                            {
                                keep = true;
                                return new Exchange { Response = response };
                            }
                            return new Exchange { Reply = await ReadAsync(response, token).ConfigureAwait(false) };
                        }
                        var text = await ReadTextAsync(response, token).ConfigureAwait(false);
                        failure = Failure(op, response, text);
                        var retryAfter = Retry.ParseRetryAfter(HeaderValue(response, "Retry-After"));
                        if (!retryable || attempt >= maxRetries || !Retry.IsRetryableStatus(status, failure.Code)) throw failure;
                        wait = retryAfter;
                    }
                    catch (RewloyException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // The caller's own cancellation is theirs: an OperationCanceledException, not a Rewloy failure.
                        cancellationToken.ThrowIfCancellationRequested();
                        if (stream != null && stream.Stop.IsCancellationRequested) throw;
                        var timedOut = timeoutSource.IsCancellationRequested || ex is OperationCanceledException;
                        failure = timedOut
                            ? new RewloyTimeoutException(timed ? "no answer within " + (long)timeout.TotalMilliseconds + " ms" : "the request timed out", op.Id, null, ex)
                            : new RewloyConnectionException(Describe(ex), op.Id, null, ex);
                        if (!retryable || attempt >= maxRetries) throw failure;
                    }
                    finally
                    {
                        if (!keep) response?.Dispose();
                    }
                }
                var delay = wait ?? Retry.Backoff(attempt);
                if (delay > Retry.MaxRetryAfter) throw failure;
                await _delay(delay, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static string? HeaderValue(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

        private static string Describe(Exception ex)
        {
            var inner = ex.InnerException;
            return inner != null && inner.Message != ex.Message ? ex.Message + ": " + inner.Message : ex.Message;
        }

        private static async Task<Reply> ReadAsync(HttpResponseMessage response, CancellationToken token)
        {
            var bytes = await ReadBytesAsync(response, token).ConfigureAwait(false);
            return new Reply((int)response.StatusCode, RewloyHeaders.From(response), bytes);
        }

        private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, CancellationToken token)
        {
#if NET5_0_OR_GREATER
            return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
#else
            // Older stacks do not honour the token while reading: disposing the answer breaks the read when it cancels.
            using (token.Register(() => response.Dispose()))
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory, 81920, token).ConfigureAwait(false);
                return memory.ToArray();
            }
#endif
        }

        private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken token)
        {
            try
            {
                return Encoding.UTF8.GetString(await ReadBytesAsync(response, token).ConfigureAwait(false));
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                return string.Empty;
            }
        }

        private static RewloyException Invalid(OperationInfo op, Reply reply, string detail, Exception? inner = null) =>
            new RewloyException(reply.Status, "INVALID_RESPONSE", detail, requestId: reply.Headers.Get("x-request-id"), body: Encoding.UTF8.GetString(reply.Body), headers: reply.Headers, operation: op.Id, innerException: inner);

        /// <summary>The API's error body (or a proxy's page) as the exception it is.</summary>
        private static RewloyException Failure(OperationInfo op, HttpResponseMessage response, string text)
        {
            var status = (int)response.StatusCode;
            var headers = RewloyHeaders.From(response);
            string? code = null, message = null, docs = null, bodyRequestId = null;
            JsonElement? details = null;
            try
            {
                using (var doc = JsonDocument.Parse(text))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
                    {
                        code = e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        message = e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                        docs = e.TryGetProperty("docs", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                        bodyRequestId = e.TryGetProperty("requestId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                        if (e.TryGetProperty("details", out var x)) details = x.Clone();
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON: a proxy's page.
            }
            code ??= "HTTP_" + status.ToString(CultureInfo.InvariantCulture);
            var detail = message ?? (string.IsNullOrEmpty(response.ReasonPhrase) ? "HTTP " + status.ToString(CultureInfo.InvariantCulture) : response.ReasonPhrase!);
            var title = ErrorCode.Title(code);
            var requestId = headers.Get("x-request-id") ?? bodyRequestId;
            if (status == 429)
            {
                var header = Retry.ParseRetryAfter(headers.Get("retry-after"));
                TimeSpan? retryAfter = header.HasValue
                    ? TimeSpan.FromSeconds(Math.Ceiling(header.Value.TotalSeconds))
                    : details is { ValueKind: JsonValueKind.Object } d2 && d2.TryGetProperty("retryAfterSec", out var sec) && sec.ValueKind == JsonValueKind.Number
                        ? TimeSpan.FromSeconds(sec.GetDouble())
                        : (TimeSpan?)null;
                return new RateLimitException(status, code, detail, retryAfter, title, details, docs, requestId, text, headers, op.Id);
            }
            return new RewloyException(status, code, detail, title, details, docs, requestId, text, headers, op.Id);
        }

        private static void Notice(OperationInfo op, HttpResponseMessage response)
        {
            if (!response.Headers.Contains("Deprecation") || !Warned.TryAdd(op.Id, true)) return;
            var sunset = HeaderValue(response, "Sunset");
            var link = DeprecationLink(response.Headers.TryGetValues("Link", out var links) ? string.Join(", ", links) : null);
            var message = "Rewloy API operation " + op.Id + " (" + op.Method + " " + op.Path + ") is deprecated."
                + (sunset != null ? " Sunset: " + sunset + "." : string.Empty)
                + (link != null ? " See " + link : string.Empty);
            Trace.TraceWarning(message);
            try
            {
                Deprecated?.Invoke(null, new DeprecationEventArgs(op, sunset, link, message));
            }
            catch (Exception)
            {
                // A subscriber's failure must not fail an answered call.
            }
        }

        /// <summary>The URL a <c>Link</c> header gives for <c>rel="deprecation"</c> (else its first).</summary>
        private static string? DeprecationLink(string? link)
        {
            if (string.IsNullOrEmpty(link)) return null;
            string? first = null;
            foreach (Match m in Regex.Matches(link!, "<([^>]*)>([^,]*)"))
            {
                first ??= m.Groups[1].Value;
                if (Regex.IsMatch(m.Groups[2].Value, "\\brel\\s*=\\s*\"?[^\";]*\\bdeprecation\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return m.Groups[1].Value;
            }
            return first;
        }

        /// <summary>For tests: forget which operations were warned about.</summary>
        internal static void ResetDeprecationWarnings() => Warned.Clear();
    }
}
