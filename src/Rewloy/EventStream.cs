using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rewloy
{
    /// <summary>
    /// A live stream of server-sent events: iterate it with <c>await foreach</c>. It ends when the cancellation
    /// token cancels, on <c>break</c>, on <see cref="Close"/>, or with a <see cref="RewloyException"/> that a
    /// reconnection cannot fix (401, 403, 404...); with <see cref="RequestOptions.Reconnect"/> set to
    /// <c>false</c> also when the connection ends. It can be iterated once.
    /// <para>
    /// While it runs, it reconnects as a browser's <c>EventSource</c> does: after the server's <c>retry:</c> delay
    /// (5 seconds on Rewloy), with backoff up to 30 seconds while connecting fails, sending <c>Last-Event-ID</c>
    /// once an event carried an id. A connection that sends no byte for the idle timeout (60 seconds; the API
    /// sends a heartbeat every 25) counts as dropped.
    /// </para>
    /// </summary>
    public sealed class EventStream : IAsyncEnumerable<ServerSentEvent>, IAsyncDisposable
    {
        private const int DefaultRetryMs = 3000;
        private static readonly TimeSpan MaxReconnect = TimeSpan.FromSeconds(30);

        private readonly Source _source;
        private readonly CancellationTokenSource _close = new CancellationTokenSource();
        private int _started;

        /// <summary>How a stream asks the client for a connection.</summary>
        internal sealed class Source
        {
            public Source(string operation, Func<string, CancellationToken, Task<HttpResponseMessage>> connect, bool reconnect, TimeSpan idleTimeout, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token)
            {
                Operation = operation;
                Connect = connect;
                Reconnect = reconnect;
                IdleTimeout = idleTimeout;
                Delay = delay;
                Token = token;
            }

            public string Operation { get; }

            /// <summary>Opens one connection (retrying inside): resolves with the answer once its headers are in.</summary>
            public Func<string, CancellationToken, Task<HttpResponseMessage>> Connect { get; }

            public bool Reconnect { get; }

            public TimeSpan IdleTimeout { get; }

            public Func<TimeSpan, CancellationToken, Task> Delay { get; }

            public CancellationToken Token { get; }
        }

        internal EventStream(Source source)
        {
            _source = source;
        }

        /// <summary>The last event ID seen; sent as <c>Last-Event-ID</c> when reconnecting.</summary>
        public string LastEventId { get; private set; } = string.Empty;

        /// <summary>The wait before reconnecting: the server's <c>retry:</c> once it sent one.</summary>
        public TimeSpan RetryDelay { get; private set; } = TimeSpan.FromMilliseconds(DefaultRetryMs);

        /// <summary><c>x-request-id</c> of the current connection.</summary>
        public string? RequestId { get; private set; }

        /// <summary><c>Rewloy-Mode</c> of the current connection (see <see cref="RewloyResponse.Mode"/>).</summary>
        public string? Mode { get; private set; }

        /// <summary>Closes the connection and ends the iteration.</summary>
        public void Close() => _close.Cancel();

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Close();
            return default;
        }

        /// <inheritdoc />
        public IAsyncEnumerator<ServerSentEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("An EventStream can be iterated once.");
            return RunAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        }

        /// <summary>Errors a new connection may fix.</summary>
        private static bool IsTransient(Exception err) =>
            err is RewloyConnectionException || (err is RewloyException e && (e.Status == 429 || Retry.IsGatewayStatus(e.Status)));

        private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken stop)
        {
            try
            {
                await _source.Delay(delay, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            return !stop.IsCancellationRequested;
        }

        private TimeSpan ReconnectDelay(int failures) =>
            TimeSpan.FromMilliseconds(Math.Max(RetryDelay.TotalMilliseconds, Math.Min(MaxReconnect.TotalMilliseconds, 1000.0 * Math.Pow(2, Math.Min(failures, 16)))));

        private async IAsyncEnumerable<ServerSentEvent> RunAsync([EnumeratorCancellation] CancellationToken enumeratorToken)
        {
            var s = _source;
            using (var stopSource = CancellationTokenSource.CreateLinkedTokenSource(s.Token, _close.Token, enumeratorToken))
            {
                var stop = stopSource.Token;
                var failures = 0;
                var idle = s.IdleTimeout > TimeSpan.Zero && s.IdleTimeout != System.Threading.Timeout.InfiniteTimeSpan ? s.IdleTimeout : (TimeSpan?)null;
                var buffer = new byte[8192];
                while (!stop.IsCancellationRequested)
                {
                    HttpResponseMessage? response = null;
                    Exception? connectError = null;
                    try
                    {
                        response = await s.Connect(LastEventId, stop).ConfigureAwait(false);
                    }
                    catch (Exception err)
                    {
                        connectError = err;
                    }
                    if (connectError != null)
                    {
                        if (stop.IsCancellationRequested) yield break;
                        if (!s.Reconnect || !IsTransient(connectError))
                        {
                            ExceptionDispatchInfo.Capture(connectError).Throw();
                        }
                        failures++;
                        if (!await WaitAsync(ReconnectDelay(failures), stop).ConfigureAwait(false)) yield break;
                        continue;
                    }

                    RequestId = response!.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
                    Mode = response.Headers.TryGetValues("rewloy-mode", out var modes) ? modes.FirstOrDefault() : null;

                    var parser = new SseParser(LastEventId);
                    var decoder = Encoding.UTF8.GetDecoder();
                    var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
                    RewloyException? dropped = null;
                    using (var connection = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    using (var silence = new CancellationTokenSource())
                    using (var reading = CancellationTokenSource.CreateLinkedTokenSource(connection.Token, silence.Token))
                    using (reading.Token.Register(() => response.Dispose()))
                    {
                        try
                        {
                            Stream? body = null;
                            Exception? openError = null;
                            try
                            {
                                if (idle.HasValue) silence.CancelAfter(idle.Value);
#if NET5_0_OR_GREATER
                                body = await response.Content.ReadAsStreamAsync(reading.Token).ConfigureAwait(false);
#else
                                body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                            }
                            catch (Exception err)
                            {
                                openError = err;
                            }
                            if (openError != null)
                            {
                                if (stop.IsCancellationRequested) yield break;
                                dropped = Dropped(openError, silence, s, idle);
                            }

                            while (dropped == null)
                            {
                                var read = 0;
                                Exception? readError = null;
                                try
                                {
                                    read = await body!.ReadAsync(buffer, 0, buffer.Length, reading.Token).ConfigureAwait(false);
                                }
                                catch (Exception err)
                                {
                                    readError = err;
                                }
                                if (readError != null)
                                {
                                    if (stop.IsCancellationRequested) yield break;
                                    dropped = Dropped(readError, silence, s, idle);
                                    break;
                                }

                                var done = read == 0;
                                var count = done ? decoder.GetChars(buffer, 0, 0, chars, 0, flush: true) : decoder.GetChars(buffer, 0, read, chars, 0, flush: false);
                                if (!done && idle.HasValue) silence.CancelAfter(idle.Value);
                                foreach (var ev in parser.Push(new string(chars, 0, count)))
                                {
                                    LastEventId = ev.Id;
                                    failures = 0;
                                    yield return ev;
                                }
                                LastEventId = parser.LastEventId;
                                if (parser.RetryMilliseconds.HasValue) RetryDelay = TimeSpan.FromMilliseconds(parser.RetryMilliseconds.Value);
                                if (done)
                                {
                                    parser.End();
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            connection.Cancel();
                            response.Dispose();
                        }
                    }

                    if (!s.Reconnect)
                    {
                        if (dropped != null) throw dropped;
                        yield break;
                    }
                    if (dropped != null) failures++;
                    var delay = dropped != null ? ReconnectDelay(failures) : RetryDelay;
                    if (!await WaitAsync(delay, stop).ConfigureAwait(false)) yield break;
                }
            }
        }

        private RewloyException Dropped(Exception err, CancellationTokenSource silence, Source s, TimeSpan? idle) =>
            silence.IsCancellationRequested && idle.HasValue
                ? new RewloyTimeoutException("no data for " + (long)idle.Value.TotalMilliseconds + " ms", s.Operation, RequestId)
                : new RewloyConnectionException(err.Message, s.Operation, RequestId, err);
    }
}
