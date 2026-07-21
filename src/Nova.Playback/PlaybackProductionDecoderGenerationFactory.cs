namespace PeerCast.Nova.Playback;

public enum PlaybackProductionDecoderGenerationFactoryState
{
    Idle,
    Creating,
    Faulted,
    Disposed,
}

public enum PlaybackProductionDecoderGenerationFactoryResult
{
    Created,
    LicenseReviewPending,
    PolicyRejected,
    InvalidConfiguration,
    SourceDisposed,
    OutputUnavailable,
    BindingRejected,
    VideoRuntimeRejected,
    VideoAbiGateRejected,
    AudioRuntimeRejected,
    AudioAbiGateRejected,
    OwnershipConflict,
    NativeSessionRejected,
    GenerationRejected,
    Cancelled,
    Disposed,
    Faulted,
}

public sealed record PlaybackProductionDecoderGenerationFactoryOptions
{
    public PlaybackSchedulerOptions Scheduler { get; init; } = new();

    public PlaybackPolicyOptions SchedulerPolicy { get; init; } = new();

    public PlaybackAvTimingTolerances Timing { get; init; } = new();

    public PlaybackSourceBoundOneFrameDecodeOptions Decode { get; init; } = new();
}

public readonly record struct PlaybackProductionDecoderGenerationFactorySnapshot(
    PlaybackProductionDecoderGenerationFactoryState State,
    PlaybackProductionDecoderGenerationFactoryResult Result,
    long CreatedGenerationCount,
    bool HasActiveCreation);

public sealed record PlaybackProductionDecoderGenerationCreation(
    PlaybackProductionDecoderGenerationFactoryResult Result,
    PlaybackProductionDecoderGeneration? Generation);

internal enum PlaybackProductionDecoderGenerationStageResult
{
    Created,
    Rejected,
    Cancelled,
    Faulted,
}

internal readonly record struct PlaybackProductionDecoderGenerationOwnedCreation(
    PlaybackProductionDecoderGenerationStageResult Result,
    IDisposable? Owner);

internal readonly record struct PlaybackProductionDecoderGenerationRootCreation(
    PlaybackProductionDecoderGenerationStageResult Result,
    IPlaybackProductionDecoderGenerationRoot? Root);

internal interface IPlaybackProductionDecoderGenerationRoot : IDisposable, IAsyncDisposable
{
    PlaybackBoundedDemuxSession SourceSession { get; }

    int DecoderGeneration { get; }

    int DiscontinuityGeneration { get; }

    bool IsDisposed { get; }

    PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot { get; }

    bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession);

    PlaybackDecodedCompletionSchedulerIngressResult Transfer(
        PlaybackSourceBoundDecodedFrameCompletion? completion,
        CancellationToken cancellationToken);

    ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
        CancellationToken cancellationToken);

    ValueTask StopAsync();
}

internal interface IPlaybackProductionDecoderGenerationFactoryOperations
{
    PlaybackDecoderBackendProvenance Provenance { get; }

    PlaybackProductionDecoderGenerationOwnedCreation PrepareBinding(
        PlaybackBoundedDemuxSession sourceSession);

    ValueTask<PlaybackProductionDecoderGenerationOwnedCreation> LocateRuntimeAsync(
        string runtimeDirectory,
        CancellationToken cancellationToken);

    PlaybackProductionDecoderGenerationOwnedCreation AcquireGate(IDisposable runtimeOwner);

    PlaybackProductionDecoderGenerationOwnedCreation OpenNativeSession(
        IDisposable bindingOwner,
        IDisposable videoGateOwner,
        IDisposable audioGateOwner);

    PlaybackProductionDecoderGenerationRootCreation CreateGenerationRoot(
        PlaybackBoundedDemuxSession sourceSession,
        IDisposable nativeSessionOwner,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackProductionDecoderGenerationFactoryOptions options);
}

public sealed class PlaybackProductionDecoderGeneration : IDisposable, IAsyncDisposable
{
    private IPlaybackProductionDecoderGenerationRoot? _root;

    internal PlaybackProductionDecoderGeneration(
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration,
        IPlaybackProductionDecoderGenerationRoot root)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegative(decoderGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(discontinuityGeneration);

        if (sourceSession.IsDisposed || root.IsDisposed ||
            !root.ReferencesSourceSession(sourceSession) ||
            root.DecoderGeneration != decoderGeneration ||
            root.DiscontinuityGeneration != discontinuityGeneration)
        {
            root.Dispose();
            throw new ArgumentException(
                "The production generation root does not match the requested source generation.",
                nameof(root));
        }

        SourceSession = sourceSession;
        DecoderGeneration = decoderGeneration;
        DiscontinuityGeneration = discontinuityGeneration;
        _root = root;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public int DecoderGeneration { get; }

    public int DiscontinuityGeneration { get; }

    public bool IsDisposed => Volatile.Read(ref _root) is null;

    public PlaybackGenerationMatchedSchedulerDeliverySnapshot? Snapshot =>
        Volatile.Read(ref _root)?.Snapshot;

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
        ReferenceEquals(SourceSession, sourceSession);

    public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
        PlaybackSourceBoundDecodedFrameCompletion? completion,
        CancellationToken cancellationToken = default)
    {
        var root = Volatile.Read(ref _root);
        if (root is null)
        {
            completion?.Dispose();
            return PlaybackDecodedCompletionSchedulerIngressResult.Disposed;
        }

        return root.Transfer(completion, cancellationToken);
    }

    public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
        CancellationToken cancellationToken = default)
    {
        var root = Volatile.Read(ref _root);
        return root is null
            ? ValueTask.FromResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed)
            : root.DeliverOneAsync(cancellationToken);
    }

    public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        var root = Volatile.Read(ref _root);
        return root is null
            ? ValueTask.FromResult(PlaybackGenerationMatchedSchedulerResetResult.Disposed)
            : root.ResetAsync(cancellationToken);
    }

    public async ValueTask StopAsync()
    {
        var root = Interlocked.Exchange(ref _root, null);
        if (root is null)
        {
            return;
        }

        try
        {
            await root.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await root.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        StopAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Finite policy-gated factory for one complete source-bound decode, scheduler,
/// timing, and result-aware output generation. Approved external provenance still
/// requires explicit host policy and verified runtime acquisition. Windows output resources are
/// borrowed and never activated or disposed by this boundary.
/// </summary>
public sealed class PlaybackProductionDecoderGenerationFactory : IDisposable, IAsyncDisposable
{
    private const PlaybackDecoderBackendPolicyRejection KnownPolicyRejections =
        PlaybackDecoderBackendPolicyRejection.LicenseNotApproved |
        PlaybackDecoderBackendPolicyRejection.ThirdPartyNotAllowed |
        PlaybackDecoderBackendPolicyRejection.BundledNotAllowed |
        PlaybackDecoderBackendPolicyRejection.NativeCodeNotAllowed |
        PlaybackDecoderBackendPolicyRejection.InvalidProvenance;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _creationGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly PlaybackDecoderBackendPolicy _policy;
    private readonly PlaybackProductionDecoderGenerationFactoryOptions _options;
    private readonly IPlaybackProductionDecoderGenerationFactoryOperations _operations;

    private PlaybackProductionDecoderGenerationFactoryState _state =
        PlaybackProductionDecoderGenerationFactoryState.Idle;
    private PlaybackProductionDecoderGenerationFactoryResult _result =
        PlaybackProductionDecoderGenerationFactoryResult.InvalidConfiguration;
    private long _createdGenerationCount;
    private bool _hasActiveCreation;
    private int _isDisposed;

    public PlaybackProductionDecoderGenerationFactory(
        PlaybackDecoderBackendPolicy policy,
        PlaybackProductionDecoderGenerationFactoryOptions? options = null,
        IPlaybackMonotonicClock? clock = null)
        : this(
            policy,
            options,
            new PlaybackProductionDecoderGenerationFactoryOperations(
                clock ?? new StopwatchPlaybackMonotonicClock()))
    {
    }

    internal PlaybackProductionDecoderGenerationFactory(
        PlaybackDecoderBackendPolicy policy,
        PlaybackProductionDecoderGenerationFactoryOptions? options,
        IPlaybackProductionDecoderGenerationFactoryOperations operations)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(operations);

        _policy = policy with { };
        _options = CloneAndValidateOptions(options);
        _operations = operations;
    }

    public PlaybackProductionDecoderGenerationFactorySnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(_state, _result, _createdGenerationCount, _hasActiveCreation);
            }
        }
    }

    public async ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
        string runtimeDirectory,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackWindowsOutputBufferProcessingBoundary? output,
        int decoderGeneration,
        int discontinuityGeneration,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return Creation(PlaybackProductionDecoderGenerationFactoryResult.Disposed);
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        try
        {
            await _creationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Creation(
                Volatile.Read(ref _isDisposed) != 0
                    ? PlaybackProductionDecoderGenerationFactoryResult.Disposed
                    : PlaybackProductionDecoderGenerationFactoryResult.Cancelled);
        }

        IDisposable? binding = null;
        IDisposable? videoRuntime = null;
        IDisposable? videoGate = null;
        IDisposable? audioRuntime = null;
        IDisposable? audioGate = null;
        IDisposable? nativeSession = null;
        IPlaybackProductionDecoderGenerationRoot? root = null;
        try
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                return Finish(PlaybackProductionDecoderGenerationFactoryResult.Disposed);
            }

            BeginCreation();
            if (string.IsNullOrWhiteSpace(runtimeDirectory) || sourceSession is null ||
                output is null || decoderGeneration < 0 || discontinuityGeneration < 0)
            {
                return Finish(PlaybackProductionDecoderGenerationFactoryResult.InvalidConfiguration);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var policyResult = EvaluatePolicy(_operations.Provenance);
            if (policyResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(policyResult);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            if (sourceSession.IsDisposed)
            {
                return Finish(PlaybackProductionDecoderGenerationFactoryResult.SourceDisposed);
            }

            if (!IsOutputAvailable(output))
            {
                return Finish(PlaybackProductionDecoderGenerationFactoryResult.OutputUnavailable);
            }

            var bindingCreation = _operations.PrepareBinding(sourceSession);
            var bindingResult = TakeOwner(
                bindingCreation,
                PlaybackProductionDecoderGenerationFactoryResult.BindingRejected,
                out binding);
            if (bindingResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(sourceSession.IsDisposed
                    ? PlaybackProductionDecoderGenerationFactoryResult.SourceDisposed
                    : bindingResult);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var videoRuntimeCreation = await _operations.LocateRuntimeAsync(
                runtimeDirectory,
                linkedCancellation.Token).ConfigureAwait(false);
            var videoRuntimeResult = TakeOwner(
                videoRuntimeCreation,
                PlaybackProductionDecoderGenerationFactoryResult.VideoRuntimeRejected,
                out videoRuntime);
            if (videoRuntimeResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(videoRuntimeResult);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var videoGateCreation = _operations.AcquireGate(videoRuntime!);
            var videoGateResult = TakeOwner(
                videoGateCreation,
                PlaybackProductionDecoderGenerationFactoryResult.VideoAbiGateRejected,
                out videoGate);
            if (videoGateResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(videoGateResult);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var audioRuntimeCreation = await _operations.LocateRuntimeAsync(
                runtimeDirectory,
                linkedCancellation.Token).ConfigureAwait(false);
            var audioRuntimeResult = TakeOwner(
                audioRuntimeCreation,
                PlaybackProductionDecoderGenerationFactoryResult.AudioRuntimeRejected,
                out audioRuntime);
            if (audioRuntimeResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(audioRuntimeResult);
            }

            if (ReferenceEquals(videoRuntime, audioRuntime))
            {
                return Finish(PlaybackProductionDecoderGenerationFactoryResult.OwnershipConflict);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var audioGateCreation = _operations.AcquireGate(audioRuntime!);
            var audioGateResult = TakeOwner(
                audioGateCreation,
                PlaybackProductionDecoderGenerationFactoryResult.AudioAbiGateRejected,
                out audioGate);
            if (audioGateResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(audioGateResult);
            }

            if (ReferenceEquals(videoGate, audioGate))
            {
                return Finish(PlaybackProductionDecoderGenerationFactoryResult.OwnershipConflict);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var nativeCreation = _operations.OpenNativeSession(
                binding!,
                videoGate!,
                audioGate!);
            binding = null;
            videoGate = null;
            audioGate = null;
            var nativeResult = TakeOwner(
                nativeCreation,
                PlaybackProductionDecoderGenerationFactoryResult.NativeSessionRejected,
                out nativeSession);
            if (nativeResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(sourceSession.IsDisposed
                    ? PlaybackProductionDecoderGenerationFactoryResult.SourceDisposed
                    : nativeResult);
            }

            // A successfully opened source-bound session owns both complete
            // context→gate→runtime chains. The separate runtime references are
            // relinquished only after that transfer has succeeded.
            videoRuntime = null;
            audioRuntime = null;

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var rootCreation = _operations.CreateGenerationRoot(
                sourceSession,
                nativeSession!,
                decoderGeneration,
                discontinuityGeneration,
                output,
                _options);
            nativeSession = null;
            var rootResult = TakeRoot(
                rootCreation,
                PlaybackProductionDecoderGenerationFactoryResult.GenerationRejected,
                out root);
            if (rootResult != PlaybackProductionDecoderGenerationFactoryResult.Created)
            {
                return Finish(sourceSession.IsDisposed
                    ? PlaybackProductionDecoderGenerationFactoryResult.SourceDisposed
                    : rootResult);
            }

            if (root!.IsDisposed || !root.ReferencesSourceSession(sourceSession) ||
                root.DecoderGeneration != decoderGeneration ||
                root.DiscontinuityGeneration != discontinuityGeneration ||
                sourceSession.IsDisposed || !IsOutputAvailable(output))
            {
                await DisposeRootAsync(root).ConfigureAwait(false);
                root = null;
                return Finish(sourceSession.IsDisposed
                    ? PlaybackProductionDecoderGenerationFactoryResult.SourceDisposed
                    : PlaybackProductionDecoderGenerationFactoryResult.GenerationRejected);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            var generation = new PlaybackProductionDecoderGeneration(
                sourceSession,
                decoderGeneration,
                discontinuityGeneration,
                root);
            root = null;
            return Finish(PlaybackProductionDecoderGenerationFactoryResult.Created, generation);
        }
        catch (OperationCanceledException)
        {
            if (root is not null)
            {
                await DisposeRootAsync(root).ConfigureAwait(false);
                root = null;
            }

            return Finish(
                Volatile.Read(ref _isDisposed) != 0
                    ? PlaybackProductionDecoderGenerationFactoryResult.Disposed
                    : PlaybackProductionDecoderGenerationFactoryResult.Cancelled);
        }
        catch
        {
            if (root is not null)
            {
                await DisposeRootAsync(root).ConfigureAwait(false);
                root = null;
            }

            return Finish(PlaybackProductionDecoderGenerationFactoryResult.Faulted);
        }
        finally
        {
            DisposeOwnedReverse(
                nativeSession,
                audioGate,
                audioRuntime,
                videoGate,
                videoRuntime,
                binding);
            EndCreation();
            _creationGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        _creationGate.Wait();
        try
        {
            lock (_sync)
            {
                _state = PlaybackProductionDecoderGenerationFactoryState.Disposed;
                _result = PlaybackProductionDecoderGenerationFactoryResult.Disposed;
                _hasActiveCreation = false;
            }
        }
        finally
        {
            _creationGate.Release();
            _creationGate.Dispose();
            _disposeCancellation.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        await _creationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                _state = PlaybackProductionDecoderGenerationFactoryState.Disposed;
                _result = PlaybackProductionDecoderGenerationFactoryResult.Disposed;
                _hasActiveCreation = false;
            }
        }
        finally
        {
            _creationGate.Release();
            _creationGate.Dispose();
            _disposeCancellation.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private PlaybackProductionDecoderGenerationFactoryResult EvaluatePolicy(
        PlaybackDecoderBackendProvenance provenance)
    {
        if (!Enum.IsDefined(provenance.Origin) ||
            !Enum.IsDefined(provenance.Distribution) ||
            !Enum.IsDefined(provenance.LicenseReview))
        {
            return PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected;
        }

        if (provenance.LicenseReview == PlaybackDecoderBackendLicenseReview.Pending)
        {
            return PlaybackProductionDecoderGenerationFactoryResult.LicenseReviewPending;
        }

        if (provenance.LicenseReview != PlaybackDecoderBackendLicenseReview.Approved)
        {
            return PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected;
        }

        var rejection = _policy.Evaluate(provenance);
        if ((rejection & ~KnownPolicyRejections) != 0)
        {
            return PlaybackProductionDecoderGenerationFactoryResult.Faulted;
        }

        return rejection == PlaybackDecoderBackendPolicyRejection.None
            ? PlaybackProductionDecoderGenerationFactoryResult.Created
            : PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected;
    }

    private static PlaybackProductionDecoderGenerationFactoryResult TakeOwner(
        PlaybackProductionDecoderGenerationOwnedCreation creation,
        PlaybackProductionDecoderGenerationFactoryResult rejection,
        out IDisposable? owner)
    {
        owner = creation.Owner;
        if (!Enum.IsDefined(creation.Result))
        {
            return PlaybackProductionDecoderGenerationFactoryResult.Faulted;
        }

        if (creation.Result == PlaybackProductionDecoderGenerationStageResult.Created)
        {
            return owner is null
                ? PlaybackProductionDecoderGenerationFactoryResult.Faulted
                : PlaybackProductionDecoderGenerationFactoryResult.Created;
        }

        if (owner is not null)
        {
            return PlaybackProductionDecoderGenerationFactoryResult.Faulted;
        }

        return creation.Result switch
        {
            PlaybackProductionDecoderGenerationStageResult.Rejected => rejection,
            PlaybackProductionDecoderGenerationStageResult.Cancelled =>
                PlaybackProductionDecoderGenerationFactoryResult.Cancelled,
            PlaybackProductionDecoderGenerationStageResult.Faulted =>
                PlaybackProductionDecoderGenerationFactoryResult.Faulted,
            _ => PlaybackProductionDecoderGenerationFactoryResult.Faulted,
        };
    }

    private static PlaybackProductionDecoderGenerationFactoryResult TakeRoot(
        PlaybackProductionDecoderGenerationRootCreation creation,
        PlaybackProductionDecoderGenerationFactoryResult rejection,
        out IPlaybackProductionDecoderGenerationRoot? root)
    {
        root = creation.Root;
        if (!Enum.IsDefined(creation.Result))
        {
            return PlaybackProductionDecoderGenerationFactoryResult.Faulted;
        }

        if (creation.Result == PlaybackProductionDecoderGenerationStageResult.Created)
        {
            return root is null
                ? PlaybackProductionDecoderGenerationFactoryResult.Faulted
                : PlaybackProductionDecoderGenerationFactoryResult.Created;
        }

        if (root is not null)
        {
            return PlaybackProductionDecoderGenerationFactoryResult.Faulted;
        }

        return creation.Result switch
        {
            PlaybackProductionDecoderGenerationStageResult.Rejected => rejection,
            PlaybackProductionDecoderGenerationStageResult.Cancelled =>
                PlaybackProductionDecoderGenerationFactoryResult.Cancelled,
            PlaybackProductionDecoderGenerationStageResult.Faulted =>
                PlaybackProductionDecoderGenerationFactoryResult.Faulted,
            _ => PlaybackProductionDecoderGenerationFactoryResult.Faulted,
        };
    }

    private static bool IsOutputAvailable(PlaybackWindowsOutputBufferProcessingBoundary output)
    {
        var snapshot = output.Snapshot;
        return snapshot.VideoState != PlaybackWindowsOutputBufferState.Disposed &&
            snapshot.AudioState != PlaybackWindowsOutputBufferState.Disposed;
    }

    private void BeginCreation()
    {
        lock (_sync)
        {
            _state = PlaybackProductionDecoderGenerationFactoryState.Creating;
            _hasActiveCreation = true;
        }
    }

    private void EndCreation()
    {
        lock (_sync)
        {
            _hasActiveCreation = false;
            if (_state == PlaybackProductionDecoderGenerationFactoryState.Creating)
            {
                _state = PlaybackProductionDecoderGenerationFactoryState.Idle;
            }
        }
    }

    private PlaybackProductionDecoderGenerationCreation Finish(
        PlaybackProductionDecoderGenerationFactoryResult result,
        PlaybackProductionDecoderGeneration? generation = null)
    {
        if (!Enum.IsDefined(result) ||
            (result == PlaybackProductionDecoderGenerationFactoryResult.Created) !=
            (generation is not null))
        {
            generation?.Dispose();
            result = PlaybackProductionDecoderGenerationFactoryResult.Faulted;
            generation = null;
        }

        lock (_sync)
        {
            _result = result;
            _state = result == PlaybackProductionDecoderGenerationFactoryResult.Faulted
                ? PlaybackProductionDecoderGenerationFactoryState.Faulted
                : Volatile.Read(ref _isDisposed) != 0
                    ? PlaybackProductionDecoderGenerationFactoryState.Disposed
                    : PlaybackProductionDecoderGenerationFactoryState.Idle;
            if (result == PlaybackProductionDecoderGenerationFactoryResult.Created &&
                _createdGenerationCount < long.MaxValue)
            {
                _createdGenerationCount++;
            }
        }

        return new(result, generation);
    }

    private PlaybackProductionDecoderGenerationCreation Creation(
        PlaybackProductionDecoderGenerationFactoryResult result) => new(result, null);

    private static async ValueTask DisposeRootAsync(
        IPlaybackProductionDecoderGenerationRoot root)
    {
        try
        {
            await root.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Cleanup continues through the idempotent root disposal boundary.
        }

        try
        {
            await root.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // A fixed factory result never exposes cleanup exceptions.
        }
    }

    private static void DisposeOwnedReverse(params IDisposable?[] owners)
    {
        var disposed = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        foreach (var owner in owners)
        {
            if (owner is null || !disposed.Add(owner))
            {
                continue;
            }

            try
            {
                owner.Dispose();
            }
            catch
            {
                // Partial construction cleanup remains best effort and fail closed.
            }
        }
    }

    private static PlaybackProductionDecoderGenerationFactoryOptions CloneAndValidateOptions(
        PlaybackProductionDecoderGenerationFactoryOptions? options)
    {
        options ??= new PlaybackProductionDecoderGenerationFactoryOptions();
        ArgumentNullException.ThrowIfNull(options.Scheduler);
        ArgumentNullException.ThrowIfNull(options.SchedulerPolicy);
        ArgumentNullException.ThrowIfNull(options.Timing);
        ArgumentNullException.ThrowIfNull(options.Decode);

        var clone = new PlaybackProductionDecoderGenerationFactoryOptions
        {
            Scheduler = options.Scheduler with { },
            SchedulerPolicy = options.SchedulerPolicy with { },
            Timing = options.Timing with { },
            Decode = options.Decode with { },
        };
        ValidateOptions(clone);
        return clone;
    }

    private static void ValidateOptions(PlaybackProductionDecoderGenerationFactoryOptions options)
    {
        if (options.Scheduler.Capacity is < 1 or > PlaybackSchedulerOptions.MaximumCapacity ||
            options.Scheduler.MaximumOwnedBytes is < 1 or
                > PlaybackSchedulerOptions.MaximumOwnedBytesHardLimit ||
            !Enum.IsDefined(options.Scheduler.FullPolicy) ||
            !Enum.IsDefined(options.SchedulerPolicy.ClockMaster) ||
            !IsPositiveBounded(options.SchedulerPolicy.MaximumWait,
                PlaybackPolicyOptions.MaximumWaitLimit) ||
            !IsPositiveBounded(options.SchedulerPolicy.MaximumLateness,
                PlaybackPolicyOptions.MaximumLatenessLimit) ||
            !IsPositiveBounded(options.SchedulerPolicy.MaximumLead,
                PlaybackPolicyOptions.MaximumLeadLimit) ||
            options.Decode.MaximumManagedFrameBytes is < 1 or
                > PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit ||
            !IsNonNegativeBounded(options.Timing.VideoEarlyTolerance,
                PlaybackAvTimingTolerances.MaximumTolerance) ||
            !IsNonNegativeBounded(options.Timing.VideoLateTolerance,
                PlaybackAvTimingTolerances.MaximumTolerance) ||
            !IsNonNegativeBounded(options.Timing.AudioEarlyTolerance,
                PlaybackAvTimingTolerances.MaximumTolerance) ||
            !IsNonNegativeBounded(options.Timing.AudioLateTolerance,
                PlaybackAvTimingTolerances.MaximumTolerance) ||
            !IsPositiveBounded(options.Timing.MaximumWait,
                PlaybackAvTimingTolerances.MaximumWaitLimit) ||
            options.Timing.VideoEarlyTolerance > options.Timing.MaximumWait ||
            options.Timing.AudioEarlyTolerance > options.Timing.MaximumWait)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static bool IsPositiveBounded(TimeSpan value, TimeSpan maximum) =>
        value > TimeSpan.Zero && value <= maximum;

    private static bool IsNonNegativeBounded(TimeSpan value, TimeSpan maximum) =>
        value >= TimeSpan.Zero && value <= maximum;
}

internal sealed class PlaybackProductionDecoderGenerationFactoryOperations(
    IPlaybackMonotonicClock clock) : IPlaybackProductionDecoderGenerationFactoryOperations
{
    private readonly PlaybackBoundedFlvDecoderParameterBindingAdapter _binding = new();
    private readonly PlaybackDecoderFfmpegAbiSymbolGate _gate = new();
    private readonly PlaybackBoundedFlvNativeDecoderSessionFactory _nativeSession = new();

    public PlaybackDecoderBackendProvenance Provenance =>
        PlaybackFfmpegBackendCandidate.Provenance;

    public PlaybackProductionDecoderGenerationOwnedCreation PrepareBinding(
        PlaybackBoundedDemuxSession sourceSession)
    {
        var preparation = _binding.Prepare(sourceSession);
        return preparation.Result switch
        {
            PlaybackBoundedFlvDecoderParameterBindingResult.Prepared
                when preparation.Binding is not null => Created(preparation.Binding),
            PlaybackBoundedFlvDecoderParameterBindingResult.Faulted => Faulted(),
            _ => Rejected(),
        };
    }

    public async ValueTask<PlaybackProductionDecoderGenerationOwnedCreation> LocateRuntimeAsync(
        string runtimeDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var location = await PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync(
                runtimeDirectory,
                PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate,
                cancellationToken).ConfigureAwait(false);
            return location.Location == PlaybackDecoderBackendInstalledRuntimeLocation.Verified &&
                location.Lease is not null
                ? Created(location.Lease)
                : Rejected();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch
        {
            return Faulted();
        }
    }

    public PlaybackProductionDecoderGenerationOwnedCreation AcquireGate(
        IDisposable runtimeOwner)
    {
        if (runtimeOwner is not VerifiedPlaybackDecoderRuntimeLease runtime)
        {
            return Faulted();
        }

        try
        {
            var acquisition = _gate.Acquire(runtime);
            if (acquisition.Result == PlaybackDecoderFfmpegAbiSymbolGateResult.Verified &&
                acquisition.Lease is not null)
            {
                return Created(acquisition.Lease);
            }

            return acquisition.Result == PlaybackDecoderFfmpegAbiSymbolGateResult.ReleaseFailed
                ? Faulted()
                : Rejected();
        }
        catch
        {
            return Faulted();
        }
    }

    public PlaybackProductionDecoderGenerationOwnedCreation OpenNativeSession(
        IDisposable bindingOwner,
        IDisposable videoGateOwner,
        IDisposable audioGateOwner)
    {
        if (bindingOwner is not PlaybackBoundedFlvDecoderParameterBinding binding ||
            videoGateOwner is not PlaybackDecoderFfmpegAbiSymbolGateLease videoGate ||
            audioGateOwner is not PlaybackDecoderFfmpegAbiSymbolGateLease audioGate)
        {
            bindingOwner.Dispose();
            videoGateOwner.Dispose();
            audioGateOwner.Dispose();
            return Faulted();
        }

        try
        {
            var opening = _nativeSession.Open(binding, videoGate, audioGate);
            return opening.Result switch
            {
                PlaybackBoundedFlvNativeDecoderSessionResult.Opened
                    when opening.Session is not null => Created(opening.Session),
                PlaybackBoundedFlvNativeDecoderSessionResult.Faulted => Faulted(),
                _ => Rejected(),
            };
        }
        catch
        {
            binding.Dispose();
            videoGate.Dispose();
            audioGate.Dispose();
            return Faulted();
        }
    }

    public PlaybackProductionDecoderGenerationRootCreation CreateGenerationRoot(
        PlaybackBoundedDemuxSession sourceSession,
        IDisposable nativeSessionOwner,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackProductionDecoderGenerationFactoryOptions options)
    {
        if (nativeSessionOwner is not PlaybackBoundedFlvNativeDecoderSession nativeSession)
        {
            nativeSessionOwner.Dispose();
            return RootFaulted();
        }

        PlaybackBoundedFlvNativeDecoderSession? ownedNative = nativeSession;
        PlaybackSourceBoundOneFrameDecodeBoundary? decode = null;
        BoundedPlaybackScheduler? scheduler = null;
        PlaybackAvTimingDecisionBoundary? timing = null;
        try
        {
            decode = new(
                sourceSession,
                ownedNative,
                decoderGeneration,
                discontinuityGeneration,
                options.Decode);
            ownedNative = null;

            var schedulerPolicy = new PlaybackPolicyClockBoundary(
                clock,
                options.SchedulerPolicy);
            scheduler = new(options.Scheduler, schedulerPolicy);
            timing = new(
                clock,
                options.Timing,
                decoderGeneration,
                decoderGeneration);
            var delivery = new PlaybackGenerationMatchedSchedulerDeliveryBoundary(
                sourceSession,
                decode,
                nativeSession,
                decoderGeneration,
                discontinuityGeneration,
                scheduler,
                timing,
                output,
                initialSchedulerSequence: 0);
            decode = null;
            scheduler = null;
            timing = null;
            return new(
                PlaybackProductionDecoderGenerationStageResult.Created,
                new PlaybackProductionDecoderGenerationRoot(
                    sourceSession,
                    decoderGeneration,
                    discontinuityGeneration,
                    delivery));
        }
        catch
        {
            try
            {
                timing?.Dispose();
            }
            finally
            {
                try
                {
                    scheduler?.Dispose();
                }
                finally
                {
                    try
                    {
                        decode?.Dispose();
                    }
                    finally
                    {
                        ownedNative?.Dispose();
                    }
                }
            }

            return RootFaulted();
        }
    }

    private static PlaybackProductionDecoderGenerationOwnedCreation Created(IDisposable owner) =>
        new(PlaybackProductionDecoderGenerationStageResult.Created, owner);

    private static PlaybackProductionDecoderGenerationOwnedCreation Rejected() =>
        new(PlaybackProductionDecoderGenerationStageResult.Rejected, null);

    private static PlaybackProductionDecoderGenerationOwnedCreation Cancelled() =>
        new(PlaybackProductionDecoderGenerationStageResult.Cancelled, null);

    private static PlaybackProductionDecoderGenerationOwnedCreation Faulted() =>
        new(PlaybackProductionDecoderGenerationStageResult.Faulted, null);

    private static PlaybackProductionDecoderGenerationRootCreation RootFaulted() =>
        new(PlaybackProductionDecoderGenerationStageResult.Faulted, null);
}

internal sealed class PlaybackProductionDecoderGenerationRoot :
    IPlaybackProductionDecoderGenerationRoot
{
    private PlaybackGenerationMatchedSchedulerDeliveryBoundary? _delivery;

    internal PlaybackProductionDecoderGenerationRoot(
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackGenerationMatchedSchedulerDeliveryBoundary delivery)
    {
        SourceSession = sourceSession;
        DecoderGeneration = decoderGeneration;
        DiscontinuityGeneration = discontinuityGeneration;
        _delivery = delivery;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public int DecoderGeneration { get; }

    public int DiscontinuityGeneration { get; }

    public bool IsDisposed => Volatile.Read(ref _delivery) is null;

    public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot
    {
        get
        {
            var delivery = Volatile.Read(ref _delivery);
            ObjectDisposedException.ThrowIf(delivery is null, this);
            return delivery.Snapshot;
        }
    }

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
        ReferenceEquals(SourceSession, sourceSession);

    public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
        PlaybackSourceBoundDecodedFrameCompletion? completion,
        CancellationToken cancellationToken)
    {
        var delivery = Volatile.Read(ref _delivery);
        if (delivery is null)
        {
            completion?.Dispose();
            return PlaybackDecodedCompletionSchedulerIngressResult.Disposed;
        }

        return delivery.Transfer(completion, cancellationToken);
    }

    public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
        CancellationToken cancellationToken)
    {
        var delivery = Volatile.Read(ref _delivery);
        return delivery is null
            ? ValueTask.FromResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed)
            : delivery.DeliverOneAsync(cancellationToken);
    }

    public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
        CancellationToken cancellationToken)
    {
        var delivery = Volatile.Read(ref _delivery);
        return delivery is null
            ? ValueTask.FromResult(PlaybackGenerationMatchedSchedulerResetResult.Disposed)
            : delivery.ResetAsync(cancellationToken);
    }

    public async ValueTask StopAsync()
    {
        var delivery = Interlocked.Exchange(ref _delivery, null);
        if (delivery is not null)
        {
            await delivery.StopAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        StopAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
