using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Playback;

/// <summary>
/// Finite production-generation factory using one immutable built-in or host-supplied
/// external LGPL runtime manifest. All runtime files still pass through the existing
/// explicit-directory, retained-handle, SHA-256, PE dependency, ABI, and symbol gates.
/// </summary>
public sealed class PlaybackFfmpegExternalRuntimeGenerationFactory :
    IDisposable,
    IAsyncDisposable
{
    private readonly PlaybackProductionDecoderGenerationFactory _factory;
    private readonly bool _policyRejected;

    public PlaybackFfmpegExternalRuntimeGenerationFactory(
        PlaybackDecoderBackendPolicy policy,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
        PlaybackProductionDecoderGenerationFactoryOptions? options = null,
        IPlaybackMonotonicClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(runtimeManifest);

        RuntimeManifest = runtimeManifest;
        _policyRejected = EvaluateManifestPolicy(policy, runtimeManifest) !=
            PlaybackDecoderBackendPolicyRejection.None;
        var standardOperations = new PlaybackProductionDecoderGenerationFactoryOperations(
            clock ?? new StopwatchPlaybackMonotonicClock());
        _factory = new PlaybackProductionDecoderGenerationFactory(
            policy,
            options,
            new PlaybackFfmpegExternalRuntimeGenerationFactoryOperations(
                runtimeManifest,
                standardOperations));
    }

    internal PlaybackFfmpegExternalRuntimeGenerationFactory(
        PlaybackDecoderBackendPolicy policy,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
        PlaybackProductionDecoderGenerationFactoryOptions? options,
        IPlaybackProductionDecoderGenerationFactoryOperations standardOperations)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(runtimeManifest);
        ArgumentNullException.ThrowIfNull(standardOperations);

        RuntimeManifest = runtimeManifest;
        _policyRejected = EvaluateManifestPolicy(policy, runtimeManifest) !=
            PlaybackDecoderBackendPolicyRejection.None;
        _factory = new PlaybackProductionDecoderGenerationFactory(
            policy,
            options,
            new PlaybackFfmpegExternalRuntimeGenerationFactoryOperations(
                runtimeManifest,
                standardOperations));
    }

    public PlaybackFfmpegExternalRuntimeManifest RuntimeManifest { get; }

    public PlaybackProductionDecoderGenerationFactorySnapshot Snapshot => _policyRejected
        ? new(
            PlaybackProductionDecoderGenerationFactoryState.Idle,
            PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected,
            0,
            HasActiveCreation: false)
        : _factory.Snapshot;

    public ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
        string runtimeDirectory,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackWindowsOutputBufferProcessingBoundary? output,
        int decoderGeneration,
        int discontinuityGeneration,
        CancellationToken cancellationToken = default)
    {
        if (_policyRejected)
        {
            return ValueTask.FromResult(new PlaybackProductionDecoderGenerationCreation(
                PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected,
                null));
        }

        return _factory.CreateAsync(
            runtimeDirectory,
            sourceSession,
            output,
            decoderGeneration,
            discontinuityGeneration,
            cancellationToken);
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private static PlaybackDecoderBackendPolicyRejection EvaluateManifestPolicy(
        PlaybackDecoderBackendPolicy policy,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest) =>
        runtimeManifest.IsHostSupplied
            ? policy.EvaluateHostAttestedRuntime(runtimeManifest.Provenance)
            : policy.Evaluate(runtimeManifest.Provenance);
}

/// <summary>
/// Application-composition-root entry for the reviewed built-in or explicitly
/// host-attested external FFmpeg runtime route. It adds no runtime discovery,
/// download, extraction, installation, fallback, queue, resident loop, or
/// Windows resource ownership.
/// </summary>
public static class PlaybackFfmpegExternalRuntimeComposition
{
    public static PlaybackProductionPcpFlvCompositionBoundary Create(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        string runtimeDirectory,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackDecoderBackendPolicy policy,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
        long initialPacketSequence = 0,
        PlaybackPcpFlvAwaitedDecodeOptions? options = null,
        PlaybackBoundedFlvParserOptions? parserOptions = null,
        PlaybackProductionDecoderGenerationFactoryOptions? generationOptions = null,
        IPlaybackMonotonicClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeManifest);
        var factory = new PlaybackFfmpegExternalRuntimeGenerationFactory(
            policy,
            runtimeManifest,
            generationOptions,
            clock);
        return new PlaybackProductionPcpFlvCompositionBoundary(
            channelId,
            channelSessionId,
            runtimeDirectory,
            output,
            new PlaybackFfmpegExternalRuntimeGenerationCreator(factory),
            audioNativeStageReporter: null,
            initialPacketSequence,
            options,
            parserOptions);
    }
}

internal sealed class PlaybackFfmpegExternalRuntimeGenerationCreator(
    PlaybackFfmpegExternalRuntimeGenerationFactory factory) :
    IPlaybackProductionDecoderGenerationCreator
{
    public ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
        string runtimeDirectory,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackWindowsOutputBufferProcessingBoundary? output,
        int decoderGeneration,
        int discontinuityGeneration,
        CancellationToken cancellationToken) =>
        factory.CreateAsync(
            runtimeDirectory,
            sourceSession,
            output,
            decoderGeneration,
            discontinuityGeneration,
            cancellationToken);

    public void Dispose() => factory.Dispose();

    public ValueTask DisposeAsync() => factory.DisposeAsync();
}

internal sealed class PlaybackFfmpegExternalRuntimeGenerationFactoryOperations(
    PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
    IPlaybackProductionDecoderGenerationFactoryOperations standardOperations) :
    IPlaybackProductionDecoderGenerationFactoryOperations
{
    /// <summary>
    /// The generic production factory validates the reviewed FFmpeg ABI/backend class.
    /// A host runtime's separate unreviewed declaration and explicit attestation are
    /// evaluated by PlaybackFfmpegExternalRuntimeGenerationFactory before this adapter
    /// can be reached; they are never reported as a Nova review approval.
    /// </summary>
    public PlaybackDecoderBackendProvenance Provenance => runtimeManifest.IsHostSupplied
        ? PlaybackFfmpegBackendCandidate.Provenance
        : runtimeManifest.Provenance;

    public PlaybackProductionDecoderGenerationOwnedCreation PrepareBinding(
        PlaybackBoundedDemuxSession sourceSession) =>
        standardOperations.PrepareBinding(sourceSession);

    public async ValueTask<PlaybackProductionDecoderGenerationOwnedCreation> LocateRuntimeAsync(
        string runtimeDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var location = await PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync(
                runtimeDirectory,
                runtimeManifest.RuntimeInventory,
                cancellationToken).ConfigureAwait(false);
            return location.Location == PlaybackDecoderBackendInstalledRuntimeLocation.Verified &&
                location.Lease is not null
                ? new(
                    PlaybackProductionDecoderGenerationStageResult.Created,
                    location.Lease)
                : new(
                    PlaybackProductionDecoderGenerationStageResult.Rejected,
                    null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(PlaybackProductionDecoderGenerationStageResult.Cancelled, null);
        }
        catch
        {
            return new(PlaybackProductionDecoderGenerationStageResult.Faulted, null);
        }
    }

    public PlaybackProductionDecoderGenerationOwnedCreation AcquireGate(
        IDisposable runtimeOwner) =>
        standardOperations.AcquireGate(runtimeOwner);

    public PlaybackProductionDecoderGenerationOwnedCreation OpenNativeSession(
        IDisposable bindingOwner,
        IDisposable videoGateOwner,
        IDisposable audioGateOwner) =>
        standardOperations.OpenNativeSession(
            bindingOwner,
            videoGateOwner,
            audioGateOwner);

    public PlaybackProductionDecoderGenerationRootCreation CreateGenerationRoot(
        PlaybackBoundedDemuxSession sourceSession,
        IDisposable nativeSessionOwner,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackProductionDecoderGenerationFactoryOptions options) =>
        standardOperations.CreateGenerationRoot(
            sourceSession,
            nativeSessionOwner,
            decoderGeneration,
            discontinuityGeneration,
            output,
            options);
}
