using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

// A narrow adapter seam over the existing coordinator, not a new PCP stack.
internal interface INovaPublicRelaySession : IAsyncDisposable
{
    Task<bool> RunAsync(string host, int port);
    Task<PcpRelayCoordinatorReadinessOutcome> WaitForReadinessAsync(CancellationToken cancellationToken);
    IPEndPoint LocalEndPoint { get; }
    Task StopAsync();
}

internal sealed class NovaPublicRelaySession(PcpRelayCoordinator coordinator) : INovaPublicRelaySession
{
    public async Task<bool> RunAsync(string host, int port) =>
        (await coordinator.RunAsync(host, port).ConfigureAwait(false)).ShutdownCompleted;
    public Task<PcpRelayCoordinatorReadinessOutcome> WaitForReadinessAsync(CancellationToken cancellationToken) =>
        coordinator.WaitForReadinessAsync(cancellationToken);
    public IPEndPoint LocalEndPoint => coordinator.LocalEndPoint;
    public Task StopAsync() => coordinator.StopAsync();
    public ValueTask DisposeAsync() => coordinator.DisposeAsync();
}

/// <summary>
/// Borrows one application manager. Coordinator/server own the channel,
/// fan-out, bound listener and mapping token; this driver never maps directly.
/// </summary>
internal sealed class NovaBorrowedPublicRelayDriver(
    NovaBorrowedNatManager borrowed,
    Func<NovaBorrowedNatManager, NovaPublicRelayIdentity, INovaPublicRelaySession>? createSession = null) : INovaPublicRelayDriver
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private bool cancellationFailed;
    private readonly Func<NovaBorrowedNatManager, NovaPublicRelayIdentity, INovaPublicRelaySession> factory =
        createSession ?? (static (owner, identity) => new NovaPublicRelaySession(new PcpRelayCoordinator(
            new TcpTransportConnector(), new ChannelStatusTracker(identity.ChannelId, identity.SessionId),
            new IPEndPoint(IPAddress.Any, 0), portMapping: owner.Manager)));
    private INovaPublicRelaySession? session;
    private Task<bool>? run;
    private Task<NovaPublicRelayStartResult>? starting;
    private Task<NovaPublicRelayFailure>? stopping;
    private Task<NovaPublicRelayEnd>? completion;

    public Task<NovaPublicRelayEnd> Completion
    {
        get { lock (gate) { return completion ?? throw new InvalidOperationException("Relay has not started."); } }
    }

    public Task<NovaPublicRelayStartResult> StartAsync(NovaPublicRelayIdentity identity, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (stopping is not null || starting is not null)
                return Task.FromResult(new NovaPublicRelayStartResult(null, NovaPublicRelayFailure.InvalidRequest));
            starting = Task.Run(() => StartCoreAsync(identity, cancellationToken), CancellationToken.None);
            return starting;
        }
    }

    public Task<NovaPublicRelayFailure> StopAsync()
    {
        lock (gate)
        {
            if (stopping is null)
            {
                try { stop.Cancel(); }
                catch (AggregateException) { cancellationFailed = true; }
                stopping = Task.Run(StopCoreAsync);
            }
            return stopping;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (await StopAsync().ConfigureAwait(false) != NovaPublicRelayFailure.None)
            throw new InvalidOperationException("Public relay cleanup was not confirmed.");
    }

    private async Task<NovaPublicRelayStartResult> StartCoreAsync(NovaPublicRelayIdentity identity, CancellationToken token)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        token = operation.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if (identity.RuntimeOwnerId != borrowed.OwnerId)
                return new(null, NovaPublicRelayFailure.InvalidRequest);
            session = factory(borrowed, identity);
            run = session.RunAsync(identity.Host, identity.Port);
            lock (gate) { completion = ObserveAsync(run); }
            if (await session.WaitForReadinessAsync(token).ConfigureAwait(false) != PcpRelayCoordinatorReadinessOutcome.Ready)
                return new(null, NovaPublicRelayFailure.ListenFailed);
            var endpoint = session.LocalEndPoint;
            if (endpoint.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(endpoint.Address) || endpoint.Port == 0)
                return new(null, NovaPublicRelayFailure.ListenFailed);
            if (run.IsCompleted)
                return new(null, NovaPublicRelayFailure.DriverEnded);
            token.ThrowIfCancellationRequested();
            return new(new(endpoint.Address.ToString(), endpoint.Port), NovaPublicRelayFailure.None);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(null, NovaPublicRelayFailure.Canceled); }
        catch (Exception) { return new(null, NovaPublicRelayFailure.DriverFailed); }
    }

    private static async Task<NovaPublicRelayEnd> ObserveAsync(Task<bool> task)
    {
        try { return new(await task.ConfigureAwait(false)); }
        catch (Exception) { return new(false); }
    }

    private async Task<NovaPublicRelayFailure> StopCoreAsync()
    {
        Task<NovaPublicRelayStartResult>? start;
        lock (gate) { start = starting; }
        if (start is not null) await start.ConfigureAwait(false);
        var failure = NovaPublicRelayFailure.None;
        try { if (session is not null) await session.StopAsync().ConfigureAwait(false); }
        catch (Exception) { failure = NovaPublicRelayFailure.CleanupFailed; }
        finally
        {
            try { if (session is not null) await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception) { failure = NovaPublicRelayFailure.CleanupFailed; }
        }
        try
        {
            if (run is not null && !await run.ConfigureAwait(false))
                failure = NovaPublicRelayFailure.CleanupFailed;
        }
        catch (Exception) { failure = NovaPublicRelayFailure.CleanupFailed; }
        stop.Dispose();
        return cancellationFailed ? NovaPublicRelayFailure.CleanupFailed : failure;
    }
}
