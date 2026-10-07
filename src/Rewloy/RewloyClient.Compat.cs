#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Rewloy.Models;

namespace Rewloy
{
    // Source compatibility with 0.2.4. API 1.3.0 gave `programJoinQr` a query (`branchCode`, `format`), so the generated
    // method now has a `query` parameter in the position `options` had. These overloads keep a call that passed the
    // options positionally, `ProgramJoinQrAsync(id, options)`, compiling and doing exactly what it did. `options` is
    // not optional here so that `ProgramJoinQrAsync(id)` still resolves to the generated method.
    public sealed partial class RewloyClient
    {
        /// <summary>Katılım QR kodu, 0.2.4's signature (no query): the program's own join address as SVG.</summary>
        public Task<RewloyFile> ProgramJoinQrAsync(Guid id, RequestOptions options, CancellationToken cancellationToken = default)
            => ProgramJoinQrAsync(id, null, options, cancellationToken);

        /// <summary>Katılım QR kodu (the whole answer), 0.2.4's signature (no query).</summary>
        public Task<RewloyResponse<RewloyFile>> ProgramJoinQrWithResponseAsync(Guid id, RequestOptions options, CancellationToken cancellationToken = default)
            => ProgramJoinQrWithResponseAsync(id, null, options, cancellationToken);
    }
}
