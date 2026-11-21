using System.Buffers.Binary;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Playback;

/// <summary>
/// Remuxes borrowed RTMP access units into one local fMP4 generation.
/// Only completed video intervals and admitted AAC frames are published.
/// </summary>
internal sealed class PlaybackObsFmp4Publisher(
    ChannelSessionId sessionId, PlaybackFmp4HttpStreamHost output) : IObsRtmpEncodedMediaConsumer, IDisposable
{
    private const uint VideoTrack = 1;
    private const uint AudioTrack = 2;
    private const int MillisecondsPerSecond = 1_000;
    private const int AacFrameSamples = 1_024;
    private const int AudioTimestampToleranceMs = 2;
    private const int TargetFragmentMs = 500;
    private const int MaximumFragmentSamples = 64;
    private const int MaximumFragmentPayloadBytes = 1024 * 1024;
    private const int MaximumPendingSamples = 256;
    private const int MaximumPendingBytes = 4 * 1024 * 1024;
    private static readonly PlaybackBoundedFlvParserOptions SpsLimits = new();

    private readonly List<Fmp4MuxEncodedSample> _video = [];
    private readonly List<Fmp4MuxEncodedSample> _audio = [];
    private Fmp4Muxer? _muxer;
    private Fmp4MuxEncodedSample? _pendingVideo;
    private uint? _originMilliseconds;
    private long? _nextAudioDts;
    private long _lastVideoDuration;
    private int _sampleRate;
    private int _pendingBytes;
    private bool _ended;

    public long PublishedFragments { get; private set; }
    public string? Rejection { get; private set; }

    public void Configure(ReadOnlySpan<byte> avcDecoderConfiguration, ReadOnlySpan<byte> aacAudioSpecificConfig)
    {
        if (_muxer is not null || _ended ||
            !TryReadVideoSize(avcDecoderConfiguration, out var width, out var height) ||
            !AacLcAudioSpecificConfigParser.TryParse(aacAudioSpecificConfig, out var rate, out var channels, out var shortFrame) ||
            shortFrame)
        {
            throw Reject("OBS fMP4 requires a single AVC resolution and 1,024-sample AAC-LC.");
        }

        Fmp4MuxTrackDescription[] tracks =
        [
            new(VideoTrack, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1,
                MillisecondsPerSecond, avcDecoderConfiguration.ToArray(), width, height),
            new(AudioTrack, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc,
                (uint)rate, aacAudioSpecificConfig.ToArray(), sampleRate: rate, channelCount: channels),
        ];
        var result = Fmp4Muxer.Create(tracks, out var muxer, new()
        {
            MaximumInitializationSegmentBytes = PlaybackFmp4HttpStreamHost.MaximumInitializationBytes,
            MaximumMediaSegmentBytes = PlaybackFmp4HttpStreamHost.MaximumMediaSegmentBytes,
            MaximumFragmentPayloadBytes = MaximumFragmentPayloadBytes,
            MaximumSampleBytes = MaximumFragmentPayloadBytes,
            MaximumSamplesPerFragment = MaximumFragmentSamples,
        });
        if (result != Fmp4MuxerCreateResult.Created || muxer is null)
        {
            throw Reject($"The OBS codec configuration could not initialize fMP4 ({result}).");
        }

        _muxer = muxer;
        _sampleRate = rate;
        if (muxer.GenerateInitializationSegment(out var initialization) != Fmp4MuxerResult.Produced ||
            !output.TryPublishSegment(sessionId, initialization))
        {
            throw Reject("The fMP4 initialization segment was rejected.");
        }
    }

    public void AcceptVideo(
        uint decodeTimestampMilliseconds, int compositionOffsetMilliseconds,
        bool isIdr, ReadOnlySpan<byte> accessUnit)
    {
        RequireActive();
        if (_originMilliseconds is null && !isIdr)
        {
            return;
        }

        _originMilliseconds ??= decodeTimestampMilliseconds;
        var dts = (long)decodeTimestampMilliseconds - _originMilliseconds.Value;
        var pts = dts + compositionOffsetMilliseconds;
        if (dts < 0 || pts < 0 || (_pendingVideo is { } pending && dts <= pending.DtsTicks))
        {
            throw Reject("The AVC decoding timeline changed or an AVC presentation timestamp precedes the stream start.");
        }

        Reserve(accessUnit.Length);
        if (_pendingVideo is { } previous)
        {
            _lastVideoDuration = dts - previous.DtsTicks;
            _video.Add(previous with { DurationTicks = _lastVideoDuration });
        }

        _pendingVideo = new(VideoTrack, dts, pts, 0, accessUnit.ToArray(),
            isIdr ? Fmp4MuxRandomAccessKind.IndependentlyDecodable : Fmp4MuxRandomAccessKind.NotIndependentlyDecodable);
        PublishReadyFragments();
    }

    public void AcceptAudio(uint timestampMilliseconds, ReadOnlySpan<byte> accessUnit)
    {
        RequireActive();
        if (_originMilliseconds is not { } origin || (timestampMilliseconds < origin && _nextAudioDts is null))
        {
            // Start with the first IDR and the first whole audio frame at or after it.
            return;
        }

        var relativeMilliseconds = (long)timestampMilliseconds - origin;
        var fromRtmp = relativeMilliseconds * _sampleRate;
        var dts = _nextAudioDts ?? ((fromRtmp + (MillisecondsPerSecond / 2)) / MillisecondsPerSecond);
        // After the first timestamp, use AAC's sample clock. Allow only the
        // difference introduced by rounding the two RTMP timestamps to ms.
        if (relativeMilliseconds < 0 ||
            Math.Abs(fromRtmp - dts * MillisecondsPerSecond) > (long)AudioTimestampToleranceMs * _sampleRate)
        {
            throw Reject("The AAC timestamps no longer match the continuous sample clock.");
        }

        Reserve(accessUnit.Length);
        _audio.Add(new(AudioTrack, dts, dts, AacFrameSamples, accessUnit.ToArray(),
            Fmp4MuxRandomAccessKind.NotApplicable));
        _nextAudioDts = dts + AacFrameSamples;
        PublishReadyFragments();
    }

    /// <summary>Flushes the remaining samples after an explicit RTMP publisher stop.</summary>
    public void Complete()
    {
        RequireActive();
        if (_pendingVideo is not { } last || _lastVideoDuration == 0 || _nextAudioDts is null)
        {
            throw Reject("The publisher ended before both tracks had a usable timeline.");
        }

        // There is no next DTS at normal EOS. Use the last observed frame interval.
        _video.Add(last with { DurationTicks = _lastVideoDuration });
        _pendingVideo = null;
        PublishPrefix(_video.Count, _audio.Count);
        _ended = true;
    }

    public void Dispose()
    {
        _ended = true;
        _muxer?.Dispose();
        _video.Clear();
        _audio.Clear();
        _pendingVideo = null;
    }

    private void PublishReadyFragments()
    {
        while (_video.Count != 0 && _pendingVideo is { } pending && _nextAudioDts is { } audioEnd)
        {
            var videoStart = _video[0].DtsTicks;
            var videoCount = 1;
            // End immediately before an IDR or after the target duration.
            // The pending video frame already provides the preceding duration.
            for (; videoCount <= _video.Count; videoCount++)
            {
                var next = videoCount == _video.Count ? pending : _video[videoCount];
                if (next.RandomAccessKind == Fmp4MuxRandomAccessKind.IndependentlyDecodable ||
                    next.DtsTicks - videoStart >= TargetFragmentMs)
                {
                    break;
                }
            }

            if (videoCount > _video.Count)
            {
                return;
            }

            var cut = (videoCount == _video.Count ? pending : _video[videoCount]).DtsTicks * _sampleRate;
            if (audioEnd * MillisecondsPerSecond < cut)
            {
                return;
            }

            var audioCount = 0;
            while (audioCount < _audio.Count && _audio[audioCount].DtsTicks * MillisecondsPerSecond < cut)
            {
                audioCount++;
            }

            PublishPrefix(videoCount, audioCount);
        }
    }

    private void PublishPrefix(int videoCount, int audioCount)
    {
        var batch = new List<Fmp4MuxEncodedSample>(MaximumFragmentSamples);
        var bytes = 0;
        var publishedBytes = 0;
        var videoIndex = 0;
        var audioIndex = 0;
        while (videoIndex < videoCount || audioIndex < audioCount)
        {
            var takeVideo = audioIndex == audioCount || (videoIndex < videoCount &&
                _video[videoIndex].DtsTicks * _sampleRate <= _audio[audioIndex].DtsTicks * MillisecondsPerSecond);
            var sample = takeVideo ? _video[videoIndex++] : _audio[audioIndex++];
            if (batch.Count == MaximumFragmentSamples || sample.Payload.Length > MaximumFragmentPayloadBytes - bytes)
            {
                PublishFragment(batch);
                batch.Clear();
                bytes = 0;
            }

            batch.Add(sample);
            bytes += sample.Payload.Length;
            publishedBytes += sample.Payload.Length;
        }

        if (batch.Count != 0)
        {
            PublishFragment(batch);
        }

        _video.RemoveRange(0, videoCount);
        _audio.RemoveRange(0, audioCount);
        _pendingBytes -= publishedBytes;
    }

    private void PublishFragment(List<Fmp4MuxEncodedSample> batch)
    {
        var result = _muxer!.MuxFragment(batch, out var fragment);
        if (result != Fmp4MuxerResult.Produced || fragment is null)
        {
            throw Reject($"The fMP4 muxer rejected an OBS fragment ({result}).");
        }

        if (!output.TryPublishSegment(sessionId, fragment.Bytes))
        {
            throw Reject("The local fMP4 HTTP host rejected an OBS fragment.");
        }

        PublishedFragments++;
    }

    private void RequireActive()
    {
        if (_muxer is null || _ended || Rejection is not null)
        {
            throw new InvalidOperationException("The OBS fMP4 publisher is not active.");
        }
    }

    private void Reserve(int bytes)
    {
        var count = _video.Count + _audio.Count + (_pendingVideo.HasValue ? 1 : 0);
        if (bytes is <= 0 or > MaximumFragmentPayloadBytes ||
            count >= MaximumPendingSamples || bytes > MaximumPendingBytes - _pendingBytes)
        {
            throw Reject("The OBS fMP4 pending access-unit limit was exceeded.");
        }

        _pendingBytes += bytes;
    }

    private InvalidDataException Reject(string reason)
    {
        Rejection = reason;
        return new InvalidDataException(reason);
    }

    private static bool TryReadVideoSize(ReadOnlySpan<byte> configuration, out int width, out int height)
    {
        width = height = 0;
        if (configuration.Length < 8)
        {
            return false;
        }

        var count = configuration[5] & 31;
        var offset = 6;
        for (var index = 0; index < count; index++)
        {
            if (configuration.Length - offset < 2)
            {
                return false;
            }

            var size = BinaryPrimitives.ReadUInt16BigEndian(configuration[offset..]);
            offset += 2;
            if (size > configuration.Length - offset ||
                PlaybackBoundedFlvParser.TryParseSps(configuration.Slice(offset, size), SpsLimits,
                    out var currentWidth, out var currentHeight, out _, out _) != PlaybackBoundedFlvParserResult.PacketsAvailable ||
                (index != 0 && (width != currentWidth || height != currentHeight)))
            {
                return false;
            }

            width = currentWidth;
            height = currentHeight;
            offset += size;
        }

        return count != 0;
    }
}
