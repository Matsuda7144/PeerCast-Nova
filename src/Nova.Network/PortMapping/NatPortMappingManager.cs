using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.PortMapping;

/// <summary>
/// Management authority for public PCP TCP listeners. Desired state is mutable
/// during an operation; only this serialized reconciler can adopt observations
/// or choose another operation. Owner tokens belong to listeners, not sessions.
/// </summary>
public sealed class NatPortMappingManager : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim reconciler = new(1, 1);
    private readonly INatMappingBackend backend;
    private readonly NatDeviceOperationSerializer serializer;
    private readonly NatPortMappingOptions options;
    private readonly TimeProvider clock;
    private readonly DateTimeOffset timelineOrigin;
    private readonly long timestampOrigin;
    private readonly Func<TimeSpan> routerRecoveryDelay;
    private readonly Dictionary<Guid, ListenerAddress> owners = [];
    private readonly Dictionary<NatMappingIdentity, Entry> entries = [];
    private readonly Dictionary<NatDeviceIdentity, RouterEpochObservation> routerEpochs = [];
    private readonly Queue<NatMappingDiagnostic> diagnostics = [];
    private NatNetworkIdentity? network;
    private long generation;
    private bool enabled;
    private bool shuttingDown;
    private bool cleanupClosed;
    private Task<NatShutdownOutcome>? shutdownTask;

    public NatPortMappingManager(
        INatMappingBackend backend,
        NatPortMappingOptions? options = null,
        TimeProvider? timeProvider = null,
        NatDeviceOperationSerializer? serializer = null,
        Func<TimeSpan>? routerRecoveryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        this.backend = backend;
        this.options = (options ?? new NatPortMappingOptions()) with { };
        this.options.Validate();
        clock = timeProvider ?? TimeProvider.System;
        timelineOrigin = clock.GetUtcNow();
        timestampOrigin = clock.GetTimestamp();
        this.routerRecoveryDelay = routerRecoveryDelay ??
            (static () => TimeSpan.FromSeconds(Random.Shared.NextDouble() * 5));
        this.serializer = serializer ?? NatDeviceOperationSerializer.Shared;
        enabled = this.options.Enabled;
    }

    public bool Enabled
    {
        get { lock (gate) { return enabled; } }
    }

    public long Generation
    {
        get { lock (gate) { return generation; } }
    }

    public NatNetworkIdentity? Network
    {
        get { lock (gate) { return network; } }
    }

    public IReadOnlyList<NatMappingSnapshot> Snapshot
    {
        get
        {
            lock (gate)
            {
                return entries.Values.Select(entry => new NatMappingSnapshot(
                    entry.Identity,
                    OwnerCountLocked(entry.Identity),
                    entry.Status,
                    entry.ExternalReachability,
                    entry.ReturnedExternalPort,
                    entry.Lease,
                    entry.Pending is not null,
                    entry.MayExist,
                    entry.Pending?.Request.Operation,
                    entry.Pending is null ? entry.Backend : backend.GetDisplayName(entry.Identity),
                    entry.DetailCode,
                    entry.ExternalAddress)).ToArray();
            }
        }
    }

    public IReadOnlyList<NatMappingDiagnostic> Diagnostics
    {
        get { lock (gate) { return diagnostics.ToArray(); } }
    }

    public NatPortMappingReadState ReadState()
    {
        lock (gate)
        {
            return new NatPortMappingReadState(enabled, generation, network,
                options.RequestedLifetimeSeconds, clock.GetUtcNow(),
                owners.Values.Where(owner => network is null || MatchesNetwork(owner, network))
                    .Select(owner => owner.Port).Distinct().Order().ToArray(),
                Snapshot, diagnostics.ToArray());
        }
    }

    internal Task WaitForObservationAsync(Guid operationId)
    {
        lock (gate)
        {
            var pending = entries.Values.Select(entry => entry.Pending)
                .FirstOrDefault(operation => operation?.Request.OperationId == operationId);
            if (pending is not null)
            {
                return pending.Observed.Task;
            }

            if (diagnostics.Any(item => item.Event == "OperationObserved" && item.Request?.OperationId == operationId))
            {
                return Task.CompletedTask;
            }

            throw new ArgumentException("The operation is not tracked.", nameof(operationId));
        }
    }

    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(shuttingDown, this);
            if (enabled != value)
            {
                enabled = value;
                DesiredChangedLocked("SettingChanged");
            }
        }
    }

    public void UpdateNetwork(NatNetworkIdentity? value)
    {
        value?.Validate();
        lock (gate)
        {
            if (network != value)
            {
                network = value;
                DesiredChangedLocked("NetworkChanged");
            }
        }
    }

    public bool ReportExternalReachability(int internalPort, NatExternalReachability reachability, string? externalAddress = null)
    {
        if (internalPort is < 1 or > 65535 || !Enum.IsDefined(reachability))
        {
            return false;
        }

        lock (gate)
        {
            if (shuttingDown || network is null)
            {
                return false;
            }

            var identity = new NatMappingIdentity(network, NatMappingProtocol.Tcp, internalPort);
            if (!entries.TryGetValue(identity, out var entry))
            {
                return false;
            }

            var validAddress = PeerCast.Nova.Network.Publishing.PublishingIpv4.IsPublicAddress(externalAddress)
                ? externalAddress!.Trim()
                : null;
            if (entry.ExternalReachability == reachability &&
                (validAddress is null || string.Equals(entry.ExternalAddress, validAddress, StringComparison.Ordinal)))
            {
                return false;
            }

            entry.ExternalReachability = reachability;
            if (validAddress is not null)
            {
                entry.ExternalAddress = validAddress;
            }

            generation = checked(generation + 1);
            RecordLocked("ExternalReachabilityObserved");
            return true;
        }
    }

    /// <summary>
    /// Called only after the public listener binds. Multiple users of the same
    /// listener may hold tokens; only the final release removes its demand.
    /// Loopback, IPv6 and unbound (port zero) listeners cannot be registered.
    /// </summary>
    public IDisposable AcquirePublicTcpListener(IPEndPoint boundEndPoint)
    {
        ArgumentNullException.ThrowIfNull(boundEndPoint);
        if (boundEndPoint.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(boundEndPoint.Address) || boundEndPoint.Port == 0)
        {
            throw new ArgumentException("A bound public IPv4 TCP listener is required.", nameof(boundEndPoint));
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(shuttingDown, this);
            var id = Guid.NewGuid();
            owners.Add(id, new ListenerAddress(boundEndPoint.Address.ToString(), boundEndPoint.Port));
            DesiredChangedLocked("ListenerAcquired");
            return new OwnerToken(this, id);
        }
    }

    /// <summary>
    /// Host-driven pump; tests drive this explicitly with a virtual clock.
    /// Cancellation limits waiting, never discards a submitted operation.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await reconciler.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Entry? entry;
                PendingOperation? pending;
                lock (gate)
                {
                    EnsureDesiredEntriesLocked();
                    ObserveCompletedLocked();
                    entry = SelectNextLocked();
                    if (entry is null)
                    {
                        return;
                    }

                    var ownerCount = OwnerCountLocked(entry.Identity);
                    var operation = ownerCount == 0 ? NatMappingOperation.Unmap :
                        entry.MayExist ? NatMappingOperation.Renew : NatMappingOperation.Map;
                    var request = new NatMappingRequest(
                        Guid.NewGuid(), generation, entry.Identity, operation,
                        operation == NatMappingOperation.Unmap ? 0 :
                            entry.ReturnedExternalPort ?? entry.Identity.InternalPort,
                        operation == NatMappingOperation.Unmap ? 0 : options.RequestedLifetimeSeconds,
                        ownerCount);
                    pending = new PendingOperation(request, clock.GetUtcNow(), TimelineNow, entry.MayExist);
                    entry.Pending = pending;
                    entry.Status = operation == NatMappingOperation.Unmap
                        ? NatMappingStatus.Unmapping : NatMappingStatus.Mapping;
                }

                var task = serializer.ExecuteAsync(entry.Identity.Network.Device,
                    () => DispatchAsync(entry, pending));
                lock (gate) { pending.Task = task; }
                try
                {
                    await task.WaitAsync(options.OperationTimeout, clock, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
                {
                    lock (gate)
                    {
                        entry.Status = pending.Request.Operation == NatMappingOperation.Unmap || OwnerCountLocked(entry.Identity) == 0
                            ? NatMappingStatus.UnmapUnconfirmed : NatMappingStatus.ResultUnknown;
                        if (pending.Dispatched && pending.Request.RequestedLifetimeSeconds > 0)
                        {
                            var estimate = pending.StartedAt.AddSeconds(pending.Request.RequestedLifetimeSeconds);
                            if (entry.Lease?.EstimatedExpiresAt is { } previous && previous > estimate)
                            {
                                estimate = previous;
                            }

                            entry.Lease = new NatLeaseEvidence(pending.Request.RequestedLifetimeSeconds,
                                null, null, backend.MayCreatePermanentLease(entry.Identity) ? null : estimate,
                                backend.MayCreatePermanentLease(entry.Identity) ? NatLeaseKind.Unknown : NatLeaseKind.Finite);
                        }

                        RecordLocked("OperationWaitEnded", pending.Request,
                            new NatBackendResult(NatBackendOutcome.ResultUnknown, DetailCode: exception.GetType().Name));
                    }

                    // The backend may ignore cancellation or have already changed the router.
                    // Its device permit remains held until the original task really finishes.
                    pending.Cancellation.Cancel();
                    _ = RetainLateObservationAsync(task);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        finally
        {
            reconciler.Release();
        }
    }

    public Task<NatShutdownOutcome> ShutdownAsync()
    {
        lock (gate)
        {
            shutdownTask ??= ShutdownCoreAsync();
            return shutdownTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
    }

    private async Task<NatBackendResult> DispatchAsync(Entry entry, PendingOperation pending)
    {
        lock (gate)
        {
            // Recheck after acquiring the device lane. A queued request can be
            // superseded before any send, including by a new network epoch.
            var desired = OwnerCountLocked(entry.Identity) > 0;
            if (cleanupClosed || pending.Cancellation.IsCancellationRequested ||
                network != entry.Identity.Network ||
                (pending.Request.Operation == NatMappingOperation.Unmap ? desired : !desired))
            {
                return new NatBackendResult(NatBackendOutcome.NotSent, DetailCode: "SupersededBeforeSend");
            }

            pending.Dispatched = true;
            pending.StartedAt = clock.GetUtcNow();
            pending.StartedOnTimeline = TimelineNow;
            entry.MayExist = true;
            RecordLocked("OperationDispatched", pending.Request);
        }

        try
        {
            return await backend.ExecuteAsync(pending.Request, pending.Cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new NatBackendResult(NatBackendOutcome.ResultUnknown, DetailCode: exception.GetType().Name);
        }
    }

    private async Task RetainLateObservationAsync(Task<NatBackendResult> task)
    {
        await task.ConfigureAwait(false);
        // Even after bounded shutdown ends, consume the observation and update
        // evidence. cleanupClosed prevents new sends outside the cleanup budget.
        await ReconcileAsync().ConfigureAwait(false);
    }

    private void EnsureDesiredEntriesLocked()
    {
        if (!enabled || shuttingDown || network is null)
        {
            return;
        }

        foreach (var owner in owners.Values)
        {
            if (MatchesNetwork(owner, network))
            {
                var identity = new NatMappingIdentity(network, NatMappingProtocol.Tcp, owner.Port);
                entries.TryAdd(identity, new Entry(identity));
            }
        }
    }

    private Entry? SelectNextLocked()
    {
        var now = TimelineNow;
        foreach (var entry in entries.Values)
        {
            var desired = OwnerCountLocked(entry.Identity) > 0;
            if (!desired && entry.MayExist && entry.Pending is null)
            {
                entry.Status = NatMappingStatus.UnmapUnconfirmed;
            }

            if (entry.Status == NatMappingStatus.Mapped && entry.ExpiresAt is { } expires && expires <= now)
            {
                entry.Status = NatMappingStatus.ResultUnknown;
            }

            // Never address a retired route. In particular, an old epoch's
            // NAT-PMP delete could delete the new epoch's identical internal key.
            if (cleanupClosed || entry.Pending is not null || network != entry.Identity.Network)
            {
                continue;
            }

            if (desired && entry.RetryAt <= now &&
                (entry.Status != NatMappingStatus.Mapped || entry.RenewAt <= now))
            {
                return entry;
            }

            if (!desired && entry.MayExist && entry.RetryAt <= now)
            {
                return entry;
            }
        }

        return null;
    }

    private void ObserveCompletedLocked()
    {
        foreach (var entry in entries.Values)
        {
            var pending = entry.Pending;
            if (pending?.Task is not { IsCompletedSuccessfully: true } task)
            {
                continue;
            }

            var result = task.Result;
            if (result.Outcome == NatBackendOutcome.Success &&
                (pending.Request.Operation == NatMappingOperation.Unmap
                    ? result.LifetimeSeconds != 0 || result.ExternalPort != 0
                    : result.LifetimeSeconds is null || result.ExternalPort is null or < 1 or > 65535 ||
                        (result.LeaseKind == NatLeaseKind.Permanent ? result.LifetimeSeconds != 0 :
                            result.LeaseKind != NatLeaseKind.Finite || result.LifetimeSeconds == 0)))
            {
                result = new NatBackendResult(NatBackendOutcome.ResultUnknown, DetailCode: "InvalidSuccessEvidence");
            }

            entry.Pending = null;
            entry.Backend = result.Backend;
            entry.DetailCode = result.DetailCode;
            pending.Cancellation.Dispose();
            RecordLocked("OperationObserved", pending.Request, result);
            var now = clock.GetUtcNow();
            var schedulingNow = TimelineNow;
            var desired = OwnerCountLocked(entry.Identity) > 0;
            var currentNetwork = network == entry.Identity.Network;
            entry.RetryAt = schedulingNow + options.RetryInterval;
            if (currentNetwork && result.RouterEpochSeconds is { } epoch)
            {
                ObserveRouterEpochLocked(entry, epoch, pending.Request);
            }

            switch (result.Outcome)
            {
                case NatBackendOutcome.Success when pending.Request.Operation == NatMappingOperation.Unmap:
                    entry.MayExist = false;
                    entry.ExternalAddress = null;
                    entry.ExternalReachability = NatExternalReachability.Unknown;
                    entry.Status = NatMappingStatus.Unmapped;
                    entry.RetryAt = schedulingNow;
                    break;
                case NatBackendOutcome.Success:
                    entry.MayExist = true;
                    entry.ReturnedExternalPort = result.ExternalPort;
                    if (!string.IsNullOrWhiteSpace(result.ExternalAddress))
                    {
                        entry.ExternalAddress = result.ExternalAddress;
                    }
                    entry.Lease = new NatLeaseEvidence(pending.Request.RequestedLifetimeSeconds,
                        result.LifetimeSeconds, now,
                        result.LeaseKind == NatLeaseKind.Permanent ? null : pending.StartedAt.AddSeconds(result.LifetimeSeconds!.Value),
                        result.LeaseKind);
                    entry.RenewAt = pending.StartedOnTimeline.AddSeconds(result.LeaseKind == NatLeaseKind.Permanent
                        ? options.RequestedLifetimeSeconds / 2.0 : result.LifetimeSeconds!.Value / 2.0);
                    entry.ExpiresAt = result.LeaseKind == NatLeaseKind.Permanent ? null :
                        pending.StartedOnTimeline.AddSeconds(result.LifetimeSeconds!.Value);
                    entry.RetryAt = schedulingNow;
                    entry.Status = desired ? NatMappingStatus.Mapped : NatMappingStatus.UnmapUnconfirmed;

                    RecordLocked(desired ? "MappingAdopted" : "MappingRequiresCleanup", pending.Request, result);
                    break;
                case NatBackendOutcome.NotSent:
                case NatBackendOutcome.Rejected:
                    entry.MayExist = pending.PreviousMayExist;
                    entry.Status = entry.MayExist && !desired
                        ? NatMappingStatus.UnmapUnconfirmed : NatMappingStatus.Rejected;
                    break;
                case NatBackendOutcome.ResultUnknown:
                    entry.MayExist = pending.Dispatched || pending.PreviousMayExist;
                    entry.Status = pending.Request.Operation == NatMappingOperation.Unmap || !desired
                        ? NatMappingStatus.UnmapUnconfirmed : NatMappingStatus.ResultUnknown;
                    if (pending.Request.RequestedLifetimeSeconds > 0 && pending.Dispatched)
                    {
                        var estimate = pending.StartedAt.AddSeconds(pending.Request.RequestedLifetimeSeconds);
                        if (entry.Lease?.EstimatedExpiresAt is { } previous && previous > estimate)
                        {
                            estimate = previous;
                        }

                        entry.Lease = new NatLeaseEvidence(pending.Request.RequestedLifetimeSeconds,
                            null, null, backend.MayCreatePermanentLease(entry.Identity) ? null : estimate,
                            backend.MayCreatePermanentLease(entry.Identity) ? NatLeaseKind.Unknown : NatLeaseKind.Finite);
                    }

                    // Correct a stale delete with current demand, or a stale
                    // creation with no demand, without waiting for a retry timer.
                    if (pending.Request.Generation != generation && currentNetwork)
                    {
                        entry.RetryAt = schedulingNow;
                    }

                    break;
            }

            if (!currentNetwork && entry.MayExist)
            {
                entry.Status = NatMappingStatus.UnmapUnconfirmed;
            }

            pending.Observed.TrySetResult();
        }
    }

    private void ObserveRouterEpochLocked(Entry observed, uint epoch, NatMappingRequest request)
    {
        var device = observed.Identity.Network.Device;
        var now = TimelineNow;
        // RFC 6886 3.6 tolerates slow gateway clocks and two seconds of
        // rounding. A reset can be detected even when the new counter is
        // larger than the old value after a long gap between observations.
        if (routerEpochs.TryGetValue(device, out var previous) &&
            epoch < previous.Seconds + (now - previous.ObservedAt).TotalSeconds * 0.875 - 2)
        {
            var recoveryAt = now + routerRecoveryDelay();
            foreach (var entry in entries.Values)
            {
                if (entry != observed && entry.Identity.Network.Device == device &&
                    entry.MayExist && OwnerCountLocked(entry.Identity) > 0)
                {
                    entry.Status = NatMappingStatus.ResultUnknown;
                    entry.RetryAt = recoveryAt;
                }
            }

            RecordLocked("RouterEpochRegressed", request);
        }

        routerEpochs[device] = new RouterEpochObservation(epoch, now);
    }

    private async Task<NatShutdownOutcome> ShutdownCoreAsync()
    {
        shuttingDown = true;
        enabled = false;
        owners.Clear();
        DesiredChangedLocked("ShutdownRequested");
        using var budget = new CancellationTokenSource(options.ShutdownTimeout, clock);
        try
        {
            while (true)
            {
                await ReconcileAsync(budget.Token).ConfigureAwait(false);
                lock (gate)
                {
                    if (entries.Values.All(entry => !entry.MayExist && entry.Pending is null))
                    {
                        cleanupClosed = true;
                        RecordLocked("ShutdownCompleted");
                        return NatShutdownOutcome.Completed;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), clock, budget.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            lock (gate)
            {
                cleanupClosed = true;
                foreach (var entry in entries.Values.Where(entry => entry.MayExist))
                {
                    entry.Status = NatMappingStatus.UnmapUnconfirmed;
                }

                RecordLocked("ShutdownCleanupUnconfirmed");
            }

            return NatShutdownOutcome.CleanupUnconfirmed;
        }
    }

    private int OwnerCountLocked(NatMappingIdentity identity) =>
        enabled && !shuttingDown && network == identity.Network
            ? owners.Values.Count(owner => owner.Port == identity.InternalPort && MatchesNetwork(owner, identity.Network))
            : 0;

    private static bool MatchesNetwork(ListenerAddress owner, NatNetworkIdentity current) =>
        owner.Address == "0.0.0.0" || owner.Address == current.InternalAddress;

    private void Release(Guid id)
    {
        lock (gate)
        {
            if (owners.Remove(id))
            {
                DesiredChangedLocked("ListenerReleased");
            }
        }
    }

    private void DesiredChangedLocked(string eventName)
    {
        generation = checked(generation + 1);
        foreach (var entry in entries.Values)
        {
            entry.RetryAt = TimelineNow;
            if (OwnerCountLocked(entry.Identity) == 0 && entry.MayExist)
            {
                entry.Status = NatMappingStatus.UnmapUnconfirmed;
            }
        }

        RecordLocked(eventName);
    }

    private void RecordLocked(string eventName, NatMappingRequest? request = null, NatBackendResult? result = null)
    {
        diagnostics.Enqueue(new NatMappingDiagnostic(clock.GetUtcNow(), eventName,
            generation, request, result, request is not null && request.Generation != generation,
            request is not null && request.Identity.Network == network,
            request is null ? owners.Count : OwnerCountLocked(request.Identity), network));
        if (diagnostics.Count > 256)
        {
            diagnostics.Dequeue();
        }
    }

    private sealed record ListenerAddress(string Address, int Port);

    // Wall-clock changes must not postpone finite lease renewals or retries.
    private DateTimeOffset TimelineNow =>
        timelineOrigin + clock.GetElapsedTime(timestampOrigin, clock.GetTimestamp());

    private sealed record RouterEpochObservation(uint Seconds, DateTimeOffset ObservedAt);

    private sealed class Entry(NatMappingIdentity identity)
    {
        public NatMappingIdentity Identity { get; } = identity;
        public NatMappingStatus Status { get; set; }
        public NatExternalReachability ExternalReachability { get; set; } = NatExternalReachability.Unknown;
        public string? ExternalAddress { get; set; }
        public bool MayExist { get; set; }
        public int? ReturnedExternalPort { get; set; }
        public NatLeaseEvidence? Lease { get; set; }
        public DateTimeOffset RetryAt { get; set; }
        public DateTimeOffset RenewAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string Backend { get; set; } = "UPnP / NAT-PMP（検出中）";
        public string? DetailCode { get; set; }
        public PendingOperation? Pending { get; set; }
    }

    private sealed class PendingOperation(
        NatMappingRequest request, DateTimeOffset startedAt, DateTimeOffset startedOnTimeline, bool previousMayExist)
    {
        public NatMappingRequest Request { get; } = request;
        public CancellationTokenSource Cancellation { get; } = new();
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public DateTimeOffset StartedOnTimeline { get; set; } = startedOnTimeline;
        public bool PreviousMayExist { get; } = previousMayExist;
        public bool Dispatched { get; set; }
        public Task<NatBackendResult>? Task { get; set; }
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class OwnerToken(NatPortMappingManager owner, Guid id) : IDisposable
    {
        private NatPortMappingManager? manager = owner;

        public void Dispose() => Interlocked.Exchange(ref manager, null)?.Release(id);
    }
}
