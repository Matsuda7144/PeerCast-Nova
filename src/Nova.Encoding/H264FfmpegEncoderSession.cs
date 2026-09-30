using PeerCast.Nova.Playback;

namespace PeerCast.Nova.MediaEncoding;

/// <summary>Finite, serialized libopenh264 session; emits owned AVCC access units.</summary>
public sealed class H264FfmpegEncoderSession : IDisposable
{
    private readonly object _sync = new();
    private readonly H264EncoderConfiguration _configuration;
    private readonly FfmpegEncoderNativeSession _native;
    private readonly AvcEncoderOutputFraming _framing;
    private long _inputCount;
    private long _packetCount;
    private long _flushCount;
    private bool _completed;
    private bool _disposed;

    public H264FfmpegEncoderSession(VerifiedPlaybackDecoderRuntimeLease runtime,
        H264EncoderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _native = FfmpegEncoderNativeSession.Create(runtime, configuration, null);
        try
        {
            _framing = AvcEncoderOutputFraming.Create(_native.Extradata);
        }
        catch
        {
            _native.Dispose();
            throw;
        }
    }

    public string EncoderName { get; } = "libopenh264";
    public int TimeBaseNumerator { get; } = 1;
    public int TimeBaseDenominator => _configuration.FramesPerSecond;
    public H264EncoderCodecConfiguration CodecConfiguration => _framing.Configuration;
    public string NativePacketFraming => _framing.NativePacketFraming;
    public string OutputPacketFraming { get; } = "AVCC (4-byte NAL lengths)";
    public MediaEncoderSnapshot Snapshot
    {
        get { lock (_sync) { return new(_inputCount, _packetCount, _flushCount, _completed); } }
    }

    public IReadOnlyList<MediaEncodedSample> Encode(RawYuv420VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_sync)
        {
            EnsureActive();
            if (frame.Width != _configuration.Width || frame.Height != _configuration.Height ||
                frame.PresentationTimestamp != _inputCount)
            {
                throw new ArgumentException("Video frames must match configuration and contiguous frame-index PTS.",
                    nameof(frame));
            }

            try
            {
                var packets = Normalize(_native.Encode(frame));
                _inputCount++;
                return packets;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    public IReadOnlyList<MediaEncodedSample> Complete()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed) { return []; }
            try
            {
                var packets = Normalize(_native.Complete(isVideo: true));
                _flushCount = packets.Count;
                _completed = true;
                return packets;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<MediaEncodedSample> Normalize(IReadOnlyList<NativeEncodedPacket> packets)
    {
        var output = packets.Select(packet => new MediaEncodedSample(
            packet.Dts, packet.Pts, packet.Duration, packet.IsKeyFrame,
            _framing.NormalizePacket(packet.Payload, packet.IsKeyFrame))).ToArray();
        _packetCount = checked(_packetCount + output.Length);
        return Array.AsReadOnly(output);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) { throw new InvalidOperationException("The H.264 encoder has completed."); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) { return; }
            _disposed = true;
            _native.Dispose();
        }
    }
}
