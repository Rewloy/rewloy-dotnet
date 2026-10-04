using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rewloy.Tests.Support
{
    /// <summary>
    /// A response body that arrives as scripted: each step is a chunk of bytes (or text), an error to throw, or
    /// <see cref="Hang"/> to stay silent until cancelled. After the last step the stream ends.
    /// </summary>
    public sealed class ChunkStream : Stream
    {
        public sealed class HangStep
        {
        }

        public static readonly HangStep Hang = new HangStep();

        private readonly object[] _steps;
        private int _next;
        private byte[]? _current;
        private int _offset;

        public ChunkStream(params object[] steps)
        {
            _steps = steps.Select(s => s is string text ? Encoding.UTF8.GetBytes(text) : s).ToArray();
        }

        public bool Disposed { get; private set; }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Disposed) throw new ObjectDisposedException(nameof(ChunkStream));
            while (_current == null)
            {
                if (_next >= _steps.Length) return 0;
                var step = _steps[_next++];
                switch (step)
                {
                    case byte[] bytes: _current = bytes; _offset = 0; break;
                    case Exception error: throw error;
                    case HangStep:
                        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                        break;
                    default: throw new InvalidOperationException("unknown step");
                }
            }
            var n = Math.Min(count, _current.Length - _offset);
            Buffer.BlockCopy(_current, _offset, buffer, offset, n);
            _offset += n;
            if (_offset >= _current.Length) _current = null;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
