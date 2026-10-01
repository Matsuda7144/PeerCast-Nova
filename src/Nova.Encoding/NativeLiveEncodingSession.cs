using PeerCast.Nova.Core.Media;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.MediaEncoding;

internal interface ILiveEncodingSession : IDisposable
{
    int InitialPaddingSamples { get; }
    IReadOnlyList<Fmp4MuxTrackDescription> Tracks { get; }
    MediaEncoderSnapshot VideoSnapshot { get; }
    MediaEncoderSnapshot AudioSnapshot { get; }
    IReadOnlyList<Fmp4MuxEncodedSample> Encode(LiveMediaInput input);
    IReadOnlyList<Fmp4MuxEncodedSample> Complete();
}

internal sealed class NativeLiveEncodingSession : ILiveEncodingSession
{
    private readonly H264FfmpegEncoderSession _video;
    private readonly AacFfmpegEncoderSession _audio;

    public NativeLiveEncodingSession(VerifiedPlaybackDecoderRuntimeLease runtime)
    {
        _video = new(runtime, new(64, 48, keyFrameInterval: 24));
        try { _audio = new(runtime, new()); }
        catch { _video.Dispose(); throw; }
        try
        {
            if (_audio.InitialPaddingSamples < 0 || _audio.SamplesPerInputFrame != 1024)
            {
                throw new InvalidOperationException("The live AAC profile is unsupported.");
            }
            Tracks = Array.AsReadOnly<Fmp4MuxTrackDescription>([
                new(1, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1, 48_000,
                    _video.CodecConfiguration.ConfigurationRecord, width: 64, height: 48),
                new(2, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc, 48_000,
                    _audio.CodecConfiguration, sampleRate: 48_000, channelCount: 2),
            ]);
        }
        catch { Dispose(); throw; }
    }

    public int InitialPaddingSamples => _audio.InitialPaddingSamples;
    public IReadOnlyList<Fmp4MuxTrackDescription> Tracks { get; }
    public MediaEncoderSnapshot VideoSnapshot => _video.Snapshot;
    public MediaEncoderSnapshot AudioSnapshot => _audio.Snapshot;

    public IReadOnlyList<Fmp4MuxEncodedSample> Encode(LiveMediaInput input)
    {
        if (input.Video is { } video && input.Audio is null)
        {
            return Convert(_video.Encode(video), video: true);
        }
        if (input.Audio is { } audio && input.Video is null)
        {
            return Convert(_audio.Encode(audio), video: false);
        }
        throw new ArgumentException("Exactly one raw media event is required.", nameof(input));
    }

    public IReadOnlyList<Fmp4MuxEncodedSample> Complete() =>
        Convert(_video.Complete(), video: true).Concat(Convert(_audio.Complete(), video: false)).ToArray();

    private Fmp4MuxEncodedSample[] Convert(IReadOnlyList<MediaEncodedSample> packets, bool video)
    {
        var multiplier = video ? 1600L : 1L;
        return packets.Select(packet => new Fmp4MuxEncodedSample(video ? 1U : 2U,
            checked(packet.DecodeTimestamp * multiplier + InitialPaddingSamples),
            checked(packet.PresentationTimestamp * multiplier + InitialPaddingSamples),
            checked(packet.Duration * multiplier), packet.Payload,
            video ? (packet.IsKeyFrame ? Fmp4MuxRandomAccessKind.IndependentlyDecodable :
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable) : Fmp4MuxRandomAccessKind.NotApplicable)).ToArray();
    }

    public void Dispose()
    {
        try { _audio.Dispose(); }
        finally { _video.Dispose(); }
    }
}
