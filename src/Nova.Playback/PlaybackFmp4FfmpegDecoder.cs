using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

internal enum PlaybackFmp4FfmpegTrackProfileResult
{
    Supported,
    InvalidTrackInventory,
    UnsupportedTrack,
    InvalidConfiguration,
    InvalidTimeBase,
}

internal static class PlaybackFmp4FfmpegTrackProfile
{
    public static PlaybackFmp4FfmpegTrackProfileResult Validate(
        PlaybackEncodedStreamDescription streamDescription)
    {
        ArgumentNullException.ThrowIfNull(streamDescription);
        if (streamDescription.TrackDescriptions.Count != 2 ||
            streamDescription.TrackDescriptions.Count(static track =>
                track.MediaRole == PlaybackEncodedMediaRole.Video) != 1 ||
            streamDescription.TrackDescriptions.Count(static track =>
                track.MediaRole == PlaybackEncodedMediaRole.Audio) != 1)
        {
            return PlaybackFmp4FfmpegTrackProfileResult.InvalidTrackInventory;
        }

        foreach (var track in streamDescription.TrackDescriptions)
        {
            if (!TryReduceTimeBase(track.TimeBase, out _))
            {
                return PlaybackFmp4FfmpegTrackProfileResult.InvalidTimeBase;
            }

            if (track.MediaRole == PlaybackEncodedMediaRole.Video)
            {
                var nalLengthSize = track.NalLengthSize;
                if (track.CodecKind != PlaybackEncodedCodecKind.Avc ||
                    track.CodecVariant != PlaybackEncodedCodecVariant.Avc1 ||
                    nalLengthSize is not { } declaredNalLengthSize ||
                    declaredNalLengthSize is < 1 or > 4 ||
                    track.Width is not (>= 1 and <= PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension) ||
                    track.Height is not (>= 1 and <= PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension))
                {
                    return PlaybackFmp4FfmpegTrackProfileResult.UnsupportedTrack;
                }

                if (!IsValidAvcConfiguration(
                        track.CodecConfiguration,
                        declaredNalLengthSize))
                {
                    return PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration;
                }
            }
            else if (track.MediaRole == PlaybackEncodedMediaRole.Audio)
            {
                if (track.CodecKind != PlaybackEncodedCodecKind.Aac ||
                    track.CodecVariant != PlaybackEncodedCodecVariant.Aac ||
                    track.SampleRate is not (>= PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioSampleRate and <= PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioSampleRate) ||
                    track.ChannelCount is not (>= PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioChannelCount and <= PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioChannelCount))
                {
                    return PlaybackFmp4FfmpegTrackProfileResult.UnsupportedTrack;
                }

                if (!IsValidAacLcConfiguration(
                        track.CodecConfiguration,
                        track.SampleRate.Value,
                        track.ChannelCount.Value))
                {
                    return PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration;
                }
            }
            else
            {
                return PlaybackFmp4FfmpegTrackProfileResult.UnsupportedTrack;
            }
        }

        return PlaybackFmp4FfmpegTrackProfileResult.Supported;
    }

    public static bool TryReduceTimeBase(
        PlaybackEncodedTimeBase timeBase,
        out PlaybackBoundedDemuxTimeBase reduced)
    {
        reduced = default;
        if (timeBase.Numerator <= 0 || timeBase.Denominator <= 0)
        {
            return false;
        }

        var numerator = timeBase.Numerator;
        var denominator = timeBase.Denominator;
        var left = numerator;
        var right = denominator;
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        numerator /= left;
        denominator /= left;
        if (numerator > PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent ||
            denominator > PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent)
        {
            return false;
        }

        reduced = new((int)numerator, (int)denominator);
        return true;
    }

    private static bool IsValidAvcConfiguration(
        IReadOnlyList<byte> configuration,
        byte expectedNalLengthSize)
    {
        if (configuration.Count is < 7 or > PlaybackDecoderFfmpegCodecParametersFactory.MaximumExtradataLength)
        {
            return false;
        }

        var bytes = configuration.ToArray().AsSpan();
        if (bytes[0] != 1 ||
            (bytes[4] & 0xfc) != 0xfc ||
            (bytes[5] & 0xe0) != 0xe0 ||
            ((bytes[4] & 0x03) + 1) != expectedNalLengthSize)
        {
            return false;
        }

        var offset = 6;
        var sequenceCount = bytes[5] & 0x1f;
        if (sequenceCount == 0 ||
            !SkipParameterSets(bytes, ref offset, sequenceCount, 7) ||
            offset >= bytes.Length)
        {
            return false;
        }

        var pictureCount = bytes[offset++];
        if (pictureCount == 0 ||
            !SkipParameterSets(bytes, ref offset, pictureCount, 8))
        {
            return false;
        }

        if (offset != bytes.Length)
        {
            if (!SupportsHighProfileExtension(bytes[1]) ||
                bytes.Length - offset < 4 ||
                (bytes[offset] & 0xfc) != 0xfc ||
                (bytes[offset + 1] & 0xf8) != 0xf8 ||
                (bytes[offset + 2] & 0xf8) != 0xf8)
            {
                return false;
            }

            offset += 3;
            var extensionCount = bytes[offset++];
            if (!SkipParameterSets(bytes, ref offset, extensionCount, 13))
            {
                return false;
            }
        }

        return offset == bytes.Length;
    }

    private static bool SkipParameterSets(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        int count,
        int expectedNalType)
    {
        for (var index = 0; index < count; index++)
        {
            if (bytes.Length - offset < sizeof(ushort))
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            offset += sizeof(ushort);
            if (length == 0 || bytes.Length - offset < length ||
                (bytes[offset] & 0x1f) != expectedNalType)
            {
                return false;
            }

            offset += length;
        }

        return true;
    }

    private static bool IsValidAacLcConfiguration(
        IReadOnlyList<byte> configuration,
        int expectedSampleRate,
        int expectedChannelCount)
    {
        if (configuration.Count is < 2 or > PlaybackDecoderFfmpegCodecParametersFactory.MaximumExtradataLength)
        {
            return false;
        }

        var bytes = configuration.ToArray().AsSpan();
        var bitOffset = 0;
        if (!TryReadBits(bytes, ref bitOffset, 5, out var objectType) ||
            objectType != 2 ||
            !TryReadBits(bytes, ref bitOffset, 4, out var frequencyIndex))
        {
            return false;
        }

        int sampleRate;
        if (frequencyIndex == 15)
        {
            if (!TryReadBits(bytes, ref bitOffset, 24, out var explicitFrequency) ||
                explicitFrequency > int.MaxValue)
            {
                return false;
            }

            sampleRate = (int)explicitFrequency;
        }
        else
        {
            ReadOnlySpan<int> sampleRates =
            [96_000, 88_200, 64_000, 48_000, 44_100, 32_000, 24_000,
                22_050, 16_000, 12_000, 11_025, 8_000, 7_350];
            if (frequencyIndex >= sampleRates.Length)
            {
                return false;
            }

            sampleRate = sampleRates[(int)frequencyIndex];
        }

        if (!TryReadBits(bytes, ref bitOffset, 4, out var channelConfiguration))
        {
            return false;
        }

        if (!TryReadBits(bytes, ref bitOffset, 1, out _) ||
            !TryReadBits(bytes, ref bitOffset, 1, out var dependsOnCoreCoder))
        {
            return false;
        }

        if (dependsOnCoreCoder != 0 &&
            !TryReadBits(bytes, ref bitOffset, 14, out _))
        {
            return false;
        }

        if (!TryReadBits(bytes, ref bitOffset, 1, out var extensionFlag) ||
            extensionFlag != 0)
        {
            return false;
        }

        var remainingBits = bytes.Length * 8L - bitOffset;
        if (remainingBits >= 8 &&
            (!TryReadBits(bytes, ref bitOffset, 11, out var syncExtensionType) ||
                syncExtensionType != 0x2b7 ||
                !TryReadBits(bytes, ref bitOffset, 5, out var extensionObjectType) ||
                extensionObjectType != 5 ||
                !TryReadBits(bytes, ref bitOffset, 1, out var sbrPresent) ||
                sbrPresent != 0))
        {
            return false;
        }

        if (bytes.Length * 8L - bitOffset >= 8)
        {
            return false;
        }

        while (bitOffset < bytes.Length * 8)
        {
            if (!TryReadBits(bytes, ref bitOffset, 1, out var paddingBit) ||
                paddingBit != 0)
            {
                return false;
            }
        }

        var channelCount = channelConfiguration switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 8,
            _ => 0,
        };
        return sampleRate == expectedSampleRate &&
            channelCount == expectedChannelCount;
    }

    private static bool TryReadBits(
        ReadOnlySpan<byte> bytes,
        ref int bitOffset,
        int count,
        out uint value)
    {
        value = 0;
        if (count is < 1 or > 32 || count > bytes.Length * 8L - bitOffset)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            var currentBit = bitOffset + index;
            value = (value << 1) |
                (uint)((bytes[currentBit / 8] >> (7 - currentBit % 8)) & 1);
        }

        bitOffset += count;
        return true;
    }

    private static bool SupportsHighProfileExtension(byte profile) =>
        profile is 100 or 110 or 122 or 144;
}

internal enum PlaybackFmp4FfmpegPacketMapResult
{
    Mapped,
    WrongInputKind,
    MissingSampleMetadata,
    UnknownTrack,
    StreamGenerationMismatch,
    TrackMetadataMismatch,
    InvalidRandomAccessClassification,
    InvalidPayload,
    InvalidTimeBase,
    InvalidDiscontinuity,
}

internal static class PlaybackFmp4FfmpegPacketMapper
{
    private const int KeyPacketFlag = 0x0001;

    public static PlaybackFmp4FfmpegPacketMapResult TryMap(
        PlaybackEncodedInput input,
        Guid streamGenerationId,
        PlaybackEncodedTrackDescription? track,
        int streamIndex,
        out PlaybackMetadataAwareNativePacketInput packet)
    {
        packet = default;
        if (input.Kind != PlaybackEncodedInputKind.Packet)
        {
            return PlaybackFmp4FfmpegPacketMapResult.WrongInputKind;
        }

        if (input.Sequence < 0 || input.Discontinuity is not
                (PlaybackDiscontinuityKind.None or PlaybackDiscontinuityKind.Source))
        {
            return PlaybackFmp4FfmpegPacketMapResult.InvalidDiscontinuity;
        }

        if (input.Fmp4SampleMetadata is not { } metadata)
        {
            return PlaybackFmp4FfmpegPacketMapResult.MissingSampleMetadata;
        }

        if (track is null)
        {
            return PlaybackFmp4FfmpegPacketMapResult.UnknownTrack;
        }

        if (metadata.StreamGenerationId != streamGenerationId)
        {
            return PlaybackFmp4FfmpegPacketMapResult.StreamGenerationMismatch;
        }

        if (metadata.TrackId != track.TrackId ||
            metadata.MediaRole != track.MediaRole ||
            metadata.CodecKind != track.CodecKind ||
            metadata.CodecVariant != track.CodecVariant ||
            metadata.TimeBase != track.TimeBase ||
            metadata.SampleOrdinal < 0 ||
            metadata.DurationTicks <= 0 ||
            !Enum.IsDefined(metadata.MediaRole) ||
            !Enum.IsDefined(metadata.CodecKind) ||
            !Enum.IsDefined(metadata.CodecVariant))
        {
            return PlaybackFmp4FfmpegPacketMapResult.TrackMetadataMismatch;
        }

        if (!Enum.IsDefined(metadata.RandomAccessClassification) ||
            (track.MediaRole == PlaybackEncodedMediaRole.Video &&
                metadata.RandomAccessClassification ==
                    PlaybackEncodedRandomAccessClassification.NotApplicable) ||
            (track.MediaRole == PlaybackEncodedMediaRole.Audio &&
                metadata.RandomAccessClassification !=
                    PlaybackEncodedRandomAccessClassification.NotApplicable))
        {
            return PlaybackFmp4FfmpegPacketMapResult.InvalidRandomAccessClassification;
        }

        if (input.Payload.IsEmpty ||
            input.Payload.Length > PlaybackMetadataAwareNativePacketBoundary.MaximumPacketBytes ||
            streamIndex < 0)
        {
            return PlaybackFmp4FfmpegPacketMapResult.InvalidPayload;
        }

        if (!PlaybackFmp4FfmpegTrackProfile.TryReduceTimeBase(
                track.TimeBase,
                out var timeBase))
        {
            return PlaybackFmp4FfmpegPacketMapResult.InvalidTimeBase;
        }

        var flags = track.MediaRole == PlaybackEncodedMediaRole.Video &&
            metadata.RandomAccessClassification ==
                PlaybackEncodedRandomAccessClassification.IndependentlyDecodable
                    ? KeyPacketFlag
                    : 0;
        packet = new(
            input.Payload,
            metadata.PtsTicks,
            metadata.DtsTicks,
            metadata.DurationTicks,
            streamIndex,
            flags,
            timeBase);
        return PlaybackFmp4FfmpegPacketMapResult.Mapped;
    }
}

/// <summary>
/// Creates only the managed fMP4 AVC1/AAC profile. The supplied verified runtime
/// lease transfers to the decoder after a supported context is created.
/// </summary>
public sealed class PlaybackFmp4FfmpegDecoderFactory : IPlaybackDecoderFactory
{
    private readonly PlaybackDecoderFfmpegContextLeaseFactory _contextFactory;
    private readonly PlaybackDecoderFfmpegParameterTransfer _parameterTransfer;
    private readonly PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary _timeBaseBoundary;
    private readonly PlaybackDecoderFfmpegCodecOpen _codecOpen;
    private readonly PlaybackDecoderFfmpegDiscontinuityResetBoundary _resetBoundary;
    private readonly PlaybackDecoderFfmpegManagedFrameCopyBoundary _frameCopy;
    private readonly PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary _sinkDelivery;
    private VerifiedPlaybackDecoderRuntimeLease? _runtimeLease;

    public PlaybackFmp4FfmpegDecoderFactory(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease)
        : this(
            runtimeLease,
            new PlaybackDecoderFfmpegContextLeaseFactory(),
            new PlaybackDecoderFfmpegParameterTransfer(),
            new PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary(),
            new PlaybackDecoderFfmpegCodecOpen(),
            new PlaybackDecoderFfmpegDiscontinuityResetBoundary(),
            new PlaybackDecoderFfmpegManagedFrameCopyBoundary(),
            new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary())
    {
    }

    internal PlaybackFmp4FfmpegDecoderFactory(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackDecoderFfmpegContextLeaseFactory contextFactory,
        PlaybackDecoderFfmpegParameterTransfer parameterTransfer,
        PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary timeBaseBoundary,
        PlaybackDecoderFfmpegCodecOpen codecOpen,
        PlaybackDecoderFfmpegDiscontinuityResetBoundary resetBoundary,
        PlaybackDecoderFfmpegManagedFrameCopyBoundary frameCopy,
        PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary sinkDelivery)
    {
        ArgumentNullException.ThrowIfNull(runtimeLease);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(parameterTransfer);
        ArgumentNullException.ThrowIfNull(timeBaseBoundary);
        ArgumentNullException.ThrowIfNull(codecOpen);
        ArgumentNullException.ThrowIfNull(resetBoundary);
        ArgumentNullException.ThrowIfNull(frameCopy);
        ArgumentNullException.ThrowIfNull(sinkDelivery);
        _runtimeLease = runtimeLease;
        _contextFactory = contextFactory;
        _parameterTransfer = parameterTransfer;
        _timeBaseBoundary = timeBaseBoundary;
        _codecOpen = codecOpen;
        _resetBoundary = resetBoundary;
        _frameCopy = frameCopy;
        _sinkDelivery = sinkDelivery;
    }

    public IPlaybackDecoder? Create(PlaybackDecoderCreationContext context)
    {
        if (context.Container != PlaybackMediaContainer.FragmentedMp4 ||
            context.EncodedStreamDescription is not { } streamDescription ||
            streamDescription.StreamGenerationId == Guid.Empty ||
            PlaybackFmp4FfmpegTrackProfile.Validate(streamDescription) !=
                PlaybackFmp4FfmpegTrackProfileResult.Supported)
        {
            return null;
        }

        var runtimeLease = Interlocked.Exchange(ref _runtimeLease, null);
        if (runtimeLease is null || runtimeLease.IsDisposed)
        {
            return null;
        }

        var tracks = new List<PlaybackFmp4FfmpegDecoderTrack>(2);
        try
        {
            var descriptions = streamDescription.TrackDescriptions
                .OrderBy(static track => track.TrackId)
                .ToArray();
            for (var streamIndex = 0; streamIndex < descriptions.Length; streamIndex++)
            {
                tracks.Add(CreateTrack(runtimeLease, descriptions[streamIndex], streamIndex));
            }

            var video = tracks.Single(static track =>
                track.Description.MediaRole == PlaybackEncodedMediaRole.Video);
            var audio = tracks.Single(static track =>
                track.Description.MediaRole == PlaybackEncodedMediaRole.Audio);
            return new PlaybackFmp4FfmpegDecoder(
                streamDescription,
                context.OutputSink,
                runtimeLease,
                video,
                audio,
                _resetBoundary,
                _frameCopy,
                _sinkDelivery);
        }
        catch
        {
            foreach (var track in tracks)
            {
                track.Dispose();
            }

            runtimeLease.Dispose();
            throw;
        }
    }

    private PlaybackFmp4FfmpegDecoderTrack CreateTrack(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackEncodedTrackDescription description,
        int streamIndex)
    {
        var codecId = description.MediaRole switch
        {
            PlaybackEncodedMediaRole.Video => PlaybackDecoderFfmpegCodecId.H264,
            PlaybackEncodedMediaRole.Audio => PlaybackDecoderFfmpegCodecId.Aac,
            _ => throw new InvalidOperationException("The fMP4 track role is unsupported."),
        };
        var parametersInput = description.MediaRole switch
        {
            PlaybackEncodedMediaRole.Video =>
                PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                    description.Width!.Value,
                    description.Height!.Value,
                    new ReadOnlyMemory<byte>(description.CodecConfiguration.ToArray())),
            PlaybackEncodedMediaRole.Audio =>
                PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                    description.SampleRate!.Value,
                    description.ChannelCount!.Value,
                    new ReadOnlyMemory<byte>(description.CodecConfiguration.ToArray())),
            _ => throw new InvalidOperationException("The fMP4 track role is unsupported."),
        };
        var parameters = PlaybackDecoderFfmpegCodecParametersFactory.Create(parametersInput);
        if (parameters.Result != PlaybackDecoderFfmpegCodecParametersResult.Created ||
            parameters.Lease is not { } parameterLease)
        {
            throw new InvalidOperationException(
                $"FFmpeg rejected the {description.MediaRole} codec parameters: {parameters.Result}.");
        }

        using (parameterLease)
        {
            var contextCreation = _contextFactory.CreateSharingRuntime(runtimeLease, codecId);
            if (contextCreation.Result != PlaybackDecoderFfmpegContextLeaseResult.Created ||
                contextCreation.Lease is not { } contextLease)
            {
                throw new InvalidOperationException(
                    $"FFmpeg {description.MediaRole} decoder context creation failed: {contextCreation.Result}.");
            }

            try
            {
                var transferResult = _parameterTransfer.Apply(contextLease, parameterLease);
                if (transferResult != PlaybackDecoderFfmpegParameterTransferResult.Applied)
                {
                    throw new InvalidOperationException(
                        $"FFmpeg {description.MediaRole} codec parameter transfer failed: {transferResult}.");
                }

                if (!PlaybackFmp4FfmpegTrackProfile.TryReduceTimeBase(
                        description.TimeBase,
                        out var timeBase))
                {
                    throw new InvalidOperationException("The fMP4 track timebase is not representable.");
                }

                var timeBaseResult = _timeBaseBoundary.Configure(contextLease, timeBase);
                if (timeBaseResult != PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured)
                {
                    throw new InvalidOperationException(
                        $"FFmpeg {description.MediaRole} packet timebase setup failed: {timeBaseResult}.");
                }

                var openResult = _codecOpen.Open(contextLease);
                if (openResult != PlaybackDecoderFfmpegCodecOpenResult.Opened)
                {
                    throw new InvalidOperationException(
                        $"FFmpeg {description.MediaRole} decoder open failed: {openResult}.");
                }

                return new(description, streamIndex, contextLease);
            }
            catch
            {
                contextLease.Dispose();
                throw;
            }
        }
    }
}

internal sealed class PlaybackFmp4FfmpegDecoderTrack : IDisposable
{
    public PlaybackFmp4FfmpegDecoderTrack(
        PlaybackEncodedTrackDescription description,
        int streamIndex,
        PlaybackDecoderFfmpegContextLease context)
    {
        Description = description;
        StreamIndex = streamIndex;
        Context = context;
    }

    public PlaybackEncodedTrackDescription Description { get; }

    public int StreamIndex { get; }

    public PlaybackDecoderFfmpegContextLease Context { get; }

    public bool IsDrained { get; set; }

    public void Dispose() => Context.Dispose();
}

internal readonly record struct PlaybackFmp4FfmpegFrameDrainResult(
    int FramesDelivered,
    bool EndOfStream);

internal sealed class PlaybackFmp4FfmpegFrameCapture :
    IPlaybackDecoderFfmpegManagedFrameConsumer
{
    public PlaybackDecoderFfmpegManagedFrame? Frame { get; private set; }

    public bool Consume(PlaybackDecoderFfmpegManagedFrame frame)
    {
        if (Frame is not null)
        {
            return false;
        }

        Frame = frame;
        return true;
    }
}

internal sealed class PlaybackFmp4FfmpegDecoder : IPlaybackDecoder
{
    private const int MaximumFramesPerDrain = 4_096;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlaybackEncodedStreamDescription _streamDescription;
    private readonly IPlaybackDecodedFrameSink _sink;
    private readonly VerifiedPlaybackDecoderRuntimeLease _runtimeLease;
    private readonly PlaybackFmp4FfmpegDecoderTrack[] _tracks;
    private readonly Dictionary<uint, PlaybackFmp4FfmpegDecoderTrack> _tracksById;
    private readonly PlaybackDecoderFfmpegDiscontinuityResetBoundary _resetBoundary;
    private readonly PlaybackDecoderFfmpegManagedFrameCopyBoundary _frameCopy;
    private readonly PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary _sinkDelivery;
    private long _nextOutputSequence;
    private PlaybackDecodedFrameDiscontinuityKind _pendingDiscontinuity;
    private bool _hasInputSinceReset;
    private bool _completed;
    private int _disposed;

    public PlaybackFmp4FfmpegDecoder(
        PlaybackEncodedStreamDescription streamDescription,
        IPlaybackDecodedFrameSink sink,
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackFmp4FfmpegDecoderTrack video,
        PlaybackFmp4FfmpegDecoderTrack audio,
        PlaybackDecoderFfmpegDiscontinuityResetBoundary resetBoundary,
        PlaybackDecoderFfmpegManagedFrameCopyBoundary frameCopy,
        PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary sinkDelivery)
    {
        _streamDescription = streamDescription;
        _sink = sink;
        _runtimeLease = runtimeLease;
        _tracks = [video, audio];
        _tracksById = _tracks.ToDictionary(static track => track.Description.TrackId);
        _resetBoundary = resetBoundary;
        _frameCopy = frameCopy;
        _sinkDelivery = sinkDelivery;
    }

    public async ValueTask DecodeAsync(
        PlaybackEncodedInput input,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();
            var metadata = input.Fmp4SampleMetadata;
            PlaybackFmp4FfmpegDecoderTrack? track = null;
            if (metadata is { } present)
            {
                _tracksById.TryGetValue(present.TrackId, out track);
            }

            var mapResult = PlaybackFmp4FfmpegPacketMapper.TryMap(
                input,
                _streamDescription.StreamGenerationId,
                track?.Description,
                track?.StreamIndex ?? -1,
                out var packetInput);
            if (mapResult != PlaybackFmp4FfmpegPacketMapResult.Mapped || track is null)
            {
                throw new InvalidOperationException(
                    $"The fMP4 encoded sample cannot be submitted to FFmpeg: {mapResult}.");
            }

            if (input.Discontinuity == PlaybackDiscontinuityKind.Source)
            {
                if (_hasInputSinceReset)
                {
                    ResetCore();
                }

                _pendingDiscontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset;
            }

            var packetCreation = PlaybackMetadataAwareNativePacketOperations.CreatePacket(
                track.Context,
                packetInput);
            if (packetCreation.Result != PlaybackMetadataAwareNativeSendResult.Success ||
                packetCreation.Owner is not WindowsPlaybackMetadataAwareNativePacketOwner packetOwner)
            {
                packetCreation.Owner?.Dispose();
                throw new InvalidOperationException(
                    $"FFmpeg {track.Description.MediaRole} packet creation failed: {packetCreation.Result}.");
            }

            using (packetOwner)
            {
                await PlaybackFmp4FfmpegPendingPacketSubmission.SubmitAsync(
                    () => packetOwner.SendOnce(track.Context),
                    async token => (await DrainAvailableFramesAsync(
                        track,
                        token).ConfigureAwait(false)).FramesDelivered,
                    cancellationToken).ConfigureAwait(false);
            }

            var finalDrain = await DrainAvailableFramesAsync(track, cancellationToken)
                .ConfigureAwait(false);
            if (finalDrain.EndOfStream)
            {
                throw new InvalidOperationException(
                    $"FFmpeg {track.Description.MediaRole} decoder reached end of stream before completion.");
            }

            _hasInputSinceReset = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureActive();
            ResetCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_completed)
            {
                return;
            }

            foreach (var track in _tracks)
            {
                await DrainTrackToEndAsync(track, cancellationToken).ConfigureAwait(false);
            }

            _completed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _sinkDelivery.Dispose();
            foreach (var track in _tracks)
            {
                track.Dispose();
            }

            _runtimeLease.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask DrainTrackToEndAsync(
        PlaybackFmp4FfmpegDecoderTrack track,
        CancellationToken cancellationToken)
    {
        if (track.IsDrained)
        {
            return;
        }

        for (var attempt = 0; attempt < PlaybackFmp4FfmpegPendingPacketSubmission.MaximumSendAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = PlaybackMetadataAwareNativePacketOperations.SendFlush(track.Context);
            if (result == PlaybackMetadataAwareNativeSendResult.EndOfStream)
            {
                track.IsDrained = true;
                return;
            }

            if (result == PlaybackMetadataAwareNativeSendResult.Again)
            {
                var drained = await DrainAvailableFramesAsync(
                    track,
                    cancellationToken).ConfigureAwait(false);
                if (drained.EndOfStream)
                {
                    track.IsDrained = true;
                    return;
                }

                if (drained.FramesDelivered == 0)
                {
                    throw new InvalidOperationException(
                        "FFmpeg requested a frame drain before accepting the completion marker.");
                }

                continue;
            }

            if (result != PlaybackMetadataAwareNativeSendResult.Success)
            {
                throw new InvalidOperationException(
                    $"FFmpeg {track.Description.MediaRole} completion submission failed: {result}.");
            }

            var finalDrain = await DrainAvailableFramesAsync(
                track,
                cancellationToken).ConfigureAwait(false);
            if (!finalDrain.EndOfStream)
            {
                throw new InvalidOperationException(
                    "FFmpeg accepted the completion marker without reaching end of stream.");
            }

            track.IsDrained = true;
            return;
        }

        throw new InvalidOperationException("FFmpeg completion drain limit was exceeded.");
    }

    private async ValueTask<PlaybackFmp4FfmpegFrameDrainResult> DrainAvailableFramesAsync(
        PlaybackFmp4FfmpegDecoderTrack track,
        CancellationToken cancellationToken)
    {
        var delivered = 0;
        for (var index = 0; index < MaximumFramesPerDrain; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = new PlaybackFmp4FfmpegFrameCapture();
            var result = _frameCopy.ReceiveAndCopy(track.Context, capture);
            if (result == PlaybackDecoderFfmpegManagedFrameCopyResult.Copied &&
                capture.Frame is { } frame)
            {
                using (frame)
                {
                    var delivery = await _sinkDelivery.DeliverAsync(
                        track.Context,
                        frame,
                        _nextOutputSequence,
                        _pendingDiscontinuity,
                        _sink,
                        cancellationToken).ConfigureAwait(false);
                    if (delivery != PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered)
                    {
                        throw new InvalidOperationException(
                            $"The decoded {track.Description.MediaRole} frame could not be delivered: {delivery}.");
                    }
                }

                _nextOutputSequence = checked(_nextOutputSequence + 1);
                _pendingDiscontinuity = PlaybackDecodedFrameDiscontinuityKind.None;
                delivered++;
                continue;
            }

            if (result == PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput)
            {
                return new(delivered, false);
            }

            if (result == PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream)
            {
                return new(delivered, true);
            }

            capture.Frame?.Dispose();
            throw new InvalidOperationException(
                $"FFmpeg {track.Description.MediaRole} frame receive failed: {result}.");
        }

        throw new InvalidOperationException("FFmpeg frame drain limit was exceeded.");
    }

    private void ResetCore()
    {
        foreach (var track in _tracks)
        {
            var result = _resetBoundary.Reset(track.Context);
            if (result != PlaybackDecoderFfmpegDiscontinuityResetResult.Reset)
            {
                throw new InvalidOperationException(
                    $"FFmpeg {track.Description.MediaRole} discontinuity reset failed: {result}.");
            }
        }

        _hasInputSinceReset = false;
        _pendingDiscontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset;
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_completed)
        {
            throw new InvalidOperationException("The fMP4 FFmpeg decoder has completed.");
        }

        if (_tracks.Any(static track => track.IsDrained))
        {
            throw new InvalidOperationException("The fMP4 FFmpeg decoder track has drained.");
        }
    }
}

internal static class PlaybackFmp4FfmpegPendingPacketSubmission
{
    internal const int MaximumSendAttempts = 512;

    public static async ValueTask SubmitAsync(
        Func<PlaybackMetadataAwareNativeSendResult> sendPendingPacket,
        Func<CancellationToken, ValueTask<int>> drainAvailableFrames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendPendingPacket);
        ArgumentNullException.ThrowIfNull(drainAvailableFrames);
        for (var attempt = 0; attempt < MaximumSendAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = sendPendingPacket();
            if (result == PlaybackMetadataAwareNativeSendResult.Success)
            {
                return;
            }

            if (result != PlaybackMetadataAwareNativeSendResult.Again)
            {
                throw new InvalidOperationException(
                    $"FFmpeg packet submission failed: {result}.");
            }

            var frames = await drainAvailableFrames(cancellationToken)
                .ConfigureAwait(false);
            if (frames <= 0)
            {
                throw new InvalidOperationException(
                    "FFmpeg requested a frame drain but produced no frame before packet resend.");
            }
        }

        throw new InvalidOperationException("FFmpeg packet resend limit was exceeded.");
    }
}
