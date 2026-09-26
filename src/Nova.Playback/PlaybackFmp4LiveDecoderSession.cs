using System.Buffers;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveDecoderSessionCreateResult
{
    Created,
    InvalidInput,
    DecoderUnsupported,
}

/// <summary>
/// Owns a decoder lifetime for validated fMP4 sample batches. The session
/// preserves track configuration and sample timing at decoder input and sends
/// decoded output through the existing awaited frame sink.
/// </summary>
public sealed class PlaybackFmp4LiveDecoderSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlaybackEncodedStreamDescription _streamDescription;
    private IPlaybackDecoder? _decoder;
    private long _nextInputSequence;
    private ulong _lastEpochId;
    private bool _hasLastBatch;
    private bool _completed;
    private bool _faulted;
    private int _disposed;

    private PlaybackFmp4LiveDecoderSession(
        PlaybackEncodedStreamDescription streamDescription,
        IPlaybackDecoder decoder)
    {
        _streamDescription = streamDescription;
        _decoder = decoder;
    }

    public PlaybackEncodedStreamDescription StreamDescription =>
        _streamDescription;

    public Guid StreamGenerationId =>
        _streamDescription.StreamGenerationId;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public static PlaybackFmp4LiveDecoderSessionCreateResult Create(
        PlaybackEncodedStreamDescription? streamDescription,
        IPlaybackDecoderFactory? factory,
        out PlaybackFmp4LiveDecoderSession? session,
        IPlaybackDecodedFrameSink? outputSink = null)
    {
        session = null;
        if (streamDescription is null || factory is null)
        {
            return PlaybackFmp4LiveDecoderSessionCreateResult.InvalidInput;
        }

        var context = PlaybackDecoderCreationContext.CreateForFmp4(
            streamDescription,
            outputSink ?? NullPlaybackDecodedFrameSink.Instance);
        var decoder = factory.Create(context);
        if (decoder is null)
        {
            return PlaybackFmp4LiveDecoderSessionCreateResult.DecoderUnsupported;
        }

        session = new(streamDescription, decoder);
        return PlaybackFmp4LiveDecoderSessionCreateResult.Created;
    }

    public async ValueTask DecodeBatchAsync(
        PlaybackEncodedSampleBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var decoder = GetActiveDecoder();
            ValidateBatch(batch);
            var orderedSamples = OrderSamplesByDecodeTime(batch);
            var resetRequired = RequiresReset(batch);
            try
            {
                if (resetRequired)
                {
                    await decoder.ResetAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                var discontinuityPending =
                    batch.Discontinuity == PlaybackEncodedDiscontinuity.Global;
                foreach (var sample in orderedSamples)
                {
                    var track = _streamDescription.FindTrack(sample.TrackId) ??
                        throw new InvalidOperationException(
                            "The encoded sample has no stream track description.");
                    var payloadBuffer = ArrayPool<byte>.Shared.Rent(
                        sample.PayloadSlice.Length);
                    try
                    {
                        batch.CopyPayloadTo(
                            sample,
                            payloadBuffer.AsSpan(0, sample.PayloadSlice.Length));
                        var sequence = _nextInputSequence;
                        _nextInputSequence = checked(_nextInputSequence + 1);
                        var metadata = new PlaybackFmp4SampleMetadata(
                            batch.StreamGenerationId,
                            sample.TrackId,
                            track.MediaRole,
                            track.CodecKind,
                            track.CodecVariant,
                            track.TimeBase,
                            batch.EpochId,
                            batch.FragmentSequence,
                            sample.SampleOrdinal,
                            sample.DtsTicks,
                            sample.PtsTicks,
                            sample.DurationTicks,
                            sample.RandomAccessClassification);
                        var input = PlaybackEncodedInput.CreateFmp4Sample(
                            sequence,
                            discontinuityPending
                                ? PlaybackDiscontinuityKind.Source
                                : PlaybackDiscontinuityKind.None,
                            payloadBuffer.AsMemory(
                                0,
                                sample.PayloadSlice.Length),
                            metadata);
                        await decoder.DecodeAsync(input, cancellationToken)
                            .ConfigureAwait(false);
                        discontinuityPending = false;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(
                            payloadBuffer,
                            clearArray: true);
                    }
                }

                _lastEpochId = batch.EpochId;
                _hasLastBatch = true;
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var decoder = GetActiveDecoder();
            try
            {
                await decoder.CompleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                _completed = true;
            }
            catch
            {
                _faulted = true;
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
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            var decoder = Interlocked.Exchange(ref _decoder, null);
            if (decoder is not null)
            {
                await decoder.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private IPlaybackDecoder GetActiveDecoder()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_completed)
        {
            throw new InvalidOperationException(
                "The fMP4 decoder session has already completed.");
        }

        if (_faulted)
        {
            throw new InvalidOperationException(
                "The fMP4 decoder session is faulted.");
        }

        return _decoder ?? throw new ObjectDisposedException(
            nameof(PlaybackFmp4LiveDecoderSession));
    }

    private void ValidateBatch(PlaybackEncodedSampleBatch batch)
    {
        ObjectDisposedException.ThrowIf(batch.IsDisposed, batch);
        if (batch.StreamGenerationId != StreamGenerationId ||
            !ReferenceEquals(batch.StreamDescription, _streamDescription))
        {
            throw new InvalidOperationException(
                "The encoded sample batch belongs to another stream generation.");
        }
    }

    private bool RequiresReset(PlaybackEncodedSampleBatch batch)
    {
        if (!_hasLastBatch)
        {
            return false;
        }

        if (batch.Discontinuity == PlaybackEncodedDiscontinuity.Global &&
            _lastEpochId != ulong.MaxValue &&
            batch.EpochId == _lastEpochId + 1)
        {
            return true;
        }

        if (batch.Discontinuity == PlaybackEncodedDiscontinuity.None &&
            batch.EpochId == _lastEpochId)
        {
            return false;
        }

        throw new InvalidOperationException(
            "The encoded sample batch has an invalid epoch transition.");
    }

    private List<PlaybackEncodedSample> OrderSamplesByDecodeTime(
        PlaybackEncodedSampleBatch batch)
    {
        var samples = batch.Samples;
        var cursors = new List<TrackSampleCursor>();
        TrackSampleCursor? currentCursor = null;
        uint previousTrackId = 0;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            if (sample.TrackId < previousTrackId)
            {
                throw new InvalidOperationException(
                    "Encoded samples are not ordered by track and ordinal.");
            }

            if (sample.TrackId != previousTrackId)
            {
                if (sample.SampleOrdinal != 0)
                {
                    throw new InvalidOperationException(
                        "An encoded track sample sequence must start at ordinal zero.");
                }

                var track = _streamDescription.FindTrack(sample.TrackId) ??
                    throw new InvalidOperationException(
                        "The encoded sample has no stream track description.");
                if (track.TimeBase.Numerator <= 0 ||
                    track.TimeBase.Denominator <= 0)
                {
                    throw new InvalidOperationException(
                        "An encoded track has an invalid timebase.");
                }

                currentCursor = new(track, index);
                cursors.Add(currentCursor);
                previousTrackId = sample.TrackId;
                continue;
            }

            if (currentCursor is null ||
                sample.SampleOrdinal != currentCursor.NextSampleOrdinal)
            {
                throw new InvalidOperationException(
                    "Encoded samples are not ordered by track and ordinal.");
            }

            currentCursor.EndIndexExclusive = index + 1;
            currentCursor.NextSampleOrdinal++;
        }

        if (cursors.Count == 0)
        {
            throw new InvalidOperationException(
                "An encoded sample batch must contain at least one sample.");
        }

        var orderedSamples = new List<PlaybackEncodedSample>(samples.Count);
        while (orderedSamples.Count < samples.Count)
        {
            TrackSampleCursor? selectedCursor = null;
            PlaybackEncodedSample? selectedSample = null;
            foreach (var cursor in cursors)
            {
                if (cursor.NextIndex >= cursor.EndIndexExclusive)
                {
                    continue;
                }

                var candidate = samples[cursor.NextIndex];
                if (selectedCursor is null || selectedSample is null ||
                    CompareSampleHeads(
                        candidate,
                        cursor.Track,
                        selectedSample,
                        selectedCursor.Track) < 0)
                {
                    selectedCursor = cursor;
                    selectedSample = candidate;
                }
            }

            if (selectedCursor is null || selectedSample is null)
            {
                throw new InvalidOperationException(
                    "The encoded sample track merge ended before the batch.");
            }

            orderedSamples.Add(selectedSample);
            selectedCursor.NextIndex++;
        }

        return orderedSamples;
    }

    private static int CompareSampleHeads(
        PlaybackEncodedSample leftSample,
        PlaybackEncodedTrackDescription leftTrack,
        PlaybackEncodedSample rightSample,
        PlaybackEncodedTrackDescription rightTrack)
    {
        var decodeTimeComparison = CompareDecodeTime(
            leftSample,
            leftTrack.TimeBase,
            rightSample,
            rightTrack.TimeBase);
        if (decodeTimeComparison != 0)
        {
            return decodeTimeComparison;
        }

        // Match the regular ISO BMFF merge's default PreferVideo tie policy;
        // TrackId makes the policy total if a future profile has duplicate roles.
        var leftRoleOrder = leftTrack.MediaRole == PlaybackEncodedMediaRole.Video
            ? 0
            : 1;
        var rightRoleOrder = rightTrack.MediaRole == PlaybackEncodedMediaRole.Video
            ? 0
            : 1;
        var roleComparison = leftRoleOrder.CompareTo(rightRoleOrder);
        return roleComparison != 0
            ? roleComparison
            : leftTrack.TrackId.CompareTo(rightTrack.TrackId);
    }

    private static int CompareDecodeTime(
        PlaybackEncodedSample leftSample,
        PlaybackEncodedTimeBase leftTimeBase,
        PlaybackEncodedSample rightSample,
        PlaybackEncodedTimeBase rightTimeBase)
    {
        var leftScaled = (BigInteger)leftSample.DtsTicks *
            leftTimeBase.Numerator * rightTimeBase.Denominator;
        var rightScaled = (BigInteger)rightSample.DtsTicks *
            rightTimeBase.Numerator * leftTimeBase.Denominator;
        return leftScaled.CompareTo(rightScaled);
    }

    private sealed class TrackSampleCursor(
        PlaybackEncodedTrackDescription track,
        int startIndex)
    {
        public PlaybackEncodedTrackDescription Track { get; } = track;

        public int NextIndex { get; set; } = startIndex;

        public int EndIndexExclusive { get; set; } = startIndex + 1;

        public int NextSampleOrdinal { get; set; } = 1;
    }
}
