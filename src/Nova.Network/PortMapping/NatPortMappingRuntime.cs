namespace PeerCast.Nova.Network.PortMapping;

/// <summary>Owns network monitoring and the automatic renew/reconcile pump.</summary>
public sealed class NatPortMappingRuntime : IAsyncDisposable
{
    private readonly object networkGate = new();
    private readonly INatNetworkSource networkSource;
    private readonly bool ownsNetworkSource;
    private readonly CancellationTokenSource stop = new();
    private readonly Task pump;
    private int disposed;

    public NatPortMappingRuntime(
        NatPortMappingOptions? options = null,
        INatMappingBackend? backend = null,
        INatNetworkSource? networkSource = null,
        TimeProvider? timeProvider = null)
    {
        Manager = new NatPortMappingManager(backend ?? new NatPmpMappingBackend(), options, timeProvider);
        ownsNetworkSource = networkSource is null;
        this.networkSource = networkSource ?? new SystemNatNetworkSource();
        if (this.networkSource is SystemNatNetworkSource system)
        {
            system.Changed += InvalidateNetwork;
        }

        pump = PumpAsync(timeProvider ?? TimeProvider.System);
    }

    public NatPortMappingManager Manager { get; }

    /// <summary>The reconciler's result, available after runtime cleanup completes.</summary>
    public NatShutdownOutcome? ShutdownOutcome { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var shutdown = Manager.ShutdownAsync();
        stop.Cancel();
        try
        {
            await pump.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                ShutdownOutcome = await shutdown.ConfigureAwait(false);
            }
            finally
            {
                if (networkSource is SystemNatNetworkSource system)
                {
                    system.Changed -= InvalidateNetwork;
                    if (ownsNetworkSource)
                    {
                        system.Dispose();
                    }
                }

                stop.Dispose();
            }
        }
    }

    private async Task PumpAsync(TimeProvider clock)
    {
        try
        {
            while (true)
            {
                lock (networkGate) { Manager.UpdateNetwork(networkSource.GetCurrentNetwork()); }
                await Manager.ReconcileAsync(stop.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(250), clock, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Shutdown below gets a separate finite cleanup budget.
        }
    }

    private void InvalidateNetwork()
    {
        lock (networkGate) { Manager.UpdateNetwork(null); }
    }
}
