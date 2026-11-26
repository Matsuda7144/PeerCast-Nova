using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal interface INovaAcceptanceRelaySession
{
    Task<IPEndPoint> StartAsync(CancellationToken cancellationToken);
    Task Completion { get; }
    string ListenerState { get; }
    NatPortMappingReadState ReadMappingState();
    Task<NatShutdownOutcome> CleanupAsync();
}

/// <summary>
/// Acceptance-only composition of the same downstream server used by the GUI's
/// coordinator. No upstream publisher, artificial content, or direct NAT operations.
/// </summary>
internal sealed class NovaAcceptanceRelaySession : INovaAcceptanceRelaySession, IAsyncDisposable
{
    private readonly NatPortMappingRuntime mapping;
    private readonly PcpRelayFanout fanout = new();
    private readonly PcpDownstreamRelayServer server;
    private Task<PcpDownstreamRelayServerResult>? run;
    private Task<NatShutdownOutcome>? cleanup;

    internal NovaAcceptanceRelaySession(NovaAcceptanceRelayOptions options,
        INatMappingBackend? backend = null, INatNetworkSource? networkSource = null,
        TimeProvider? timeProvider = null)
    {
        mapping = new NatPortMappingRuntime(new NatPortMappingOptions { Enabled = options.AutoPortMapping },
            backend, networkSource, timeProvider);
        server = new PcpDownstreamRelayServer(new IPEndPoint(IPAddress.Any, options.Port), fanout,
            new ChannelId(Guid.NewGuid()), Guid.NewGuid(), portMapping: mapping.Manager);
    }

    public Task Completion => run ?? Task.CompletedTask;
    public string ListenerState => server.Snapshot.State.ToString();
    public NatPortMappingReadState ReadMappingState() => mapping.Manager.ReadState();

    public Task<IPEndPoint> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // RunAsync binds synchronously and registers demand only after a successful bind.
        run = server.RunAsync(cancellationToken);
        return Task.FromResult(server.LocalEndPoint);
    }

    public Task<NatShutdownOutcome> CleanupAsync() => cleanup ??= CleanupCoreAsync();

    public async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);

    private async Task<NatShutdownOutcome> CleanupCoreAsync()
    {
        try
        {
            // Server stop closes the socket, releases its public owner and drains children.
            try
            {
                await server.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await fanout.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                // Runtime shutdown owns the finite reconciler cleanup budget and pump drain.
                await mapping.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (server.Snapshot.CleanupIncompleteCount != 0)
        {
            throw new IOException("Listener child cleanup was incomplete.");
        }

        return mapping.ShutdownOutcome ?? NatShutdownOutcome.CleanupUnconfirmed;
    }
}
