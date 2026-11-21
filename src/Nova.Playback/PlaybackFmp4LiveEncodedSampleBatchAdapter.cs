using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult
{
    Created,
    InvalidOptions,
    InvalidInitializationProfile,
    InvalidTrackTiming,
    InvalidCodecConfiguration,
}

public enum PlaybackEncodedSampleBatchCreationResult
{
    Created,
    InvalidInput,
    InvalidMediaSegment,
    InvalidFragmentBinding,
    InvalidPayloadProjection,
    InvalidEpochInput,
    InvalidEpochTransition,
    InvalidSampleMetadata,
    InvalidDuration,
    TimestampOverflow,
    SampleCountLimitExceeded,
    SamplePayloadLimitExceeded,
    TotalPayloadLimitExceeded,
    AllocationFailed,
}

public sealed record PlaybackFmp4LiveEncodedSampleBatchAdapterOptions
{
    public int MaximumSegmentBytes { get; init; } =
        checked((int)PlaybackBoundedDemuxOptions.DefaultMaximumInputBytesPerRead);

    public int MaximumSampleCount { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketsPerRead;

    public int MaximumSamplePayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes;

    public int MaximumTotalPayloadBytes { get; init; } =
        checked((int)
            PlaybackBoundedDemuxOptions.DefaultMaximumOwnedPacketBytesPerRead);

    internal bool IsValid =>
        MaximumSegmentBytes is > 0 &&
        MaximumSegmentBytes <=
            PlaybackBoundedDemuxOptions.MaximumInputBytesPerReadHardLimit &&
        MaximumSampleCount is > 0 &&
        MaximumSampleCount <=
            PlaybackBoundedDemuxOptions.MaximumPacketsPerReadHardLimit &&
        MaximumSamplePayloadBytes is > 0 &&
        MaximumSamplePayloadBytes <=
            PlaybackBoundedDemuxOptions.MaximumPacketPayloadBytesHardLimit &&
        MaximumTotalPayloadBytes is > 0 &&
        MaximumTotalPayloadBytes <=
            PlaybackBoundedDemuxOptions
                .MaximumOwnedPacketBytesPerReadHardLimit;
}

/// <summary>
/// Owns one validated initialization-profile lifetime and materializes each
/// accepted media fragment as one production-neutral, payload-owning batch.
/// </summary>
public sealed class PlaybackFmp4LiveEncodedSampleBatchAdapter
{
    private readonly object _gate = new();
    private readonly PlaybackFmp4LiveInitializationFragmentDefaultsProjection
        _initializationDefaults;
    private readonly PlaybackFmp4LiveEncodedSampleBatchAdapterOptions _options;
    private PlaybackFmp4LiveMultiTrackEpochState? _epochState;

    private PlaybackFmp4LiveEncodedSampleBatchAdapter(
        PlaybackFmp4LiveInitializationFragmentDefaultsProjection
            initializationDefaults,
        PlaybackEncodedStreamDescription streamDescription,
        PlaybackFmp4LiveEncodedSampleBatchAdapterOptions options)
    {
        _initializationDefaults = initializationDefaults;
        StreamDescription = streamDescription;
        _options = options;
    }

    public PlaybackEncodedStreamDescription StreamDescription { get; }

    public static PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult Create(
        ReadOnlyMemory<byte> initializationSegment,
        out PlaybackFmp4LiveEncodedSampleBatchAdapter? adapter,
        PlaybackFmp4LiveEncodedSampleBatchAdapterOptions? options = null)
    {
        adapter = null;
        options ??= new PlaybackFmp4LiveEncodedSampleBatchAdapterOptions();
        if (!options.IsValid || initializationSegment.IsEmpty ||
            initializationSegment.Length > options.MaximumSegmentBytes)
        {
            return PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult
                .InvalidOptions;
        }

        var initializationBytes = initializationSegment.Span;
        if (PlaybackFmp4LiveInitializationFragmentDefaultsProjector.Project(
                initializationBytes,
                options.MaximumSegmentBytes,
                out var initializationDefaults) !=
                PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                    .Projected ||
            initializationDefaults is null)
        {
            return PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult
                .InvalidInitializationProfile;
        }

        if (PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
                initializationBytes,
                options.MaximumSegmentBytes,
                out var trackTiming) !=
                PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                    .Projected ||
            trackTiming is null ||
            trackTiming.VideoTrackId !=
                initializationDefaults.Source.VideoTrackId ||
            trackTiming.AudioTrackId !=
                initializationDefaults.Source.AudioTrackId ||
            trackTiming.VideoTimescale == 0 ||
            trackTiming.AudioTimescale == 0)
        {
            return PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult
                .InvalidTrackTiming;
        }

        if (!TryCreateTrackDescriptions(
                initializationDefaults.Source,
                trackTiming,
                out var tracks))
        {
            return PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult
                .InvalidCodecConfiguration;
        }

        var streamDescription = new PlaybackEncodedStreamDescription(
            Guid.NewGuid(),
            tracks);
        adapter = new(
            initializationDefaults,
            streamDescription,
            options with { });
        return PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created;
    }

    public PlaybackEncodedSampleBatchCreationResult CreateBatch(
        ReadOnlyMemory<byte> mediaSegment,
        out PlaybackEncodedSampleBatch? batch)
    {
        batch = null;
        lock (_gate)
        {
            return CreateBatchLocked(mediaSegment, out batch);
        }
    }

    internal static PlaybackEncodedSampleBatchCreationResult ProjectMediaSegment(
        ReadOnlySpan<byte> segmentBytes,
        int maximumSegmentBytes,
        PlaybackFmp4LiveInitializationFragmentDefaultsProjection initializationDefaults,
        out PlaybackFmp4LiveMediaSegmentPayloadProjection? payloadProjection)
    {
        payloadProjection = null;
        if (PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                segmentBytes,
                maximumSegmentBytes,
                out var envelope) !=
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted ||
            envelope is null ||
            envelope.Kind != PlaybackFmp4LiveSegmentKind.Media)
        {
            return PlaybackEncodedSampleBatchCreationResult.InvalidMediaSegment;
        }

        var movieFragment = segmentBytes[..envelope.FirstBoxLength];
        if (PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var fragmentIdentity) !=
                PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected ||
            fragmentIdentity is null ||
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                movieFragment,
                fragmentIdentity,
                out var decodeTimeline) !=
                PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                    .Projected ||
            decodeTimeline is null ||
            PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
                initializationDefaults.Source,
                fragmentIdentity,
                decodeTimeline,
                out var fragmentBinding) !=
                PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound ||
            fragmentBinding is null)
        {
            return PlaybackEncodedSampleBatchCreationResult.InvalidFragmentBinding;
        }

        if (PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
                segmentBytes,
                maximumSegmentBytes,
                initializationDefaults,
                fragmentBinding,
                out payloadProjection) !=
                PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.Projected ||
            payloadProjection is null)
        {
            return PlaybackEncodedSampleBatchCreationResult
                .InvalidPayloadProjection;
        }

        return PlaybackEncodedSampleBatchCreationResult.Created;
    }

    private PlaybackEncodedSampleBatchCreationResult CreateBatchLocked(
        ReadOnlyMemory<byte> mediaSegment,
        out PlaybackEncodedSampleBatch? batch)
    {
        batch = null;
        var projectionResult = ProjectMediaSegment(
            mediaSegment.Span,
            _options.MaximumSegmentBytes,
            _initializationDefaults,
            out var payloadProjection);
        if (payloadProjection is null)
        {
            return projectionResult;
        }

        if (PlaybackFmp4LiveMultiTrackEpochInputBinder.Bind(
                payloadProjection.ResolvedRuns,
                out var epochInput) !=
                PlaybackFmp4LiveMultiTrackEpochInputBindingResult.Bound ||
            epochInput is null)
        {
            return PlaybackEncodedSampleBatchCreationResult.InvalidEpochInput;
        }

        var preparedSamples = new List<PreparedSample>();
        var preparationResult = PrepareSamples(
            mediaSegment.Length,
            payloadProjection,
            epochInput,
            preparedSamples,
            out var totalPayloadBytes);
        if (preparationResult != PlaybackEncodedSampleBatchCreationResult.Created)
        {
            return preparationResult;
        }

        if (preparedSamples.Count == 0)
        {
            return PlaybackEncodedSampleBatchCreationResult.InvalidSampleMetadata;
        }

        var epochResult = PrepareNextEpoch(
            epochInput,
            out var nextEpochState,
            out var discontinuity);
        if (epochResult != PlaybackEncodedSampleBatchCreationResult.Created)
        {
            return epochResult;
        }

        if (nextEpochState is null)
        {
            return PlaybackEncodedSampleBatchCreationResult
                .InvalidEpochTransition;
        }

        byte[]? payloadStorage = null;
        try
        {
            payloadStorage = new byte[checked((int)totalPayloadBytes)];
            var payloadSlices = new PlaybackEncodedPayloadSlice[
                preparedSamples.Count];
            var destinationOffset = 0;
            for (var index = 0; index < preparedSamples.Count; index++)
            {
                var prepared = preparedSamples[index];
                mediaSegment.Span.Slice(
                        prepared.SourceOffset,
                        prepared.PayloadLength)
                    .CopyTo(payloadStorage.AsSpan(
                        destinationOffset,
                        prepared.PayloadLength));

                if (!PlaybackEncodedPayloadSlice.TryCreate(
                        destinationOffset,
                        prepared.PayloadLength,
                        out payloadSlices[index]))
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .TotalPayloadLimitExceeded;
                }

                destinationOffset = checked(
                    destinationOffset + prepared.PayloadLength);
            }

            var randomAccessClassifications = new
                PlaybackEncodedRandomAccessClassification[
                    preparedSamples.Count];
            for (var index = 0; index < preparedSamples.Count; index++)
            {
                var prepared = preparedSamples[index];
                if (!TryClassifyRandomAccess(
                        prepared.TrackDescription.MediaRole,
                        prepared.SampleFlags,
                        out randomAccessClassifications[index]))
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .InvalidSampleMetadata;
                }
            }

            var samples = new PlaybackEncodedSample[preparedSamples.Count];
            for (var index = 0; index < preparedSamples.Count; index++)
            {
                var prepared = preparedSamples[index];
                samples[index] = new(
                    prepared.TrackId,
                    prepared.SampleOrdinal,
                    prepared.DtsTicks,
                    prepared.PtsTicks,
                    prepared.DurationTicks,
                    payloadSlices[index],
                    randomAccessClassifications[index]);
            }

            var createdBatch = new PlaybackEncodedSampleBatch(
                StreamDescription,
                nextEpochState.EpochId,
                nextEpochState.SequenceNumber,
                discontinuity,
                samples,
                payloadStorage);
            _epochState = nextEpochState;
            batch = createdBatch;
            payloadStorage = null;
            return PlaybackEncodedSampleBatchCreationResult.Created;
        }
        catch (OutOfMemoryException)
        {
            return PlaybackEncodedSampleBatchCreationResult.AllocationFailed;
        }
        catch (ArgumentException)
        {
            return PlaybackEncodedSampleBatchCreationResult.InvalidSampleMetadata;
        }
        catch (OverflowException)
        {
            return PlaybackEncodedSampleBatchCreationResult.TotalPayloadLimitExceeded;
        }
        finally
        {
            if (payloadStorage is not null)
            {
                Array.Clear(payloadStorage, 0, payloadStorage.Length);
            }
        }
    }

    private PlaybackEncodedSampleBatchCreationResult PrepareSamples(
        int sourceLength,
        PlaybackFmp4LiveMediaSegmentPayloadProjection payloadProjection,
        PlaybackFmp4LiveMultiTrackEpochInput epochInput,
        List<PreparedSample> preparedSamples,
        out long totalPayloadBytes)
    {
        totalPayloadBytes = 0;
        if (payloadProjection.Tracks.Count != epochInput.Tracks.Count)
        {
            return PlaybackEncodedSampleBatchCreationResult.InvalidSampleMetadata;
        }

        foreach (var epochTrack in epochInput.Tracks)
        {
            var payloadTrack = FindPayloadTrack(
                payloadProjection.Tracks,
                epochTrack.TrackId);
            var trackDescription = StreamDescription.FindTrack(epochTrack.TrackId);
            if (payloadTrack is null || trackDescription is null)
            {
                return PlaybackEncodedSampleBatchCreationResult
                    .InvalidSampleMetadata;
            }

            var timedSamples = epochTrack.TimedRun.Samples;
            var ranges = payloadTrack.SampleByteRanges.Ranges;
            if (timedSamples.Count != ranges.Count)
            {
                return PlaybackEncodedSampleBatchCreationResult
                    .InvalidSampleMetadata;
            }

            for (var index = 0; index < timedSamples.Count; index++)
            {
                var timed = timedSamples[index];
                var range = ranges[index];
                if (timed.SampleIndex != index || range.SampleIndex != index ||
                    timed.SampleSize != range.Length)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .InvalidSampleMetadata;
                }

                if (timed.SampleDuration == 0)
                {
                    return PlaybackEncodedSampleBatchCreationResult.InvalidDuration;
                }

                if (!TryConvertTimestamp(timed.DecodeTime, out var dtsTicks) ||
                    !TryConvertTimestamp(
                        timed.PresentationTime,
                        out var ptsTicks))
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .TimestampOverflow;
                }

                if (range.Offset < BigInteger.Zero || range.Length == 0 ||
                    range.Length > _options.MaximumSamplePayloadBytes)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .SamplePayloadLimitExceeded;
                }

                var endOffset = range.Offset + range.Length;
                if (range.EndOffsetExclusive != endOffset ||
                    range.Offset > int.MaxValue ||
                    endOffset > sourceLength)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .InvalidSampleMetadata;
                }

                var payloadLength = checked((int)range.Length);
                var sourceOffset = checked((int)range.Offset);
                var durationTicks = (long)timed.SampleDuration;
                preparedSamples.Add(new(
                    epochTrack.TrackId,
                    timed.SampleIndex,
                    dtsTicks,
                    ptsTicks,
                    durationTicks,
                    sourceOffset,
                    payloadLength,
                    timed.SampleFlags,
                    trackDescription));

                if (preparedSamples.Count > _options.MaximumSampleCount)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .SampleCountLimitExceeded;
                }

                try
                {
                    totalPayloadBytes = checked(
                        totalPayloadBytes + payloadLength);
                }
                catch (OverflowException)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .TotalPayloadLimitExceeded;
                }

                if (totalPayloadBytes > _options.MaximumTotalPayloadBytes)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .TotalPayloadLimitExceeded;
                }
            }
        }

        preparedSamples.Sort(static (left, right) =>
        {
            var trackOrder = left.TrackId.CompareTo(right.TrackId);
            return trackOrder != 0
                ? trackOrder
                : left.SampleOrdinal.CompareTo(right.SampleOrdinal);
        });
        return PlaybackEncodedSampleBatchCreationResult.Created;
    }

    private PlaybackEncodedSampleBatchCreationResult PrepareNextEpoch(
        PlaybackFmp4LiveMultiTrackEpochInput input,
        out PlaybackFmp4LiveMultiTrackEpochState? next,
        out PlaybackEncodedDiscontinuity discontinuity)
    {
        next = null;
        discontinuity = PlaybackEncodedDiscontinuity.None;
        if (_epochState is null)
        {
            if (PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                    input,
                    out next) != PlaybackFmp4LiveEpochStateCreateResult.Created ||
                next is null)
            {
                return PlaybackEncodedSampleBatchCreationResult
                    .InvalidEpochTransition;
            }
        }
        else
        {
            if (PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
                    _epochState.FragmentHeader,
                    input.FragmentHeader,
                    out var sequenceContinuity) !=
                    PlaybackIsoBmffMovieFragmentSequenceContinuityResult.Classified ||
                sequenceContinuity is null)
            {
                return PlaybackEncodedSampleBatchCreationResult
                    .InvalidEpochTransition;
            }

            var continuities = new List<
                PlaybackIsoBmffFragmentContinuityBinding>();
            foreach (var currentTrack in input.Tracks)
            {
                var previousTrack = FindEpochTrack(
                    _epochState.Tracks,
                    currentTrack.TrackId);
                if (previousTrack is null || !previousTrack.CurrentEpochMember)
                {
                    continue;
                }

                if (PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier
                        .Classify(
                            previousTrack.TimedRun,
                            currentTrack.TimedRun,
                            out var decodeContinuity) !=
                    PlaybackIsoBmffTrackFragmentDecodeContinuityResult.Classified ||
                    decodeContinuity is null ||
                    PlaybackIsoBmffFragmentContinuityBinder.Bind(
                        sequenceContinuity,
                        decodeContinuity,
                        out var binding) !=
                    PlaybackIsoBmffFragmentContinuityBindingResult.Bound ||
                    binding is null)
                {
                    return PlaybackEncodedSampleBatchCreationResult
                        .InvalidEpochTransition;
                }

                continuities.Add(binding);
            }

            if (PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
                    _epochState,
                    input,
                    continuities,
                    out next) !=
                    PlaybackFmp4LiveEpochStateTransitionResult.Advanced ||
                next is null)
            {
                return PlaybackEncodedSampleBatchCreationResult
                    .InvalidEpochTransition;
            }
        }

        if (_epochState is { } previousState &&
            next.EpochId < previousState.EpochId)
        {
            return PlaybackEncodedSampleBatchCreationResult
                .InvalidEpochTransition;
        }

        discontinuity = _epochState is { } previous &&
            next.EpochId > previous.EpochId
                ? PlaybackEncodedDiscontinuity.Global
                : PlaybackEncodedDiscontinuity.None;
        return PlaybackEncodedSampleBatchCreationResult.Created;
    }

    private static bool TryCreateTrackDescriptions(
        PlaybackFmp4LiveInitializationProfileProjection profile,
        PlaybackFmp4LiveInitializationTrackTimingProjection timing,
        out PlaybackEncodedTrackDescription[] tracks)
    {
        tracks = [];
        var videoConfiguration = profile.VideoConfiguration.Configuration;
        var videoVariant = profile.VideoConfiguration.CodecCandidate switch
        {
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 =>
                PlaybackEncodedCodecVariant.Avc1,
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc3 =>
                PlaybackEncodedCodecVariant.Avc3,
            _ => (PlaybackEncodedCodecVariant?)null,
        };
        if (videoVariant is null ||
            videoConfiguration.NalUnitLengthBytes is < 1 or > 4 ||
            profile.VideoConfiguration.VisualEntry.Width == 0 ||
            profile.VideoConfiguration.VisualEntry.Height == 0)
        {
            return false;
        }

        if (PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                profile.AudioConfiguration.ElementaryStreamDescriptor,
                out var decoderConfig) !=
                PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed ||
            decoderConfig is null ||
            PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                decoderConfig,
                out var audioSpecificConfig) !=
                PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed ||
            audioSpecificConfig is null ||
            audioSpecificConfig.ConfigurationBytes.IsEmpty ||
            audioSpecificConfig.SamplingFrequency is 0 or > int.MaxValue ||
            profile.AudioConfiguration.AudioEntry.ChannelCount == 0)
        {
            return false;
        }

        try
        {
            tracks =
            [
                new PlaybackEncodedTrackDescription(
                    profile.VideoTrackId,
                    PlaybackEncodedMediaRole.Video,
                    PlaybackEncodedCodecKind.Avc,
                    videoVariant.Value,
                    videoConfiguration.ConfigurationRecord.ToArray(),
                    new PlaybackEncodedTimeBase(1, timing.VideoTimescale),
                    nalLengthSize: videoConfiguration.NalUnitLengthBytes,
                    width: profile.VideoConfiguration.VisualEntry.Width,
                    height: profile.VideoConfiguration.VisualEntry.Height),
                new PlaybackEncodedTrackDescription(
                    profile.AudioTrackId,
                    PlaybackEncodedMediaRole.Audio,
                    PlaybackEncodedCodecKind.Aac,
                    PlaybackEncodedCodecVariant.Aac,
                    audioSpecificConfig.ConfigurationBytes.ToArray(),
                    new PlaybackEncodedTimeBase(1, timing.AudioTimescale),
                    sampleRate: checked((int)audioSpecificConfig.SamplingFrequency),
                    channelCount: profile.AudioConfiguration.AudioEntry.ChannelCount),
            ];
        }
        catch (ArgumentException)
        {
            tracks = [];
            return false;
        }

        Array.Sort(tracks, static (left, right) =>
            left.TrackId.CompareTo(right.TrackId));
        return true;
    }

    private static bool TryConvertTimestamp(BigInteger value, out long ticks)
    {
        ticks = 0;
        if (value < long.MinValue || value > long.MaxValue)
        {
            return false;
        }

        ticks = (long)value;
        return true;
    }

    private static bool TryClassifyRandomAccess(
        PlaybackEncodedMediaRole role,
        uint sampleFlags,
        out PlaybackEncodedRandomAccessClassification classification)
    {
        if (role == PlaybackEncodedMediaRole.Audio)
        {
            classification =
                PlaybackEncodedRandomAccessClassification.NotApplicable;
            return true;
        }

        if (PlaybackFmp4LiveRandomAccessSampleClassifier.Classify(
                sampleFlags,
                out var foundationClassification) !=
            PlaybackFmp4LiveRandomAccessSampleClassificationResult.Classified)
        {
            classification = default;
            return false;
        }

        classification = foundationClassification.Kind switch
        {
            PlaybackFmp4LiveRandomAccessSampleKind.IndependentlyDecodable =>
                PlaybackEncodedRandomAccessClassification.IndependentlyDecodable,
            PlaybackFmp4LiveRandomAccessSampleKind.NotIndependentlyDecodable =>
                PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable,
            PlaybackFmp4LiveRandomAccessSampleKind.Indeterminate =>
                PlaybackEncodedRandomAccessClassification.Indeterminate,
            _ => default,
        };
        return Enum.IsDefined(classification);
    }

    private static PlaybackFmp4LiveMediaSegmentTrackPayloadProjection?
        FindPayloadTrack(
            IReadOnlyList<PlaybackFmp4LiveMediaSegmentTrackPayloadProjection>
                tracks,
            uint trackId)
    {
        foreach (var track in tracks)
        {
            if (track.TrackId == trackId)
            {
                return track;
            }
        }

        return null;
    }

    private static PlaybackFmp4LiveMultiTrackEpochTrackState? FindEpochTrack(
        IReadOnlyList<PlaybackFmp4LiveMultiTrackEpochTrackState> tracks,
        uint trackId)
    {
        foreach (var track in tracks)
        {
            if (track.TrackId == trackId)
            {
                return track;
            }
        }

        return null;
    }

    private readonly record struct PreparedSample(
        uint TrackId,
        int SampleOrdinal,
        long DtsTicks,
        long PtsTicks,
        long DurationTicks,
        int SourceOffset,
        int PayloadLength,
        uint SampleFlags,
        PlaybackEncodedTrackDescription TrackDescription);
}
