using System.Net;

namespace PeerCast.Nova.App;

/// <summary>
/// One single-channel physical owner, many logical references (including
/// pending acquisition intents). Each entry remains authoritative until its
/// actual cleanup finishes. No listener or NAT operation is performed here.
/// </summary>
internal sealed class NovaSharedPublicRelayOwner(
    NovaBorrowedNatManager borrowed, Func<NovaBorrowedNatManager, INovaPublicRelayDriver> createDriver) : IAsyncDisposable
{
    private readonly object gate = new();
    private Entry? current;
    private bool closing;
    private bool cleanupFailed;
    private long epoch;
    private long revision;
    private Task? disposal;
    private NovaPublicRelaySnapshot snapshot = new(borrowed.OwnerId, 0, 0, NovaPublicRelayState.Stopped, 0, null, null, null, NovaPublicRelayFailure.None);

    public NovaPublicRelaySnapshot ReadSnapshot() { lock (gate) { return snapshot; } }

    public async Task<NovaPublicRelayAcquireResult> AcquireAsync(NovaPublicRelayRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.ChannelId is null || request.SessionId is null ||
            request.BindPolicy != NovaPublicRelayBindPolicy.AnyIpv4Ephemeral ||
            request.Protocol != NovaPublicRelayProtocol.PcpTcp || request.Scope != NovaPublicRelayScope.SingleChannelPublic ||
            !DailyPcpPlaybackSourceParser.TryParse(request.Source, out var source) || source is null ||
            source.ChannelId != request.ChannelId)
            return new(null, NovaPublicRelayFailure.InvalidRequest);

        var identity = new NovaPublicRelayIdentity(borrowed.OwnerId, source.ChannelId, request.SessionId,
            source.Host.ToLowerInvariant(), source.Port, request.BindPolicy, request.Protocol, request.Scope);
        while (true)
        {
            Entry? entry = null;
            Task<NovaPublicRelayFailure>? retiring = null;
            var leaseId = Guid.NewGuid();
            lock (gate)
            {
                if (closing) return new(null, NovaPublicRelayFailure.Closing);
                if (cleanupFailed) return new(null, NovaPublicRelayFailure.CleanupFailed);
                if (cancellationToken.IsCancellationRequested) return new(null, NovaPublicRelayFailure.Canceled);
                if (current is { Stopping: true }) retiring = current.Retired.Task;
                else
                {
                    if (current is not null && current.Identity != identity)
                        return new(null, NovaPublicRelayFailure.IncompatibleIdentity);
                    if (current is null)
                    {
                        var created = new Entry(++epoch, identity);
                        current = created;
                        // Task.Run prevents driver factories or synchronous bind
                        // from running inside caller admission / snapshot locks.
                        _ = Task.Run(() => RunEntryAsync(created), CancellationToken.None);
                    }
                    entry = current;
                    entry.References.Add(leaseId);
                    PublishLocked(entry, entry.Started.Task.IsCompletedSuccessfully &&
                        entry.Started.Task.Result == NovaPublicRelayFailure.None
                        ? NovaPublicRelayState.Listening : NovaPublicRelayState.Starting, NovaPublicRelayFailure.None);
                }
            }
            if (retiring is not null)
            {
                try { await retiring.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return new(null, NovaPublicRelayFailure.Canceled); }
                continue;
            }

            NovaPublicRelayFailure failure;
            try { failure = await entry!.Started.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { failure = NovaPublicRelayFailure.Canceled; }
            lock (gate)
            {
                if (failure == NovaPublicRelayFailure.None && cancellationToken.IsCancellationRequested)
                    failure = NovaPublicRelayFailure.Canceled;
                if (failure == NovaPublicRelayFailure.None && (closing || entry!.Stopping || current != entry))
                    failure = closing ? NovaPublicRelayFailure.Closing : NovaPublicRelayFailure.DriverEnded;
                if (failure == NovaPublicRelayFailure.None)
                    return new(new NovaPublicRelayLease(() => ReleaseAsync(entry!, leaseId)), NovaPublicRelayFailure.None);
            }
            var released = await ReleaseAsync(entry!, leaseId).ConfigureAwait(false);
            return new(null, released == NovaPublicRelayFailure.CleanupFailed ? released : failure);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is null)
            {
                closing = true;
                if (current is not null)
                {
                    current.References.Clear();
                    RequestStopLocked(current);
                }
                disposal = Task.Run(DisposeCoreAsync);
            }
            return new(disposal);
        }
    }

    private Task<NovaPublicRelayFailure> ReleaseAsync(Entry entry, Guid id)
    {
        lock (gate)
        {
            if (entry != current)
                return entry.Retired.Task;
            entry.References.Remove(id);
            if (entry.References.Count == 0)
                RequestStopLocked(entry);
            else if (!entry.Stopping)
                PublishLocked(entry, snapshot.State, snapshot.Failure);
            return entry.Stopping ? entry.Retired.Task : Task.FromResult(NovaPublicRelayFailure.None);
        }
    }

    private void RequestStopLocked(Entry entry)
    {
        if (entry.Stopping) return;
        entry.Stopping = true;
        entry.StopSignal.TrySetResult();
        try { entry.Stop.Cancel(); }
        catch (AggregateException) { entry.CancellationFailed = true; }
        PublishLocked(entry, NovaPublicRelayState.Stopping, snapshot.Failure);
    }

    private async Task RunEntryAsync(Entry entry)
    {
        INovaPublicRelayDriver? driver = null;
        var failure = NovaPublicRelayFailure.None;
        var cleanup = NovaPublicRelayFailure.None;
        try
        {
            entry.Stop.Token.ThrowIfCancellationRequested();
            driver = createDriver(borrowed);
            var result = await driver.StartAsync(entry.Identity, entry.Stop.Token).ConfigureAwait(false);
            failure = result.Failure;
            if (failure == NovaPublicRelayFailure.None && !ValidEndpoint(result.Endpoint))
                failure = NovaPublicRelayFailure.ListenFailed;
            lock (gate)
            {
                if (entry.Stopping || closing)
                    failure = NovaPublicRelayFailure.Canceled;
                if (failure == NovaPublicRelayFailure.None)
                {
                    entry.Endpoint = result.Endpoint;
                    PublishLocked(entry, NovaPublicRelayState.Listening, failure);
                }
                entry.Started.TrySetResult(failure);
            }
            if (failure == NovaPublicRelayFailure.None)
            {
                var completion = driver.Completion;
                if (await Task.WhenAny(entry.StopSignal.Task, completion).ConfigureAwait(false) == completion)
                {
                    var ended = await completion.ConfigureAwait(false);
                    lock (gate)
                    {
                        if (!entry.Stopping) failure = NovaPublicRelayFailure.DriverEnded;
                        if (!ended.CleanupConfirmed) cleanup = NovaPublicRelayFailure.CleanupFailed;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (entry.Stop.IsCancellationRequested) { failure = NovaPublicRelayFailure.Canceled; }
        catch (Exception) { failure = NovaPublicRelayFailure.DriverFailed; }
        finally
        {
            lock (gate)
            {
                entry.References.Clear();
                RequestStopLocked(entry);
                entry.Started.TrySetResult(failure);
            }
            if (driver is not null)
            {
                try
                {
                    if (await driver.StopAsync().ConfigureAwait(false) != NovaPublicRelayFailure.None)
                        cleanup = NovaPublicRelayFailure.CleanupFailed;
                }
                catch (Exception) { cleanup = NovaPublicRelayFailure.CleanupFailed; }
                finally
                {
                    try { await driver.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception) { cleanup = NovaPublicRelayFailure.CleanupFailed; }
                }
            }
            lock (gate)
            {
                if (entry.CancellationFailed) cleanup = NovaPublicRelayFailure.CleanupFailed;
                cleanupFailed |= cleanup == NovaPublicRelayFailure.CleanupFailed;
                entry.Endpoint = null;
                PublishLocked(entry, cleanupFailed || failure is not NovaPublicRelayFailure.None and not NovaPublicRelayFailure.Canceled
                    ? NovaPublicRelayState.Failed : NovaPublicRelayState.Stopped,
                    cleanupFailed ? NovaPublicRelayFailure.CleanupFailed : failure);
                if (!cleanupFailed && current == entry) current = null;
                entry.Retired.TrySetResult(cleanup);
                entry.Stop.Dispose();
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task<NovaPublicRelayFailure>? retiring;
        lock (gate) { retiring = current?.Retired.Task; }
        if (retiring is not null) await retiring.ConfigureAwait(false);
        lock (gate)
        {
            snapshot = snapshot with { Revision = ++revision, State = cleanupFailed ? NovaPublicRelayState.Failed : NovaPublicRelayState.Disposed };
            if (cleanupFailed) throw new InvalidOperationException("Public relay cleanup was not confirmed.");
        }
    }

    private void PublishLocked(Entry entry, NovaPublicRelayState state, NovaPublicRelayFailure failure) =>
        snapshot = new(borrowed.OwnerId, entry.Epoch, ++revision, state, entry.References.Count,
            entry.Identity.ChannelId, entry.Identity.SessionId, entry.Endpoint, failure);

    private static bool ValidEndpoint(NovaPublicRelayEndpoint? endpoint) =>
        endpoint is { Port: > 0 and <= 65535 } && IPAddress.TryParse(endpoint.Address, out var address) &&
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(address);

    private sealed class Entry(long epoch, NovaPublicRelayIdentity identity)
    {
        public long Epoch { get; } = epoch;
        public NovaPublicRelayIdentity Identity { get; } = identity;
        public HashSet<Guid> References { get; } = [];
        public CancellationTokenSource Stop { get; } = new();
        public TaskCompletionSource StopSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<NovaPublicRelayFailure> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<NovaPublicRelayFailure> Retired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NovaPublicRelayEndpoint? Endpoint { get; set; }
        public bool Stopping { get; set; }
        public bool CancellationFailed { get; set; }
    }
}
