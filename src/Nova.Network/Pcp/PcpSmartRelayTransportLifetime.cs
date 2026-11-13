using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpSmartRelayTransportLifetime(TcpTransportConnection connection, PcpSmartRelayOptions options,
    Action cleanupFailed) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        // Even a synchronous disposal callback cannot keep the recovery loop
        // blocked forever. A timeout faults this Owner; it never starts another
        // attempt while this actual cleanup is outstanding.
        var cleanup = Task.Run(async () =>
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            await connection.CleanupCompletion.ConfigureAwait(false);
        });
        try { await cleanup.WaitAsync(options.CleanupTimeout, options.TimeProvider, CancellationToken.None).ConfigureAwait(false); }
        catch { cleanupFailed(); throw; }
    }
}
