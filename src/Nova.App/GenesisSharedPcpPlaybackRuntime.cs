using System.Net;
using System.Runtime.ExceptionServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

internal sealed record GenesisSharedPcpPlaybackAttachment(
    IOwnedPcpChannelPacketConsumer Consumer,
    PcpRelayConsumerRegistration Registration,
    Task<PcpRelayCoordinatorResult> OwnerCompletion,
    Task OwnerRetirement);

internal sealed class GenesisSharedPcpRecordingOwnerAttachment : IAsyncDisposable
{
    private readonly IAsyncDisposable _ownerLease;

    public GenesisSharedPcpRecordingOwnerAttachment(
        RecordingRuntime recording,
        Task<PcpRelayCoordinatorReadinessOutcome> readiness,
        Task<PcpRelayCoordinatorResult> ownerCompletion,
        IAsyncDisposable ownerLease)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(ownerCompletion);
        ArgumentNullException.ThrowIfNull(ownerLease);
        Recording = recording;
        Readiness = readiness;
        OwnerCompletion = ownerCompletion;
        _ownerLease = ownerLease;
    }

    public RecordingRuntime Recording { get; }

    public Task<PcpRelayCoordinatorReadinessOutcome> Readiness { get; }

    public Task<PcpRelayCoordinatorResult> OwnerCompletion { get; }

    public ValueTask DisposeAsync() => _ownerLease.DisposeAsync();
}

/// <summary>
/// Application-owned bridge between GUI playback and the same PCP relay
/// composition that exposes Recording Core's fan-out source provider.
/// </summary>
internal sealed class GenesisSharedPcpPlaybackRuntime :
    IAsyncDisposable,
    IGenesisSharedPcpRecordingOwnerProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeGate = new();
    private readonly Dictionary<ChannelId, SharedOwner> _owners = [];
    private readonly PcpRecordingRuntimeComposition _composition;
    private Task? _disposeTask;
    private bool _disposed;

    public GenesisSharedPcpPlaybackRuntime(
        PcpRecordingRuntimeComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        _composition = composition;
    }

    internal int RetainedOwnerCount
    {
        get
        {
            _gate.Wait();
            try
            {
                return _owners.Count;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<GenesisSharedPcpPlaybackAttachment> AttachPlaybackAsync(
        DailyPcpPlaybackSource source,
        Func<ChannelId, ChannelSessionId, IOwnedPcpChannelPacketConsumer>
            consumerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(consumerFactory);
        while (true)
        {
            SharedOwner? staleOwner = null;
            SharedOwner? unstartedOwner = null;
            IOwnedPcpChannelPacketConsumer? consumer = null;
            PcpRelayConsumerRegistration? registration = null;
            GenesisSharedPcpPlaybackAttachment? attachment = null;
            Exception? attachFailure = null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                SharedOwner? owner = null;
                if (_owners.TryGetValue(source.ChannelId, out var existing))
                {
                    if (existing.Coordinator.CanAcceptConsumers)
                    {
                        if (!existing.Matches(source))
                        {
                            throw new InvalidOperationException(
                                "The active channel owner has a different PCP source endpoint.");
                        }

                        owner = existing;
                    }
                    else
                    {
                        staleOwner = existing;
                    }
                }

                if (staleOwner is null)
                {
                    var created = owner is null;
                    owner ??= CreateOwner(source);

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        consumer = consumerFactory(source.ChannelId, owner.SessionId);
                        registration = await owner.Coordinator
                            .AttachPlaybackConsumerAsync(consumer)
                            .ConfigureAwait(false);
                        if (registration is null)
                        {
                            if (created)
                            {
                                throw new InvalidOperationException(
                                    "The shared PCP channel owner no longer accepts playback consumers.");
                            }

                            staleOwner = owner;
                        }
                        else
                        {
                            if (created)
                            {
                                owner.Start(owner.Coordinator.RunAsync(
                                    source.Host,
                                    source.Port,
                                    cancellationToken: CancellationToken.None));
                                _owners.Add(source.ChannelId, owner);
                                owner.SetRetirementTask(
                                    RetireOwnerAsync(source.ChannelId, owner));
                            }

                            attachment = new(
                                consumer,
                                registration,
                                owner.Completion ?? throw new InvalidOperationException(
                                    "The shared PCP channel owner has not started."),
                                owner.RetirementTask ?? throw new InvalidOperationException(
                                    "The shared PCP channel owner is not being observed."));
                        }
                    }
                    catch (Exception exception)
                    {
                        attachFailure = exception;
                        if (created)
                        {
                            unstartedOwner = owner;
                        }
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (staleOwner is not null)
            {
                if (consumer is not null)
                {
                    await consumer.DisposeAsync().ConfigureAwait(false);
                }

                await (staleOwner.RetirementTask ?? throw new InvalidOperationException(
                    "A published shared PCP owner has no retirement observer."))
                    .ConfigureAwait(false);
                continue;
            }

            if (attachFailure is not null)
            {
                List<Exception>? cleanupFailures = null;
                if (registration is not null)
                {
                    try
                    {
                        await registration.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (cleanupFailures ??= []).Add(exception);
                    }
                }

                if (consumer is not null)
                {
                    try
                    {
                        await consumer.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (cleanupFailures ??= []).Add(exception);
                    }
                }

                if (unstartedOwner is not null)
                {
                    try
                    {
                        await unstartedOwner.ReleaseCoordinatorOnceAsync(_composition)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (cleanupFailures ??= []).Add(exception);
                    }
                }

                if (cleanupFailures is { Count: > 0 })
                {
                    cleanupFailures.Insert(0, attachFailure);
                    throw new AggregateException(
                        "The playback attachment failed and cleanup was incomplete.",
                        cleanupFailures);
                }

                ExceptionDispatchInfo.Capture(attachFailure).Throw();
            }

            return attachment ?? throw new InvalidOperationException(
                "The playback attachment was not created.");
        }
    }

    /// <summary>
    /// Ensures Recording Core joins the same application-owned channel owner
    /// as playback. The returned temporary owner lease keeps the coordinator
    /// alive until RecordingRuntime has acquired its own fan-out lease.
    /// </summary>
    public async Task<GenesisSharedPcpRecordingOwnerAttachment>
        AttachRecordingOwnerAsync(
            DailyPcpPlaybackSource source,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        GenesisSharedPcpRecordingOwnerAttachment attachment;
        while (true)
        {
            SharedOwner? staleOwner = null;
            SharedOwner? unstartedOwner = null;
            IAsyncDisposable? ownerLease = null;
            GenesisSharedPcpRecordingOwnerAttachment? candidate = null;
            Exception? attachFailure = null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                SharedOwner? owner = null;
                if (_owners.TryGetValue(source.ChannelId, out var existing))
                {
                    if (existing.Coordinator.CanAcceptConsumers)
                    {
                        if (!existing.Matches(source))
                        {
                            throw new InvalidOperationException(
                                "The active channel owner has a different PCP source endpoint.");
                        }

                        owner = existing;
                    }
                    else
                    {
                        staleOwner = existing;
                    }
                }

                if (staleOwner is null)
                {
                    var created = owner is null;
                    owner ??= CreateOwner(source);
                    ownerLease = owner.Coordinator
                        .TryAcquireApplicationConsumerLease();
                    if (ownerLease is null)
                    {
                        if (created)
                        {
                            unstartedOwner = owner;
                        }
                        else
                        {
                            staleOwner = owner;
                        }
                    }
                    else
                    {
                        try
                        {
                            if (created)
                            {
                                owner.Start(owner.Coordinator.RunAsync(
                                    source.Host,
                                    source.Port,
                                    cancellationToken: CancellationToken.None));
                                _owners.Add(source.ChannelId, owner);
                                owner.SetRetirementTask(
                                    RetireOwnerAsync(source.ChannelId, owner));
                            }

                            candidate = new(
                                _composition.Recording,
                                owner.Coordinator.WaitForReadinessAsync(cancellationToken),
                                owner.Completion ?? throw new InvalidOperationException(
                                    "The shared PCP channel owner has not started."),
                                ownerLease);
                            ownerLease = null;
                        }
                        catch (Exception exception)
                        {
                            attachFailure = exception;
                            if (created)
                            {
                                unstartedOwner = owner;
                            }
                        }
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (staleOwner is not null)
            {
                if (ownerLease is not null)
                {
                    await ownerLease.DisposeAsync().ConfigureAwait(false);
                }

                await (staleOwner.RetirementTask ?? throw new InvalidOperationException(
                    "A published shared PCP owner has no retirement observer."))
                    .ConfigureAwait(false);
                continue;
            }

            if (attachFailure is not null)
            {
                List<Exception>? cleanupFailures = null;
                if (ownerLease is not null)
                {
                    try
                    {
                        await ownerLease.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (cleanupFailures ??= []).Add(exception);
                    }
                }

                if (unstartedOwner is not null)
                {
                    try
                    {
                        await unstartedOwner.ReleaseCoordinatorOnceAsync(_composition)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (cleanupFailures ??= []).Add(exception);
                    }
                }

                if (cleanupFailures is { Count: > 0 })
                {
                    cleanupFailures.Insert(0, attachFailure);
                    throw new AggregateException(
                        "The Recording owner attachment failed and cleanup was incomplete.",
                        cleanupFailures);
                }

                ExceptionDispatchInfo.Capture(attachFailure).Throw();
            }

            if (unstartedOwner is not null)
            {
                await unstartedOwner.ReleaseCoordinatorOnceAsync(_composition)
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The shared PCP channel owner no longer accepts Recording consumers.");
            }

            if (candidate is null)
            {
                throw new InvalidOperationException(
                    "The Recording owner attachment was not created.");
            }

            attachment = candidate;
            break;
        }

        try
        {
            var readiness = await attachment.Readiness
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (readiness != PcpRelayCoordinatorReadinessOutcome.Ready)
            {
                throw new InvalidOperationException(
                    "The shared PCP channel owner did not become ready for Recording.");
            }

            return attachment;
        }
        catch
        {
            await attachment.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        SharedOwner[] owners;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            owners = [.. _owners.Values];
            _owners.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var owner in owners)
        {
            _ = owner.ReleaseCoordinatorOnceAsync(_composition);
        }

        var retirementTasks = owners
            .Select(static owner => owner.RetirementTask ?? throw new InvalidOperationException(
                "A published shared PCP owner has no retirement observer."))
            .ToArray();
        await Task.WhenAll(retirementTasks).ConfigureAwait(false);
    }

    private SharedOwner CreateOwner(DailyPcpPlaybackSource source)
    {
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        var tracker = new ChannelStatusTracker(source.ChannelId, sessionId);
        var coordinator = _composition.CreateCoordinator(
            new TcpTransportConnector(),
            tracker,
            new IPEndPoint(IPAddress.Loopback, 0),
            stopWhenNoConsumers: true);
        return new(source.Host, source.Port, sessionId, coordinator);
    }

    private async Task RetireOwnerAsync(
        ChannelId channelId,
        SharedOwner owner)
    {
        if (owner.Completion is { } completion)
        {
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch
            {
                // A terminal coordinator failure still ends this owner's lifetime.
            }
        }

        await owner.ReleaseCoordinatorOnceAsync(_composition).ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_owners.TryGetValue(channelId, out var current) &&
                ReferenceEquals(current, owner))
            {
                _owners.Remove(channelId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class SharedOwner(
        string host,
        int port,
        ChannelSessionId sessionId,
        PcpRelayCoordinator coordinator)
    {
        private readonly object _releaseGate = new();
        private Task? _releaseTask;

        public string Host { get; } = host;

        public int Port { get; } = port;

        public ChannelSessionId SessionId { get; } = sessionId;

        public PcpRelayCoordinator Coordinator { get; } = coordinator;

        public Task<PcpRelayCoordinatorResult>? Completion { get; private set; }

        public Task? RetirementTask { get; private set; }

        public void Start(Task<PcpRelayCoordinatorResult> completion) =>
            Completion = completion;

        public void SetRetirementTask(Task retirementTask) =>
            RetirementTask = retirementTask;

        public Task ReleaseCoordinatorOnceAsync(
            PcpRecordingRuntimeComposition composition)
        {
            lock (_releaseGate)
            {
                return _releaseTask ??= composition
                    .ReleaseCoordinatorAsync(Coordinator)
                    .AsTask();
            }
        }

        public bool Matches(DailyPcpPlaybackSource source) =>
            Port == source.Port &&
            string.Equals(Host, source.Host, StringComparison.OrdinalIgnoreCase);
    }
}
