using PeerCast.Nova.Playback;

namespace PeerCast.Nova.MediaEncoding;

/// <summary>
/// Finite AAC-LC session. Copies interleaved PCM into native planar float buffers;
/// emits owned raw AAC access units and preserves encoder priming timestamps.
/// </summary>
public sealed class AacFfmpegEncoderSession : IDisposable
{
    private readonly object _sync = new();
    private readonly FfmpegEncoderNativeSession _native;
    private readonly byte[] _configuration;
    private long _inputCount;
    private long _packetCount;
    private long _flushCount;
    private bool _completed;
    private bool _disposed;

    public AacFfmpegEncoderSession(VerifiedPlaybackDecoderRuntimeLease runtime,
        AacEncoderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _native = FfmpegEncoderNativeSession.Create(runtime, null, configuration);
        try
        {
            _configuration = _native.Extradata.ToArray();
            // The opened native AAC encoder supplies the ASC; no ADTS muxer is involved.
            if (_configuration.Length < 2 || (_configuration[0] >> 3) != 2 ||
                (((_configuration[0] & 7) << 1) | (_configuration[1] >> 7)) != 3 ||
                ((_configuration[1] >> 3) & 15) != 2)
            {
                throw new InvalidOperationException("Encoder ASC is not 48 kHz stereo AAC-LC.");
            }
        }
        catch
        {
            _native.Dispose();
            throw;
        }
    }

    public string EncoderName { get; } = "aac";
    public int TimeBaseNumerator { get; } = 1;
    public int TimeBaseDenominator { get; } = 48_000;
    public int SamplesPerInputFrame => _native.FrameSize;
    public int InitialPaddingSamples => _native.InitialPadding;
    public ReadOnlyMemory<byte> CodecConfiguration => _configuration.ToArray();
    public string OutputPacketFraming { get; } = "Raw AAC access units (no ADTS)";
    public MediaEncoderSnapshot Snapshot
    {
        get { lock (_sync) { return new(_inputCount, _packetCount, _flushCount, _completed); } }
    }

    public IReadOnlyList<MediaEncodedSample> Encode(RawPcmAudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_sync)
        {
            EnsureActive();
            if (frame.PresentationTimestamp != checked(_inputCount * SamplesPerInputFrame))
            {
                throw new ArgumentException("PCM PTS must count contiguous input samples.", nameof(frame));
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
                var packets = Normalize(_native.Complete(isVideo: false));
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
            packet.Dts, packet.Pts, packet.Duration, false, packet.Payload)).ToArray();
        _packetCount = checked(_packetCount + output.Length);
        return Array.AsReadOnly(output);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) { throw new InvalidOperationException("The AAC encoder has completed."); }
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
