using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

public enum PlaybackPcpFmp4CompositionState
{
    Accepting,
    Completed,
    Faulted,
    Disposed,
}

public enum PlaybackPcpFmp4CompositionResult
{
    PacketAccepted,
    Completed,
    InvalidIdentity,
    SequenceRejected,
    UnsupportedProfile,
    InitializationUnavailable,
    InvalidSegment,
    AdapterRejected,
    DecoderRejected,
    TruncatedInput,
    Cancelled,
    Faulted,
    Disposed,
}

public sealed record PlaybackPcpFmp4CompositionOptions
{
    public long InitialPacketSequence { get; init; }

    public int MaximumSegmentBytes { get; init; } = 16 * 1024 * 1024;

    internal bool IsValid => InitialPacketSequence >= 0 &&
        MaximumSegmentBytes is > 0 &&
        MaximumSegmentBytes <=
            PlaybackBoundedDemuxOptions.MaximumInputBytesPerReadHardLimit;
}
public sealed record PlaybackPcpFmp4CompositionSnapshot(
    PlaybackPcpFmp4CompositionState State,
    PlaybackPcpFmp4CompositionResult Result,
    long AcceptedPacketCount,
    long HeadPacketCount,
    long DataPacketCount,
    long IgnoredPacketCount,
    long PayloadByteCount,
    int AssemblerCarryBytes,
    long InitializationGenerationCount,
    long MediaSegmentCount,
    long BatchCount,
    long DecoderSubmissionCount,
    long DiscontinuityCount,
    long NextPacketSequence,
    long? CurrentPresentationGeneration);

public interface IPlaybackPcpFmp4PresentationOutputGeneration :
    IPlaybackDecodedFrameSink,
    IAsyncDisposable
{
    ValueTask CompleteAsync(CancellationToken cancellationToken);
}

public interface IPlaybackPcpFmp4PresentationOutputGenerationFactory
{
    IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation);
}

/// <summary>
/// Owns PCP packet validation, bounded fMP4 reassembly, and one decoder/output
/// generation. It borrows the decoder factory and metadata provider; the
/// presentation generations it creates are owned until replacement or dispose.
/// </summary>
public sealed class PlaybackPcpFmp4CompositionBoundary :
    IOwnedPcpChannelPacketConsumer
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotGate = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly IPlaybackDecoderFactory _decoderFactory;
    private readonly IPlaybackPcpFmp4PresentationOutputGenerationFactory
        _presentationFactory;
    private readonly Func<ChannelMetadataSnapshot?>? _metadataProvider;
    private readonly PlaybackPcpFmp4SegmentAssembler _assembler;
    private readonly int _maximumSegmentBytes;
    private long _nextPacketSequence;
    private byte[]? _initializationSegment;
    private Generation? _generation;
    private PlaybackPcpFmp4CompositionState _state =
        PlaybackPcpFmp4CompositionState.Accepting;
    private PlaybackPcpFmp4CompositionResult _result =
        PlaybackPcpFmp4CompositionResult.PacketAccepted;
    private long _acceptedPacketCount;
    private long _headPacketCount;
    private long _dataPacketCount;
    private long _ignoredPacketCount;
    private long _payloadByteCount;
    private int _assemblerCarryBytes;
    private long _initializationGenerationCount;
    private long _mediaSegmentCount;
    private long _batchCount;
    private long _decoderSubmissionCount;
    private long _discontinuityCount;
    private long _nextPresentationGeneration;
    private long? _currentPresentationGeneration;
    private int _disposed;

    public PlaybackPcpFmp4CompositionBoundary(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IPlaybackDecoderFactory decoderFactory,
        IPlaybackPcpFmp4PresentationOutputGenerationFactory presentationFactory,
        Func<ChannelMetadataSnapshot?>? metadataProvider = null,
        PlaybackPcpFmp4CompositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(decoderFactory);
        ArgumentNullException.ThrowIfNull(presentationFactory);

        options ??= new PlaybackPcpFmp4CompositionOptions();
        if (!options.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _channelId = channelId;
        _sessionId = sessionId;
        _decoderFactory = decoderFactory;
        _presentationFactory = presentationFactory;
        _metadataProvider = metadataProvider;
        _maximumSegmentBytes = options.MaximumSegmentBytes;
        _assembler = new(new()
        {
            MaximumSegmentBytes = options.MaximumSegmentBytes,
        });
        _nextPacketSequence = options.InitialPacketSequence;
    }

    public PlaybackPcpFmp4CompositionSnapshot Snapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return new(
                    _state,
                    _result,
                    _acceptedPacketCount,
                    _headPacketCount,
                    _dataPacketCount,
                    _ignoredPacketCount,
                    _payloadByteCount,
                    _assemblerCarryBytes,
                    _initializationGenerationCount,
                    _mediaSegmentCount,
                    _batchCount,
                    _decoderSubmissionCount,
                    _discontinuityCount,
                    _nextPacketSequence,
                    _currentPresentationGeneration);
            }
        }
    }

    public async ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAccepting();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateIdentityAndSequence(packet);

            if (packet.Discontinuity is not null)
            {
                Update(() => _discontinuityCount = checked(_discontinuityCount + 1));
                ResetAssembler();
                await ResetForDiscontinuityAsync(
                    packet.Type != HeadPacketType,
                    cancellationToken).ConfigureAwait(false);
            }

            if (packet.Type == HeadPacketType)
            {
                Update(() => _headPacketCount = checked(_headPacketCount + 1));
                var continuesInitialization = _assembler.HasPartialSegment &&
                    _assembler.PartialSegmentKind ==
                        PlaybackFmp4LiveSegmentKind.Initialization;
                if (!continuesInitialization)
                {
                    ResetAssembler();
                    await ReplaceGenerationForNewHeadAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                await _assembler.AppendAsync(
                    packet.Type,
                    packet.Payload,
                    HandleAssembledSegmentAsync,
                    cancellationToken).ConfigureAwait(false);
                Update(() => _assemblerCarryBytes = _assembler.CarryByteCount);
            }
            else if (packet.Type == DataPacketType)
            {
                Update(() => _dataPacketCount = checked(_dataPacketCount + 1));
                if (_generation is null)
                {
                    throw Error(
                        PlaybackPcpFmp4CompositionResult.InitializationUnavailable);
                }

                await _assembler.AppendAsync(
                    packet.Type,
                    packet.Payload,
                    HandleAssembledSegmentAsync,
                    cancellationToken).ConfigureAwait(false);
                Update(() => _assemblerCarryBytes = _assembler.CarryByteCount);
            }
            else
            {
                Update(() => _ignoredPacketCount = checked(_ignoredPacketCount + 1));
            }

            Update(() =>
            {
                _nextPacketSequence = checked(_nextPacketSequence + 1);
                _acceptedPacketCount = checked(_acceptedPacketCount + 1);
                _payloadByteCount = checked(_payloadByteCount + packet.Payload.Length);
                _result = PlaybackPcpFmp4CompositionResult.PacketAccepted;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResetAssembler();
            try
            {
                await DisposeGenerationAsync().ConfigureAwait(false);
            }
            finally
            {
                _initializationSegment = null;
                SetFault(PlaybackPcpFmp4CompositionResult.Cancelled);
            }

            throw;
        }
        catch (Exception exception)
        {
            ResetAssembler();
            var result = exception is PlaybackPcpFmp4CompositionException compositionException
                ? compositionException.Result
                : PlaybackPcpFmp4CompositionResult.Faulted;
            try
            {
                await DisposeGenerationAsync().ConfigureAwait(false);
            }
            finally
            {
                _initializationSegment = null;
                SetFault(result);
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Completes one enclosing source lifetime. A partial final box is a
    /// truncation and never becomes successful decoder EOS.
    /// </summary>
    public async ValueTask CompleteInputAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAccepting();
            try
            {
                _assembler.Complete();
            }
            catch (InvalidDataException)
            {
                ResetAssembler();
                try
                {
                    await DisposeGenerationAsync().ConfigureAwait(false);
                }
                finally
                {
                    _initializationSegment = null;
                    SetFault(PlaybackPcpFmp4CompositionResult.TruncatedInput);
                }

                throw Error(PlaybackPcpFmp4CompositionResult.TruncatedInput);
            }

            if (_generation is null)
            {
                await DisposeGenerationAsync().ConfigureAwait(false);
                SetFault(PlaybackPcpFmp4CompositionResult.InitializationUnavailable);
                throw Error(PlaybackPcpFmp4CompositionResult.InitializationUnavailable);
            }

            try
            {
                await _generation.Decoder.CompleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                await _generation.Output.CompleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                Update(() =>
                {
                    _state = PlaybackPcpFmp4CompositionState.Completed;
                    _result = PlaybackPcpFmp4CompositionResult.Completed;
                });
            }
            catch
            {
                var result = cancellationToken.IsCancellationRequested
                    ? PlaybackPcpFmp4CompositionResult.Cancelled
                    : PlaybackPcpFmp4CompositionResult.Faulted;
                try
                {
                    await DisposeGenerationAsync().ConfigureAwait(false);
                }
                finally
                {
                    _initializationSegment = null;
                    SetFault(result);
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _assembler.Dispose();
            Update(() => _assemblerCarryBytes = 0);
            try
            {
                await DisposeGenerationAsync().ConfigureAwait(false);
            }
            finally
            {
                _initializationSegment = null;
                Update(() =>
                {
                    _state = PlaybackPcpFmp4CompositionState.Disposed;
                    _result = PlaybackPcpFmp4CompositionResult.Disposed;
                });
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask HandleAssembledSegmentAsync(
        PlaybackFmp4LiveSegmentKind kind,
        ReadOnlyMemory<byte> segment,
        CancellationToken cancellationToken)
    {
        if (kind == PlaybackFmp4LiveSegmentKind.Initialization)
        {
            await CreateGenerationAsync(segment, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var generation = _generation ?? throw Error(
            PlaybackPcpFmp4CompositionResult.InitializationUnavailable);
        var batchResult = generation.Adapter.CreateBatch(segment, out var batch);
        if (batchResult != PlaybackEncodedSampleBatchCreationResult.Created ||
            batch is null)
        {
            throw Error(PlaybackPcpFmp4CompositionResult.AdapterRejected);
        }

        using (batch)
        {
            Update(() =>
            {
                _mediaSegmentCount = checked(_mediaSegmentCount + 1);
                _batchCount = checked(_batchCount + 1);
            });
            await generation.Decoder.DecodeBatchAsync(batch, cancellationToken)
                .ConfigureAwait(false);
            Update(() => _decoderSubmissionCount = checked(
                _decoderSubmissionCount + 1));
        }
    }

    private async ValueTask CreateGenerationAsync(
        ReadOnlyMemory<byte> initializationSegment,
        CancellationToken cancellationToken)
    {
        if (PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                initializationSegment.Span,
                initializationSegment.Length,
                out var envelope) !=
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted ||
            envelope?.Kind != PlaybackFmp4LiveSegmentKind.Initialization)
        {
            throw Error(PlaybackPcpFmp4CompositionResult.InvalidSegment);
        }

        if (IsExplicitlyIncompatibleMetadata(_metadataProvider?.Invoke()))
        {
            throw Error(PlaybackPcpFmp4CompositionResult.UnsupportedProfile);
        }

        if (PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                initializationSegment,
                out var adapter,
                new PlaybackFmp4LiveEncodedSampleBatchAdapterOptions
                {
                    MaximumSegmentBytes = _maximumSegmentBytes,
                }) != PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created ||
            adapter is null)
        {
            throw Error(PlaybackPcpFmp4CompositionResult.AdapterRejected);
        }

        var generationId = _nextPresentationGeneration;
        _nextPresentationGeneration = checked(generationId + 1);
        var output = _presentationFactory.Create(generationId) ??
            throw Error(PlaybackPcpFmp4CompositionResult.DecoderRejected);
        PlaybackFmp4LiveDecoderSession? decoder = null;
        try
        {
            if (PlaybackFmp4LiveDecoderSession.Create(
                    adapter.StreamDescription,
                    _decoderFactory,
                    out decoder,
                    output) != PlaybackFmp4LiveDecoderSessionCreateResult.Created ||
                decoder is null)
            {
                throw Error(PlaybackPcpFmp4CompositionResult.DecoderRejected);
            }
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var previous = _generation;
        _generation = new(adapter, decoder, output, generationId);
        _initializationSegment = initializationSegment.ToArray();
        Update(() =>
        {
            _currentPresentationGeneration = generationId;
            _initializationGenerationCount = checked(
                _initializationGenerationCount + 1);
        });
        if (previous is not null)
        {
            await DisposeGenerationAsync(previous).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async ValueTask ReplaceGenerationForNewHeadAsync(
        CancellationToken cancellationToken)
    {
        var previous = _generation;
        _generation = null;
        _initializationSegment = null;
        Update(() => _currentPresentationGeneration = null);
        if (previous is not null)
        {
            await DisposeGenerationAsync(previous).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async ValueTask ResetForDiscontinuityAsync(
        bool restoreInitialization,
        CancellationToken cancellationToken)
    {
        var initialization = restoreInitialization && _initializationSegment is { } bytes
            ? bytes.ToArray()
            : null;
        await DisposeGenerationAsync().ConfigureAwait(false);
        if (initialization is not null)
        {
            _initializationSegment = initialization;
            await CreateGenerationAsync(initialization, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _initializationSegment = null;
        }
    }

    private async ValueTask DisposeGenerationAsync()
    {
        var generation = _generation;
        _generation = null;
        Update(() => _currentPresentationGeneration = null);
        if (generation is not null)
        {
            await DisposeGenerationAsync(generation).ConfigureAwait(false);
        }
    }

    private static async ValueTask DisposeGenerationAsync(Generation generation)
    {
        try
        {
            await generation.Decoder.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await generation.Output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ValidateIdentityAndSequence(PcpChannelPacket packet)
    {
        if (packet.ChannelId != _channelId || packet.SessionId != _sessionId)
        {
            throw Error(PlaybackPcpFmp4CompositionResult.InvalidIdentity);
        }

        if (packet.Sequence != _nextPacketSequence ||
            packet.Sequence == long.MaxValue)
        {
            throw Error(PlaybackPcpFmp4CompositionResult.SequenceRejected);
        }
    }

    private void ResetAssembler()
    {
        _assembler.Reset();
        Update(() => _assemblerCarryBytes = 0);
    }

    private static bool IsExplicitlyIncompatibleMetadata(
        ChannelMetadataSnapshot? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        var values = new[]
        {
            metadata.ContentType,
            metadata.StreamType,
            metadata.StreamExtension,
        }.Where(static value => !string.IsNullOrWhiteSpace(value));

        foreach (var value in values)
        {
            var normalized = value!.Trim().TrimStart('.');
            if (normalized.Equals("flv", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("x-flv", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("video/flv", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("mkv", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("matroska", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ts", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("mpeg2ts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureAccepting()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_state != PlaybackPcpFmp4CompositionState.Accepting)
        {
            throw Error(_state == PlaybackPcpFmp4CompositionState.Disposed
                ? PlaybackPcpFmp4CompositionResult.Disposed
                : PlaybackPcpFmp4CompositionResult.Faulted);
        }
    }

    private void SetFault(PlaybackPcpFmp4CompositionResult result) => Update(() =>
    {
        if (_state != PlaybackPcpFmp4CompositionState.Disposed)
        {
            _state = PlaybackPcpFmp4CompositionState.Faulted;
            _result = result;
        }
    });

    private void Update(Action update)
    {
        lock (_snapshotGate)
        {
            update();
        }
    }

    private static PlaybackPcpFmp4CompositionException Error(
        PlaybackPcpFmp4CompositionResult result) => new(result);

    private sealed record Generation(
        PlaybackFmp4LiveEncodedSampleBatchAdapter Adapter,
        PlaybackFmp4LiveDecoderSession Decoder,
        IPlaybackPcpFmp4PresentationOutputGeneration Output,
        long Id);
}

public sealed class PlaybackPcpFmp4CompositionException : Exception
{
    public PlaybackPcpFmp4CompositionException(
        PlaybackPcpFmp4CompositionResult result)
        : base($"The PCP fMP4 playback composition rejected input: {result}.")
    {
        Result = result;
    }

    public PlaybackPcpFmp4CompositionResult Result { get; }
}

/// <summary>Adapts the accepted #250 Windows presentation lifetime.</summary>
public sealed class PlaybackPcpFmp4WindowsPresentationOutputGeneration(
    PlaybackDecodedFrameWindowsPresentationAdapter inner) :
    IPlaybackPcpFmp4PresentationOutputGeneration
{
    public ValueTask WriteAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken) =>
        inner.WriteAudioAsync(frame, cancellationToken);

    public ValueTask WriteVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken) =>
        inner.WriteVideoAsync(frame, cancellationToken);

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        var result = await inner.CompleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result != PlaybackDecodedFrameWindowsPresentationCompletionResult.Completed)
        {
            throw new InvalidOperationException(
                $"Windows fMP4 presentation completion failed: {result}.");
        }
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
