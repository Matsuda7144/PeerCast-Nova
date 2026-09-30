using System.Buffers.Binary;

namespace PeerCast.Nova.Core.Media;

public enum Fmp4MuxMediaKind
{
    Video,
    Audio,
}

public enum Fmp4MuxCodec
{
    Avc1,
    AacLc,
}

public enum Fmp4MuxRandomAccessKind
{
    Indeterminate,
    IndependentlyDecodable,
    NotIndependentlyDecodable,
    NotApplicable,
}

public enum Fmp4MuxerCreateResult
{
    Created,
    InvalidInput,
    UnsupportedProfile,
    InvalidOptions,
}

public enum Fmp4MuxerResult
{
    Produced,
    InvalidState,
    InvalidInput,
    LimitExceeded,
    SequenceExhausted,
    InternalFailure,
}

public enum Fmp4MuxerState
{
    Created,
    Ready,
    Faulted,
    Disposed,
}

/// <summary>
/// Immutable track metadata and codec configuration for Nova's initial
/// AVC1/AAC-LC fragmented MP4 profile. Configuration bytes are copied on input.
/// </summary>
public sealed class Fmp4MuxTrackDescription
{
    private readonly byte[] _codecConfiguration;

    public Fmp4MuxTrackDescription(
        uint trackId,
        Fmp4MuxMediaKind mediaKind,
        Fmp4MuxCodec codec,
        uint mediaTimescale,
        ReadOnlyMemory<byte> codecConfiguration,
        int width = 0,
        int height = 0,
        int sampleRate = 0,
        int channelCount = 0)
    {
        TrackId = trackId;
        MediaKind = mediaKind;
        Codec = codec;
        MediaTimescale = mediaTimescale;
        _codecConfiguration = codecConfiguration.ToArray();
        Width = width;
        Height = height;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
    }

    public uint TrackId { get; }

    public Fmp4MuxMediaKind MediaKind { get; }

    public Fmp4MuxCodec Codec { get; }

    public uint MediaTimescale { get; }

    /// <summary>Returns a defensive copy of the immutable codec configuration.</summary>
    public ReadOnlyMemory<byte> CodecConfiguration => _codecConfiguration.ToArray();

    // Internal readers can use the stable private bytes without a public copy.
    internal ReadOnlySpan<byte> CodecConfigurationSpan => _codecConfiguration;

    public int Width { get; }

    public int Height { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }
}

/// <summary>
/// One borrowed encoded access unit. Payload is read only during the
/// synchronous MuxFragment call; the muxer never retains caller payload memory.
/// </summary>
public readonly record struct Fmp4MuxEncodedSample(
    uint TrackId,
    long DtsTicks,
    long PtsTicks,
    long DurationTicks,
    ReadOnlyMemory<byte> Payload,
    Fmp4MuxRandomAccessKind RandomAccessKind);

public sealed record Fmp4MuxerOptions
{
    public const int MaximumInitializationSegmentBytesHardLimit = 8 * 1024 * 1024;
    public const int MaximumMediaSegmentBytesHardLimit = 64 * 1024 * 1024;
    public const int MaximumSampleBytesHardLimit = 32 * 1024 * 1024;
    public const int MaximumSamplesPerFragmentHardLimit = 65_536;

    public int MaximumInitializationSegmentBytes { get; init; } = 1024 * 1024;

    public int MaximumMediaSegmentBytes { get; init; } = 16 * 1024 * 1024;

    public int MaximumFragmentPayloadBytes { get; init; } = 15 * 1024 * 1024;

    public int MaximumSampleBytes { get; init; } = 4 * 1024 * 1024;

    public int MaximumSamplesPerFragment { get; init; } = 4_096;

    public uint InitialFragmentSequenceNumber { get; init; } = 1;

    internal bool IsValid =>
        MaximumInitializationSegmentBytes is > 0 and
            <= MaximumInitializationSegmentBytesHardLimit &&
        MaximumMediaSegmentBytes is > 0 and
            <= MaximumMediaSegmentBytesHardLimit &&
        MaximumFragmentPayloadBytes is > 0 &&
        MaximumFragmentPayloadBytes <= MaximumMediaSegmentBytes - 8 &&
        MaximumSampleBytes is > 0 and <= MaximumSampleBytesHardLimit &&
        MaximumSampleBytes <= MaximumFragmentPayloadBytes &&
        MaximumSamplesPerFragment is > 0 and
            <= MaximumSamplesPerFragmentHardLimit &&
        InitialFragmentSequenceNumber != 0;
}

public sealed record Fmp4MuxMediaSegment(uint SequenceNumber, byte[] Bytes);

public readonly record struct Fmp4MuxerSnapshot(
    Fmp4MuxerState State,
    long InitializationGeneratedCount,
    long MediaFragmentGeneratedCount,
    long InputSampleCount,
    long InputPayloadByteCount,
    long OutputByteCount,
    ulong NextFragmentSequenceNumber,
    long ValidationFailureCount);

/// <summary>
/// Deterministic, bounded managed writer for fragmented ISO BMFF carrying one
/// AVC1 video track and one AAC-LC audio track. It is independent of PCP,
/// playback, decoder, capture, and native runtime projects.
/// </summary>
public sealed class Fmp4Muxer : IDisposable
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Mvhd = 0x6d766864;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const uint Mdia = 0x6d646961;
    private const uint Mdhd = 0x6d646864;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Vmhd = 0x766d6864;
    private const uint Smhd = 0x736d6864;
    private const uint Dinf = 0x64696e66;
    private const uint Dref = 0x64726566;
    private const uint Url = 0x75726c20;
    private const uint Stbl = 0x7374626c;
    private const uint Stsd = 0x73747364;
    private const uint Stts = 0x73747473;
    private const uint Stsc = 0x73747363;
    private const uint Stsz = 0x7374737a;
    private const uint Stco = 0x7374636f;
    private const uint Avc1 = 0x61766331;
    private const uint AvcC = 0x61766343;
    private const uint Mp4a = 0x6d703461;
    private const uint Esds = 0x65736473;
    private const uint Mvex = 0x6d766578;
    private const uint Trex = 0x74726578;
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;
    private const uint Tfdt = 0x74666474;
    private const uint Trun = 0x7472756e;
    private const uint Mdat = 0x6d646174;
    private const uint DefaultBaseIsMoof = 0x020000;
    private const uint TrackRunFlags = 0x000f01;
    private const int TrackRunFixedLength = 20;
    private const int TrackRunSampleLength = 16;
    private const uint SampleDependsOnOthersFlag = 0x01000000;
    private const uint SampleDoesNotDependOnOthersFlag = 0x02000000;
    private const uint SampleIsNonSyncSampleFlag = 0x00010000;
    private const uint VideoSyncSampleFlags = SampleDoesNotDependOnOthersFlag;
    private const uint VideoDependentSampleFlags =
        SampleDependsOnOthersFlag | SampleIsNonSyncSampleFlag;

    private readonly object _gate = new();
    private readonly Fmp4MuxTrackDescription[] _tracks;
    private readonly Fmp4MuxTrackDescription _videoTrack;
    private readonly Fmp4MuxTrackDescription _audioTrack;
    private readonly Fmp4MuxerOptions _options;
    private readonly Dictionary<uint, ulong> _nextDecodeTimeByTrack = [];
    private Fmp4MuxerState _state = Fmp4MuxerState.Created;
    private byte[]? _initializationSegment;
    private ulong _nextFragmentSequenceNumber;
    private long _mediaFragmentGeneratedCount;
    private long _inputSampleCount;
    private long _inputPayloadByteCount;
    private long _outputByteCount;
    private long _validationFailureCount;

    private Fmp4Muxer(
        Fmp4MuxTrackDescription[] tracks,
        Fmp4MuxerOptions options,
        Fmp4MuxTrackDescription videoTrack,
        Fmp4MuxTrackDescription audioTrack)
    {
        _tracks = tracks;
        _options = options with { };
        _videoTrack = videoTrack;
        _audioTrack = audioTrack;
        _nextFragmentSequenceNumber = options.InitialFragmentSequenceNumber;
    }

    public static Fmp4MuxerCreateResult Create(
        IReadOnlyList<Fmp4MuxTrackDescription>? tracks,
        out Fmp4Muxer? muxer,
        Fmp4MuxerOptions? options = null)
    {
        muxer = null;
        options ??= new Fmp4MuxerOptions();
        if (!options.IsValid)
        {
            return Fmp4MuxerCreateResult.InvalidOptions;
        }

        if (tracks is null || tracks.Count != 2)
        {
            return Fmp4MuxerCreateResult.InvalidInput;
        }

        Fmp4MuxTrackDescription[] snapshot;
        try
        {
            snapshot = new Fmp4MuxTrackDescription[tracks.Count];
            for (var index = 0; index < snapshot.Length; index++)
            {
                snapshot[index] = tracks[index];
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            IndexOutOfRangeException)
        {
            return Fmp4MuxerCreateResult.InvalidInput;
        }

        if (snapshot.Any(static track => track is null) ||
            snapshot.Any(static track => track.TrackId == 0 ||
                track.TrackId == uint.MaxValue || track.MediaTimescale == 0) ||
            snapshot.Select(static track => track.TrackId).Distinct().Count() != 2)
        {
            return Fmp4MuxerCreateResult.InvalidInput;
        }

        Fmp4MuxTrackDescription? video = null;
        Fmp4MuxTrackDescription? audio = null;
        foreach (var track in snapshot)
        {
            switch (track.MediaKind)
            {
                case Fmp4MuxMediaKind.Video:
                    if (video is not null)
                    {
                        return Fmp4MuxerCreateResult.InvalidInput;
                    }

                    video = track;
                    break;
                case Fmp4MuxMediaKind.Audio:
                    if (audio is not null)
                    {
                        return Fmp4MuxerCreateResult.InvalidInput;
                    }

                    audio = track;
                    break;
                default:
                    return Fmp4MuxerCreateResult.InvalidInput;
            }
        }

        if (video is null || audio is null)
        {
            return Fmp4MuxerCreateResult.InvalidInput;
        }

        if (video.Codec != Fmp4MuxCodec.Avc1 ||
            audio.Codec != Fmp4MuxCodec.AacLc ||
            !IsValidVideoTrack(video) || !IsValidAudioTrack(audio))
        {
            return Fmp4MuxerCreateResult.UnsupportedProfile;
        }

        if (video.CodecConfigurationSpan.Length > 64 * 1024 ||
            audio.CodecConfigurationSpan.Length > 64 * 1024)
        {
            return Fmp4MuxerCreateResult.InvalidInput;
        }

        if (!IsValidAvcConfiguration(video.CodecConfigurationSpan) ||
            !IsValidAacConfiguration(
                audio.CodecConfigurationSpan,
                audio.SampleRate,
                audio.ChannelCount))
        {
            return Fmp4MuxerCreateResult.UnsupportedProfile;
        }

        muxer = new(snapshot, options, video, audio);
        return Fmp4MuxerCreateResult.Created;
    }

    public Fmp4MuxerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new(
                    _state,
                    _initializationSegment is null ? 0 : 1,
                    _mediaFragmentGeneratedCount,
                    _inputSampleCount,
                    _inputPayloadByteCount,
                    _outputByteCount,
                    _nextFragmentSequenceNumber,
                    _validationFailureCount);
            }
        }
    }

    /// <summary>
    /// Generates initialization metadata once. Repeated successful calls
    /// return independent copies of the same deterministic bytes.
    /// </summary>
    public Fmp4MuxerResult GenerateInitializationSegment(out byte[]? segment)
    {
        lock (_gate)
        {
            segment = null;
            if (_state == Fmp4MuxerState.Disposed ||
                _state == Fmp4MuxerState.Faulted)
            {
                return Fmp4MuxerResult.InvalidState;
            }

            if (_initializationSegment is null)
            {
                try
                {
                    var generated = BuildInitializationSegment();
                    if (generated.Length > _options.MaximumInitializationSegmentBytes)
                    {
                        CountValidationFailure();
                        return Fmp4MuxerResult.LimitExceeded;
                    }

                    _initializationSegment = generated;
                    _state = Fmp4MuxerState.Ready;
                    _outputByteCount = checked(_outputByteCount + generated.Length);
                }
                catch (Exception exception) when (
                    exception is OverflowException or InvalidDataException)
                {
                    _state = Fmp4MuxerState.Faulted;
                    return Fmp4MuxerResult.InternalFailure;
                }
            }

            segment = _initializationSegment.ToArray();
            return Fmp4MuxerResult.Produced;
        }
    }

    /// <summary>
    /// Writes one bounded moof/mdat pair. Input sample payload memory is
    /// borrowed only until this synchronous call returns.
    /// </summary>
    public Fmp4MuxerResult MuxFragment(
        IReadOnlyList<Fmp4MuxEncodedSample>? samples,
        out Fmp4MuxMediaSegment? segment)
    {
        lock (_gate)
        {
            segment = null;
            if (_state is Fmp4MuxerState.Disposed or Fmp4MuxerState.Faulted)
            {
                return Fmp4MuxerResult.InvalidState;
            }

            if (_initializationSegment is null)
            {
                return Fmp4MuxerResult.InvalidState;
            }

            if (samples is null || samples.Count is <= 0 ||
                samples.Count > _options.MaximumSamplesPerFragment)
            {
                CountValidationFailure();
                return samples is not null &&
                    samples.Count > _options.MaximumSamplesPerFragment
                    ? Fmp4MuxerResult.LimitExceeded
                    : Fmp4MuxerResult.InvalidInput;
            }

            if (_nextFragmentSequenceNumber > uint.MaxValue)
            {
                CountValidationFailure();
                return Fmp4MuxerResult.SequenceExhausted;
            }

            var preflightResult = PrepareSamples(
                samples,
                out var videoSamples,
                out var audioSamples,
                out var payloadBytes);
            if (preflightResult != Fmp4MuxerResult.Produced)
            {
                CountValidationFailure();
                return preflightResult;
            }

            int moofLength;
            int mediaLength;
            int videoPayloadLength;
            try
            {
                videoPayloadLength = SumPayloadLengths(videoSamples);
                var audioPayloadLength = SumPayloadLengths(audioSamples);
                if (checked((long)videoPayloadLength + audioPayloadLength) != payloadBytes)
                {
                    CountValidationFailure();
                    _state = Fmp4MuxerState.Faulted;
                    return Fmp4MuxerResult.InternalFailure;
                }

                moofLength = CalculateMoofLength(videoSamples.Count, audioSamples.Count);
                mediaLength = checked(moofLength + 8 + checked((int)payloadBytes));
            }
            catch (OverflowException)
            {
                CountValidationFailure();
                return Fmp4MuxerResult.LimitExceeded;
            }

            if (mediaLength > _options.MaximumMediaSegmentBytes ||
                payloadBytes > (long)uint.MaxValue - 8)
            {
                CountValidationFailure();
                return Fmp4MuxerResult.LimitExceeded;
            }

            var sequence = checked((uint)_nextFragmentSequenceNumber);
            byte[] generated;
            try
            {
                generated = BuildMediaSegment(
                    sequence,
                    videoSamples,
                    audioSamples,
                    moofLength,
                    videoPayloadLength,
                    mediaLength);
            }
            catch (Exception exception) when (
                exception is OverflowException or InvalidDataException)
            {
                CountValidationFailure();
                _state = Fmp4MuxerState.Faulted;
                return Fmp4MuxerResult.InternalFailure;
            }

            foreach (var sample in videoSamples)
            {
                _nextDecodeTimeByTrack[_videoTrack.TrackId] =
                    checked(sample.DecodeTime + sample.Duration);
            }

            foreach (var sample in audioSamples)
            {
                _nextDecodeTimeByTrack[_audioTrack.TrackId] =
                    checked(sample.DecodeTime + sample.Duration);
            }

            _nextFragmentSequenceNumber++;
            _mediaFragmentGeneratedCount = checked(_mediaFragmentGeneratedCount + 1);
            _inputSampleCount = checked(_inputSampleCount + samples.Count);
            _inputPayloadByteCount = checked(_inputPayloadByteCount + payloadBytes);
            _outputByteCount = checked(_outputByteCount + generated.Length);
            segment = new(sequence, generated);
            return Fmp4MuxerResult.Produced;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == Fmp4MuxerState.Disposed)
            {
                return;
            }

            _initializationSegment = null;
            _nextDecodeTimeByTrack.Clear();
            _state = Fmp4MuxerState.Disposed;
        }
    }

    private Fmp4MuxerResult PrepareSamples(
        IReadOnlyList<Fmp4MuxEncodedSample> source,
        out List<PreparedSample> videoSamples,
        out List<PreparedSample> audioSamples,
        out long totalPayloadBytes)
    {
        videoSamples = [];
        audioSamples = [];
        totalPayloadBytes = 0;
        var snapshot = new Fmp4MuxEncodedSample[source.Count];
        try
        {
            for (var index = 0; index < source.Count; index++)
            {
                snapshot[index] = source[index];
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            IndexOutOfRangeException)
        {
            return Fmp4MuxerResult.InvalidInput;
        }

        ulong nextDtsVideo = _nextDecodeTimeByTrack.GetValueOrDefault(
            _videoTrack.TrackId);
        ulong nextDtsAudio = _nextDecodeTimeByTrack.GetValueOrDefault(
            _audioTrack.TrackId);
        foreach (var sample in snapshot)
        {
            var isVideo = sample.TrackId == _videoTrack.TrackId;
            var isAudio = sample.TrackId == _audioTrack.TrackId;
            if ((!isVideo && !isAudio) || sample.Payload.IsEmpty ||
                sample.Payload.Length > _options.MaximumSampleBytes ||
                sample.DtsTicks < 0 || sample.DurationTicks <= 0 ||
                sample.DurationTicks > uint.MaxValue ||
                !Enum.IsDefined(sample.RandomAccessKind))
            {
                return sample.Payload.Length > _options.MaximumSampleBytes
                    ? Fmp4MuxerResult.LimitExceeded
                    : Fmp4MuxerResult.InvalidInput;
            }

            if ((isVideo && sample.RandomAccessKind is not
                    (Fmp4MuxRandomAccessKind.IndependentlyDecodable or
                     Fmp4MuxRandomAccessKind.NotIndependentlyDecodable)) ||
                (isAudio && sample.RandomAccessKind !=
                    Fmp4MuxRandomAccessKind.NotApplicable))
            {
                return Fmp4MuxerResult.InvalidInput;
            }

            if (!TryGetCompositionOffset(
                    sample.DtsTicks,
                    sample.PtsTicks,
                    out var compositionOffset))
            {
                return Fmp4MuxerResult.InvalidInput;
            }

            var currentDts = (ulong)sample.DtsTicks;
            var hasTrackSampleInFragment = isVideo
                ? videoSamples.Count != 0
                : audioSamples.Count != 0;
            var nextDts = isVideo ? nextDtsVideo : nextDtsAudio;
            if (hasTrackSampleInFragment
                ? currentDts != nextDts
                : currentDts < nextDts)
            {
                return Fmp4MuxerResult.InvalidInput;
            }

            var prepared = new PreparedSample(
                currentDts,
                checked((uint)sample.DurationTicks),
                sample.Payload,
                compositionOffset,
                sample.RandomAccessKind);
            if (isVideo)
            {
                videoSamples.Add(prepared);
                nextDtsVideo = checked(currentDts + prepared.Duration);
            }
            else
            {
                audioSamples.Add(prepared);
                nextDtsAudio = checked(currentDts + prepared.Duration);
            }

            totalPayloadBytes = checked(totalPayloadBytes + sample.Payload.Length);
            if (totalPayloadBytes > _options.MaximumFragmentPayloadBytes)
            {
                return Fmp4MuxerResult.LimitExceeded;
            }
        }

        if (videoSamples.Count == 0 && audioSamples.Count == 0)
        {
            return Fmp4MuxerResult.InvalidInput;
        }

        return Fmp4MuxerResult.Produced;
    }

    private byte[] BuildInitializationSegment()
    {
        var ftypPayload = new byte[16];
        WriteFourCc(ftypPayload, 0, FourCc("iso6"));
        BinaryPrimitives.WriteUInt32BigEndian(ftypPayload.AsSpan(4, 4), 0);
        WriteFourCc(ftypPayload, 8, FourCc("iso6"));
        WriteFourCc(ftypPayload, 12, FourCc("dash"));
        var ftyp = Box(Ftyp, ftypPayload);

        var mvhdPayload = new byte[96];
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(8, 4), 1_000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(16, 4), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(mvhdPayload.AsSpan(20, 2), 0x0100);
        WriteIdentityMatrix(mvhdPayload.AsSpan(32, 36));
        BinaryPrimitives.WriteUInt32BigEndian(
            mvhdPayload.AsSpan(92, 4),
            checked(_tracks.Max(static track => track.TrackId) + 1));
        var mvhd = FullBox(Mvhd, 0, 0, mvhdPayload);

        var videoTrak = BuildTrack(_videoTrack);
        var audioTrak = BuildTrack(_audioTrack);
        var mvex = Box(
            Mvex,
            BuildTrex(_videoTrack.TrackId),
            BuildTrex(_audioTrack.TrackId));
        var moov = Box(Moov, mvhd, videoTrak, audioTrak, mvex);
        return Concat(ftyp, moov);
    }

    private static byte[] BuildTrack(Fmp4MuxTrackDescription track)
    {
        var tkhdPayload = new byte[80];
        BinaryPrimitives.WriteUInt32BigEndian(tkhdPayload.AsSpan(8, 4), track.TrackId);
        BinaryPrimitives.WriteUInt32BigEndian(tkhdPayload.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(tkhdPayload.AsSpan(32, 2),
            track.MediaKind == Fmp4MuxMediaKind.Audio ? (ushort)0x0100 : (ushort)0);
        WriteIdentityMatrix(tkhdPayload.AsSpan(36, 36));
        if (track.MediaKind == Fmp4MuxMediaKind.Video)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                tkhdPayload.AsSpan(72, 4),
                checked((uint)track.Width << 16));
            BinaryPrimitives.WriteUInt32BigEndian(
                tkhdPayload.AsSpan(76, 4),
                checked((uint)track.Height << 16));
        }

        var tkhd = FullBox(Tkhd, 0, 7, tkhdPayload);
        var mdhdPayload = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(8, 4), track.MediaTimescale);
        mdhdPayload[16] = 0x55;
        mdhdPayload[17] = 0xc4;
        var mdhd = FullBox(Mdhd, 0, 0, mdhdPayload);

        var hdlrPayload = new byte[33];
        WriteFourCc(hdlrPayload, 4,
            track.MediaKind == Fmp4MuxMediaKind.Video
                ? FourCc("vide")
                : FourCc("soun"));
        var name = track.MediaKind == Fmp4MuxMediaKind.Video
            ? "VideoHandler\0"u8
            : "SoundHandler\0"u8;
        name.CopyTo(hdlrPayload.AsSpan(20));
        var hdlr = FullBox(Hdlr, 0, 0, hdlrPayload);

        var sampleEntry = track.MediaKind == Fmp4MuxMediaKind.Video
            ? BuildAvc1SampleEntry(track)
            : BuildMp4aSampleEntry(track);
        var stsdCount = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(stsdCount, 1);
        var stsd = FullBox(Stsd, 0, 0, stsdCount, sampleEntry);
        var stts = FullBox(Stts, 0, 0, new byte[4]);
        var stsc = FullBox(Stsc, 0, 0, new byte[4]);
        var stsz = FullBox(Stsz, 0, 0, new byte[8]);
        var stco = FullBox(Stco, 0, 0, new byte[4]);
        var stbl = Box(Stbl, stsd, stts, stsc, stsz, stco);

        var dataReference = FullBox(
            Dref,
            0,
            0,
            UInt32Bytes(1),
            FullBox(Url, 0, 1));
        var dinf = Box(Dinf, dataReference);
        var mediaHeader = track.MediaKind == Fmp4MuxMediaKind.Video
            ? FullBox(Vmhd, 0, 1, new byte[8])
            : FullBox(Smhd, 0, 0, new byte[4]);
        var minf = Box(Minf, mediaHeader, dinf, stbl);
        var mdia = Box(Mdia, mdhd, hdlr, minf);
        return Box(Trak, tkhd, mdia);
    }

    private static byte[] BuildAvc1SampleEntry(Fmp4MuxTrackDescription track)
    {
        var visual = new byte[78];
        BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(24, 2),
            checked((ushort)track.Width));
        BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(26, 2),
            checked((ushort)track.Height));
        BinaryPrimitives.WriteUInt32BigEndian(visual.AsSpan(28, 4), 0x00480000);
        BinaryPrimitives.WriteUInt32BigEndian(visual.AsSpan(32, 4), 0x00480000);
        BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(40, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(74, 2), 0x0018);
        BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(76, 2), 0xffff);
        return Box(Avc1, visual, Box(AvcC, track.CodecConfigurationSpan.ToArray()));
    }

    private static byte[] BuildMp4aSampleEntry(Fmp4MuxTrackDescription track)
    {
        var audio = new byte[28];
        BinaryPrimitives.WriteUInt16BigEndian(audio.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(audio.AsSpan(16, 2),
            checked((ushort)track.ChannelCount));
        BinaryPrimitives.WriteUInt16BigEndian(audio.AsSpan(18, 2), 16);
        BinaryPrimitives.WriteUInt32BigEndian(audio.AsSpan(24, 4),
            checked((uint)track.SampleRate << 16));
        return Box(Mp4a, audio, BuildElementaryStreamDescriptor(track.CodecConfigurationSpan));
    }

    private static byte[] BuildElementaryStreamDescriptor(ReadOnlySpan<byte> audioSpecificConfig)
    {
        var decoderSpecific = BuildDescriptor(0x05, audioSpecificConfig);
        var decoderConfigPayload = new byte[13 + decoderSpecific.Length];
        decoderConfigPayload[0] = 0x40;
        decoderConfigPayload[1] = 0x15;
        decoderSpecific.CopyTo(decoderConfigPayload.AsSpan(13));
        var decoderConfig = BuildDescriptor(0x04, decoderConfigPayload);
        var slConfig = BuildDescriptor(0x06, [0x02]);
        var elementaryPayload = new byte[3 + decoderConfig.Length + slConfig.Length];
        BinaryPrimitives.WriteUInt16BigEndian(elementaryPayload.AsSpan(0, 2), 0);
        decoderConfig.CopyTo(elementaryPayload.AsSpan(3));
        slConfig.CopyTo(elementaryPayload.AsSpan(3 + decoderConfig.Length));
        var elementary = BuildDescriptor(0x03, elementaryPayload);
        return FullBox(Esds, 0, 0, elementary);
    }

    private static byte[] BuildTrex(uint trackId)
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), 1);
        return FullBox(Trex, 0, 0, payload);
    }

    private byte[] BuildMediaSegment(
        uint sequence,
        IReadOnlyList<PreparedSample> videoSamples,
        IReadOnlyList<PreparedSample> audioSamples,
        int moofLength,
        int videoPayloadLength,
        int mediaLength)
    {
        var moof = BuildMovieFragment(
            sequence,
            videoSamples,
            audioSamples,
            moofLength,
            videoPayloadLength);
        if (moof.Length != moofLength)
        {
            throw new InvalidDataException("The generated moof length changed after preflight.");
        }

        var payloadBytes = checked(videoPayloadLength +
            SumPayloadLengths(audioSamples));
        var mdatLength = checked(payloadBytes + 8);
        var output = new byte[mediaLength];
        moof.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            output.AsSpan(moofLength, 4),
            checked((uint)mdatLength));
        WriteFourCc(output, moofLength + 4, Mdat);
        var cursor = moofLength + 8;
        CopyPayloads(videoSamples, output, ref cursor);
        CopyPayloads(audioSamples, output, ref cursor);
        if (cursor != output.Length)
        {
            throw new InvalidDataException("The generated mdat payload length changed after preflight.");
        }

        return output;
    }

    private byte[] BuildMovieFragment(
        uint sequence,
        IReadOnlyList<PreparedSample> videoSamples,
        IReadOnlyList<PreparedSample> audioSamples,
        int moofLength,
        int videoPayloadLength)
    {
        var mfhdPayload = UInt32Bytes(sequence);
        var mfhd = FullBox(Mfhd, 0, 0, mfhdPayload);
        var trafs = new List<byte[]>(2);
        var videoDataOffset = checked(moofLength + 8);
        if (videoSamples.Count != 0)
        {
            trafs.Add(BuildTrackFragment(
                _videoTrack.TrackId,
                videoSamples,
                videoDataOffset));
        }

        if (audioSamples.Count != 0)
        {
            var audioDataOffset = checked(moofLength + 8 + videoPayloadLength);
            trafs.Add(BuildTrackFragment(
                _audioTrack.TrackId,
                audioSamples,
                audioDataOffset));
        }

        var payload = new byte[checked(mfhd.Length + trafs.Sum(static traf => traf.Length))];
        var cursor = 0;
        mfhd.CopyTo(payload, cursor);
        cursor += mfhd.Length;
        foreach (var traf in trafs)
        {
            traf.CopyTo(payload, cursor);
            cursor += traf.Length;
        }

        var moof = Box(Moof, payload);
        if (moof.Length != moofLength)
        {
            throw new InvalidDataException("The generated moof size differs from its preflight size.");
        }

        return moof;
    }

    private static byte[] BuildTrackFragment(
        uint trackId,
        IReadOnlyList<PreparedSample> samples,
        int dataOffset)
    {
        if (dataOffset < 0 || dataOffset > int.MaxValue)
        {
            throw new InvalidDataException("The trun data offset is outside the signed profile range.");
        }

        var tfhdPayload = UInt32Bytes(trackId);
        var tfhd = FullBox(Tfhd, 0, DefaultBaseIsMoof, tfhdPayload);
        var tfdtPayload = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(
            tfdtPayload,
            samples[0].DecodeTime);
        var tfdt = FullBox(Tfdt, 1, 0, tfdtPayload);

        var trunPayload = new byte[checked(8 + samples.Count * TrackRunSampleLength)];
        BinaryPrimitives.WriteUInt32BigEndian(trunPayload.AsSpan(0, 4),
            checked((uint)samples.Count));
        BinaryPrimitives.WriteInt32BigEndian(trunPayload.AsSpan(4, 4), dataOffset);
        var cursor = 8;
        foreach (var sample in samples)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                trunPayload.AsSpan(cursor, 4),
                sample.Duration);
            BinaryPrimitives.WriteUInt32BigEndian(
                trunPayload.AsSpan(cursor + 4, 4),
                checked((uint)sample.Payload.Length));
            BinaryPrimitives.WriteUInt32BigEndian(
                trunPayload.AsSpan(cursor + 8, 4),
                ToSampleFlags(sample.RandomAccessKind));
            BinaryPrimitives.WriteInt32BigEndian(
                trunPayload.AsSpan(cursor + 12, 4),
                sample.CompositionTimeOffset);
            cursor += TrackRunSampleLength;
        }

        var trun = FullBox(Trun, 1, TrackRunFlags, trunPayload);
        return Box(Traf, tfhd, tfdt, trun);
    }

    private static int CalculateMoofLength(int videoCount, int audioCount)
    {
        long length = 8 + 16;
        if (videoCount != 0)
        {
            length = checked(length + CalculateTrafLength(videoCount));
        }

        if (audioCount != 0)
        {
            length = checked(length + CalculateTrafLength(audioCount));
        }

        if (length > int.MaxValue || length > uint.MaxValue)
        {
            throw new OverflowException("The movie fragment box is too large.");
        }

        return checked((int)length);
    }

    private static int CalculateTrafLength(int sampleCount)
    {
        var trunLength = checked(TrackRunFixedLength +
            sampleCount * TrackRunSampleLength);
        return checked(8 + 16 + 20 + trunLength);
    }

    private static uint ToSampleFlags(Fmp4MuxRandomAccessKind randomAccessKind) =>
        randomAccessKind switch
        {
            Fmp4MuxRandomAccessKind.IndependentlyDecodable => VideoSyncSampleFlags,
            Fmp4MuxRandomAccessKind.NotIndependentlyDecodable => VideoDependentSampleFlags,
            _ => 0,
        };

    private static bool IsValidVideoTrack(Fmp4MuxTrackDescription track) =>
        track.Width is > 0 and <= ushort.MaxValue &&
        track.Height is > 0 and <= ushort.MaxValue &&
        track.SampleRate == 0 && track.ChannelCount == 0;

    private static bool IsValidAudioTrack(Fmp4MuxTrackDescription track) =>
        track.Width == 0 && track.Height == 0 &&
        track.SampleRate is > 0 and <= ushort.MaxValue &&
        track.ChannelCount is > 0 and <= 8;

    private static bool IsValidAvcConfiguration(ReadOnlySpan<byte> config)
    {
        if (config.Length < 7 || config[0] != 1 || (config[4] & 0xfc) != 0xfc ||
            (config[5] & 0xe0) != 0xe0)
        {
            return false;
        }

        var lengthSizeMinusOne = config[4] & 0x03;
        if (lengthSizeMinusOne == 2)
        {
            return false;
        }

        var offset = 6;
        var spsCount = config[5] & 0x1f;
        if (spsCount == 0 || !SkipParameterSets(config, ref offset, spsCount))
        {
            return false;
        }

        if (offset >= config.Length)
        {
            return false;
        }

        var ppsCount = config[offset++];
        if (ppsCount == 0 || !SkipParameterSets(config, ref offset, ppsCount))
        {
            return false;
        }

        if (offset != config.Length)
        {
            if (!SupportsHighProfileExtension(config[1]) ||
                config.Length - offset < 4 ||
                (config[offset] & 0xfc) != 0xfc ||
                (config[offset + 1] & 0xf8) != 0xf8 ||
                (config[offset + 2] & 0xf8) != 0xf8)
            {
                return false;
            }

            offset += 3;
            var extensionCount = config[offset++];
            if (!SkipParameterSets(config, ref offset, extensionCount))
            {
                return false;
            }
        }

        return offset == config.Length;
    }

    private static bool SupportsHighProfileExtension(byte profile) =>
        profile is 100 or 110 or 122 or 144;

    private static bool SkipParameterSets(
        ReadOnlySpan<byte> config,
        ref int offset,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (config.Length - offset < 2)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(config[offset..]);
            offset += 2;
            if (length == 0 || config.Length - offset < length)
            {
                return false;
            }

            offset += length;
        }

        return true;
    }

    private static bool IsValidAacConfiguration(
        ReadOnlySpan<byte> config,
        int expectedSampleRate,
        int expectedChannels)
    {
        if (config.Length < 2)
        {
            return false;
        }

        var bitOffset = 0;
        if (!TryReadBits(config, ref bitOffset, 5, out var audioObjectType) ||
            audioObjectType != 2 ||
            !TryReadBits(config, ref bitOffset, 4, out var frequencyIndex))
        {
            return false;
        }

        int sampleRate;
        if (frequencyIndex == 15)
        {
            if (!TryReadBits(config, ref bitOffset, 24, out var explicitRate))
            {
                return false;
            }

            sampleRate = checked((int)explicitRate);
        }
        else
        {
            ReadOnlySpan<int> rates = [96_000, 88_200, 64_000, 48_000,
                44_100, 32_000, 24_000, 22_050, 16_000, 12_000, 11_025,
                8_000, 7_350];
            if (frequencyIndex >= rates.Length)
            {
                return false;
            }

            sampleRate = rates[checked((int)frequencyIndex)];
        }

        if (!TryReadBits(config, ref bitOffset, 4, out var channelConfiguration))
        {
            return false;
        }

        var channels = channelConfiguration switch
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
        if (channels != expectedChannels || sampleRate != expectedSampleRate ||
            !TryReadBits(config, ref bitOffset, 1, out _) ||
            !TryReadBits(config, ref bitOffset, 1, out var dependsOnCoreCoder) ||
            !TryReadBits(config, ref bitOffset, 1, out var extensionFlag) ||
            dependsOnCoreCoder != 0 || extensionFlag != 0)
        {
            return false;
        }

        if (bitOffset == config.Length * 8)
        {
            return true;
        }

        // The retained AAC-LC fixture carries the backward-compatible SBR
        // marker with sbrPresentFlag == 0. Preserve only that no-SBR form and
        // its seven zero bits at the end of the byte-sized ASC; reject every
        // other continuation without enabling SBR or PS.
        return IsSbrAbsentSyncExtension(config, bitOffset);
    }

    private static bool IsSbrAbsentSyncExtension(
        ReadOnlySpan<byte> config,
        int bitOffset)
    {
        if (config.Length * 8 - bitOffset != 24)
        {
            return false;
        }

        var extensionBitOffset = bitOffset;
        return TryReadBits(config, ref extensionBitOffset, 11, out var syncExtensionType) &&
            syncExtensionType == 0x2b7 &&
            TryReadBits(config, ref extensionBitOffset, 5, out var extensionAudioObjectType) &&
            extensionAudioObjectType == 5 &&
            TryReadBits(config, ref extensionBitOffset, 1, out var sbrPresentFlag) &&
            sbrPresentFlag == 0 &&
            TryReadBits(config, ref extensionBitOffset, 7, out var byteAlignmentBits) &&
            byteAlignmentBits == 0 &&
            extensionBitOffset == config.Length * 8;
    }

    private static bool TryReadBits(
        ReadOnlySpan<byte> bytes,
        ref int bitOffset,
        int count,
        out uint value)
    {
        value = 0;
        if (count is <= 0 or > 24 || bitOffset > bytes.Length * 8 - count)
        {
            return false;
        }

        for (var bit = 0; bit < count; bit++)
        {
            var absolute = bitOffset++;
            value = (value << 1) |
                (uint)((bytes[absolute / 8] >> (7 - absolute % 8)) & 1);
        }

        return true;
    }

    private static bool TryGetCompositionOffset(
        long dts,
        long pts,
        out int offset)
    {
        offset = 0;
        try
        {
            var difference = checked(pts - dts);
            if (difference is < int.MinValue or > int.MaxValue)
            {
                return false;
            }

            offset = checked((int)difference);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int SumPayloadLengths(IReadOnlyList<PreparedSample> samples)
    {
        var length = 0;
        foreach (var sample in samples)
        {
            length = checked(length + sample.Payload.Length);
        }

        return length;
    }

    private static void CopyPayloads(
        IReadOnlyList<PreparedSample> samples,
        byte[] destination,
        ref int cursor)
    {
        foreach (var sample in samples)
        {
            sample.Payload.Span.CopyTo(destination.AsSpan(cursor));
            cursor = checked(cursor + sample.Payload.Length);
        }
    }

    private static byte[] BuildDescriptor(byte tag, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 0x0fff_ffff)
        {
            throw new OverflowException("An MPEG-4 descriptor is too large.");
        }

        Span<byte> encodedLength = stackalloc byte[4];
        var length = payload.Length;
        var lengthBytes = 1;
        encodedLength[3] = (byte)(length & 0x7f);
        length >>= 7;
        while (length != 0)
        {
            encodedLength[3 - lengthBytes] = (byte)((length & 0x7f) | 0x80);
            length >>= 7;
            lengthBytes++;
        }

        var descriptor = new byte[checked(1 + lengthBytes + payload.Length)];
        descriptor[0] = tag;
        encodedLength[(4 - lengthBytes)..].CopyTo(descriptor.AsSpan(1));
        payload.CopyTo(descriptor.AsSpan(1 + lengthBytes));
        return descriptor;
    }

    private static byte[] Box(uint type, params byte[][] payloads)
    {
        var payloadLength = 0;
        foreach (var payload in payloads)
        {
            payloadLength = checked(payloadLength + payload.Length);
        }

        var boxLength = checked(payloadLength + 8);
        var box = new byte[boxLength];
        BinaryPrimitives.WriteUInt32BigEndian(box, checked((uint)boxLength));
        WriteFourCc(box, 4, type);
        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(box, offset);
            offset = checked(offset + payload.Length);
        }

        return box;
    }

    private static byte[] FullBox(
        uint type,
        byte version,
        uint flags,
        params byte[][] payloads)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(flags, 0x00ff_ffffU);

        var fullBoxHeader = new byte[4];
        fullBoxHeader[0] = version;
        fullBoxHeader[1] = (byte)(flags >> 16);
        fullBoxHeader[2] = (byte)(flags >> 8);
        fullBoxHeader[3] = (byte)flags;
        var allPayloads = new byte[payloads.Length + 1][];
        allPayloads[0] = fullBoxHeader;
        payloads.CopyTo(allPayloads, 1);
        return Box(type, allPayloads);
    }

    private static byte[] Concat(params byte[][] arrays)
    {
        var totalLength = 0;
        foreach (var array in arrays)
        {
            totalLength = checked(totalLength + array.Length);
        }

        var result = new byte[totalLength];
        var offset = 0;
        foreach (var array in arrays)
        {
            array.CopyTo(result, offset);
            offset = checked(offset + array.Length);
        }

        return result;
    }

    private static byte[] UInt32Bytes(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static uint FourCc(string value)
    {
        if (value.Length != 4)
        {
            throw new ArgumentException("A FourCC must contain exactly four characters.", nameof(value));
        }

        return ((uint)value[0] << 24) |
            ((uint)value[1] << 16) |
            ((uint)value[2] << 8) |
            value[3];
    }

    private static void WriteFourCc(Span<byte> destination, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, 4), value);
    }

    private static void WriteIdentityMatrix(Span<byte> matrix)
    {
        BinaryPrimitives.WriteUInt32BigEndian(matrix[..4], 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix.Slice(16, 4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix.Slice(32, 4), 0x40000000);
    }

    private void CountValidationFailure()
    {
        if (_validationFailureCount != long.MaxValue)
        {
            _validationFailureCount++;
        }
    }

    private readonly record struct PreparedSample(
        ulong DecodeTime,
        uint Duration,
        ReadOnlyMemory<byte> Payload,
        int CompositionTimeOffset,
        Fmp4MuxRandomAccessKind RandomAccessKind);
}
