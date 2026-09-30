using System.Runtime.InteropServices;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.MediaEncoding;

/// <summary>
/// FFmpeg 8.1 Windows x64 ABI only. Owns one module gate, codec context, frame,
/// and packet. Raw planes and encoded payloads are copied across the boundary.
/// The caller keeps the verified runtime lease alive until session disposal.
/// </summary>
internal sealed class FfmpegEncoderNativeSession : IDisposable
{
    private const int Again = -11;
    private const int EndOfFile = -541478725;
    private const int GlobalHeaderAndFrameDuration = (1 << 22) | (1 << 8);
    private readonly PlaybackDecoderFfmpegAbiSymbolGateLease _gate;
    private readonly Allocate _allocateFrame;
    private readonly Allocate _allocatePacket;
    private readonly Free _freeFrame;
    private readonly Free _freePacket;
    private readonly Free _freeContext;
    private readonly FrameBuffer _getBuffer;
    private readonly UnaryResult _makeWritable;
    private readonly SendReceive _send;
    private readonly SendReceive _receive;
    private readonly UnaryVoid _unrefPacket;
    private readonly ChannelLayout _channelLayout;
    private readonly Dictionary<long, long> _videoDurations = [];
    private nint _context;
    private nint _frame;
    private nint _packet;
    private long? _lastDts;
    private long _lastDuration;
    private bool _completed;
    private bool _disposed;

    private FfmpegEncoderNativeSession(PlaybackDecoderFfmpegAbiSymbolGateLease gate,
        IReadOnlyDictionary<string, nint> symbols)
    {
        _gate = gate;
        _allocateFrame = Get<Allocate>(symbols, "av_frame_alloc");
        _allocatePacket = Get<Allocate>(symbols, "av_packet_alloc");
        _freeFrame = Get<Free>(symbols, "av_frame_free");
        _freePacket = Get<Free>(symbols, "av_packet_free");
        _freeContext = Get<Free>(symbols, "avcodec_free_context");
        _getBuffer = Get<FrameBuffer>(symbols, "av_frame_get_buffer");
        _makeWritable = Get<UnaryResult>(symbols, "av_frame_make_writable");
        _send = Get<SendReceive>(symbols, "avcodec_send_frame");
        _receive = Get<SendReceive>(symbols, "avcodec_receive_packet");
        _unrefPacket = Get<UnaryVoid>(symbols, "av_packet_unref");
        _channelLayout = Get<ChannelLayout>(symbols, "av_channel_layout_default");
    }

    public byte[] Extradata { get; private set; } = [];
    public int FrameSize { get; private set; }
    public int InitialPadding { get; private set; }

    public static FfmpegEncoderNativeSession Create(VerifiedPlaybackDecoderRuntimeLease runtime,
        H264EncoderConfiguration? video, AacEncoderConfiguration? audio)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("Encoding requires the verified FFmpeg 8.1 Windows x64 ABI.");
        }

        var acquisition = new PlaybackDecoderFfmpegAbiSymbolGate().AcquireForEncoding(runtime);
        var gate = acquisition.Lease ??
            throw new InvalidOperationException($"FFmpeg encoder ABI/symbol gate rejected: {acquisition.Result}.");
        FfmpegEncoderNativeSession? session = null;
        try
        {
            return gate.UseSymbols(symbols =>
            {
                session = new(gate, symbols);
                session.Initialize(symbols, video, audio);
                return session;
            });
        }
        catch
        {
            session?.Dispose();
            gate.Dispose();
            throw;
        }
    }

    private void Initialize(IReadOnlyDictionary<string, nint> symbols,
        H264EncoderConfiguration? video, AacEncoderConfiguration? audio)
    {
        var isVideo = video is not null;
        var find = Get<FindEncoder>(symbols, "avcodec_find_encoder_by_name");
        var codecName = isVideo ? "libopenh264" : "aac";
        var codec = find(codecName);
        if (codec == 0)
        {
            throw new InvalidOperationException($"The verified runtime has no {codecName} software encoder.");
        }

        _context = Get<AllocateContext>(symbols, "avcodec_alloc_context3")(codec);
        if (_context == 0)
        {
            throw new InvalidOperationException("FFmpeg encoder context allocation failed.");
        }

        var allocateParameters = Get<Allocate>(symbols, "avcodec_parameters_alloc");
        var freeParameters = Get<Free>(symbols, "avcodec_parameters_free");
        var parameters = allocateParameters();
        if (parameters == 0)
        {
            throw new InvalidOperationException("FFmpeg encoder parameters allocation failed.");
        }

        try
        {
            // libavcodec 62 codec_par.h offsets, matching the existing decoder ABI.
            Marshal.WriteInt32(parameters, 0, isVideo ? 0 : 1);
            Marshal.WriteInt32(parameters, 4, isVideo ? 27 : 86018);
            Marshal.WriteInt32(parameters, 44, isVideo ? 0 : 8); // YUV420P / FLTP
            Marshal.WriteInt64(parameters, 48, isVideo ? video!.BitRate : audio!.BitRate);
            Marshal.WriteInt32(parameters, 64, isVideo ? 578 : 1); // constrained baseline / AAC-LC
            if (isVideo)
            {
                Marshal.WriteInt32(parameters, 72, video!.Width);
                Marshal.WriteInt32(parameters, 76, video.Height);
                Marshal.WriteInt32(parameters, 88, video.FramesPerSecond);
                Marshal.WriteInt32(parameters, 92, 1);
            }
            else
            {
                _channelLayout(parameters + 128, audio!.ChannelCount);
                Marshal.WriteInt32(parameters, 152, audio.SampleRate);
            }

            Check(Get<SendReceive>(symbols, "avcodec_parameters_to_context")(_context, parameters),
                "encoder parameters");
            var setInteger = Get<SetInteger>(symbols, "av_opt_set_int");
            Check(setInteger(_context, "flags", GlobalHeaderAndFrameDuration, 0), "global-header flags");
            Check(setInteger(_context, "threads", 1, 0), "encoder threads");
            Check(Get<SetRational>(symbols, "av_opt_set_q")(_context, "time_base",
                new(1, isVideo ? video!.FramesPerSecond : audio!.SampleRate), 0), "encoder timebase");
            if (isVideo)
            {
                Check(setInteger(_context, "g", video!.KeyFrameInterval, 0), "keyframe interval");
                Check(setInteger(_context, "bf", 0, 0), "B-frame policy");
                Check(setInteger(_context, "allow_skip_frames", 0, 1), "frame-skip policy");
            }

            Check(Get<OpenCodec>(symbols, "avcodec_open2")(_context, codec, 0), "encoder open");
            Check(Get<SendReceive>(symbols, "avcodec_parameters_from_context")(parameters, _context),
                "encoder output parameters");
            var length = Marshal.ReadInt32(parameters, 24);
            var data = Marshal.ReadIntPtr(parameters, 16);
            if (length is <= 0 or > 1_048_576 || data == 0)
            {
                throw new InvalidOperationException("Encoder produced no bounded codec extradata.");
            }

            Extradata = new byte[length];
            Marshal.Copy(data, Extradata, 0, length);
            FrameSize = isVideo ? 1 : Marshal.ReadInt32(parameters, 160);
            InitialPadding = isVideo ? 0 : Marshal.ReadInt32(parameters, 164);
            if (!isVideo && (FrameSize != 1024 || InitialPadding < 0))
            {
                throw new InvalidOperationException("The AAC encoder frame size/priming is unsupported.");
            }
        }
        finally
        {
            freeParameters(ref parameters);
        }

        _frame = _allocateFrame();
        _packet = _allocatePacket();
        if (_frame == 0 || _packet == 0)
        {
            throw new InvalidOperationException("FFmpeg frame/packet allocation failed.");
        }

        // libavutil 60 frame.h offsets pinned to Windows x64.
        Marshal.WriteInt32(_frame, 116, isVideo ? 0 : 8);
        if (isVideo)
        {
            Marshal.WriteInt32(_frame, 104, video!.Width);
            Marshal.WriteInt32(_frame, 108, video.Height);
        }
        else
        {
            Marshal.WriteInt32(_frame, 112, FrameSize);
            Marshal.WriteInt32(_frame, 180, audio!.SampleRate);
            _channelLayout(_frame + 384, audio.ChannelCount);
        }

        Marshal.WriteInt32(_frame, 152, 1);
        Marshal.WriteInt32(_frame, 156, isVideo ? video!.FramesPerSecond : audio!.SampleRate);
        Check(_getBuffer(_frame, 32), "encoder frame buffer");
    }

    public IReadOnlyList<NativeEncodedPacket> Encode(RawYuv420VideoFrame video) =>
        Guard(() =>
        {
            Check(_makeWritable(_frame), "writable video frame");
            var data = video.Data.ToArray();
            var cursor = 0;
            for (var plane = 0; plane < 3; plane++)
            {
                var width = plane == 0 ? video.Width : video.Width / 2;
                var height = plane == 0 ? video.Height : video.Height / 2;
                var destination = Marshal.ReadIntPtr(_frame, plane * 8);
                var stride = Marshal.ReadInt32(_frame, 64 + plane * 4);
                if (destination == 0 || stride < width)
                {
                    throw new InvalidOperationException("FFmpeg video plane layout is invalid.");
                }

                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(data, cursor, destination + row * stride, width);
                    cursor += width;
                }
            }

            if (_videoDurations.Count >= 512)
            {
                throw new InvalidOperationException("Encoder exceeded the bounded pending-frame limit.");
            }

            _videoDurations.Add(video.PresentationTimestamp, 1);
            SetFrameTiming(video.PresentationTimestamp, 1);
            return Submit(_frame, flushing: false, isVideo: true);
        });

    public IReadOnlyList<NativeEncodedPacket> Encode(RawPcmAudioFrame audio) =>
        Guard(() =>
        {
            Check(_makeWritable(_frame), "writable audio frame");
            var samples = audio.Samples.Span;
            for (var channel = 0; channel < 2; channel++)
            {
                var destination = Marshal.ReadIntPtr(_frame, channel * 8);
                if (destination == 0)
                {
                    throw new InvalidOperationException("FFmpeg audio plane layout is invalid.");
                }

                var plane = new float[audio.SampleCount];
                for (var index = 0; index < plane.Length; index++)
                {
                    plane[index] = samples[index * 2 + channel];
                }

                Marshal.Copy(plane, 0, destination, plane.Length);
            }

            SetFrameTiming(audio.PresentationTimestamp, audio.SampleCount);
            return Submit(_frame, flushing: false, isVideo: false);
        });

    public IReadOnlyList<NativeEncodedPacket> Complete(bool isVideo) =>
        Guard(() =>
        {
            var result = Submit(0, flushing: true, isVideo);
            if (_videoDurations.Count != 0)
            {
                throw new InvalidOperationException("Video EOS lost submitted frames.");
            }

            _completed = true;
            return result;
        });

    private IReadOnlyList<NativeEncodedPacket> Guard(Func<IReadOnlyList<NativeEncodedPacket>> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The native encoder has completed.");
        }

        return _gate.UseSymbols(_ => action());
    }

    private void SetFrameTiming(long pts, long duration)
    {
        Marshal.WriteInt64(_frame, 136, pts);
        Marshal.WriteInt64(_frame, 408, duration);
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<NativeEncodedPacket> Submit(nint frame, bool flushing, bool isVideo)
    {
        var packets = new List<NativeEncodedPacket>();
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var result = _send(_context, frame);
            if (result != Again)
            {
                Check(result, "encoder frame submission");
                Drain(packets, flushing, isVideo);
                return packets.AsReadOnly();
            }

            var before = packets.Count;
            Drain(packets, flushing: false, isVideo);
            if (before == packets.Count)
            {
                throw new InvalidOperationException("Encoder EAGAIN produced no packet before resend.");
            }
        }

        throw new InvalidOperationException("Encoder frame resend limit exceeded.");
    }

    private void Drain(List<NativeEncodedPacket> packets, bool flushing, bool isVideo)
    {
        while (packets.Count < 4096)
        {
            var result = _receive(_context, _packet);
            if (result == EndOfFile && flushing)
            {
                return;
            }

            if (result == Again && !flushing)
            {
                return;
            }

            Check(result, "encoder packet receive/drain");
            try
            {
                // libavcodec 62 packet.h; identical to Nova's decoder packet ABI.
                var pts = Marshal.ReadInt64(_packet, 8);
                var dts = Marshal.ReadInt64(_packet, 16);
                var data = Marshal.ReadIntPtr(_packet, 24);
                var size = Marshal.ReadInt32(_packet, 32);
                var duration = Marshal.ReadInt64(_packet, 64);
                var key = (Marshal.ReadInt32(_packet, 40) & 1) != 0;
                if (pts == long.MinValue || dts == long.MinValue ||
                    size is <= 0 or > 4 * 1024 * 1024 || data == 0)
                {
                    throw new InvalidOperationException("Encoder returned invalid packet metadata.");
                }

                if (isVideo)
                {
                    if (!_videoDurations.Remove(pts, out var submittedDuration) ||
                        (duration != 0 && duration != submittedDuration))
                    {
                        throw new InvalidOperationException("Video packet does not match submitted frame timing.");
                    }

                    // Some encoders omit packet duration; the matched raw frame is authoritative.
                    duration = submittedDuration;
                }

                if (duration <= 0 || (_lastDts is { } previous &&
                        dts != checked(previous + _lastDuration)))
                {
                    throw new InvalidOperationException("Encoder packet DTS/duration is not contiguous.");
                }

                var payload = new byte[size];
                Marshal.Copy(data, payload, 0, size);
                packets.Add(new(dts, pts, duration, key, payload));
                _lastDts = dts;
                _lastDuration = duration;
            }
            finally
            {
                _unrefPacket(_packet);
            }
        }

        throw new InvalidOperationException("Encoder packet drain limit exceeded.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_packet != 0)
            {
                _freePacket(ref _packet);
            }

            if (_frame != 0)
            {
                _freeFrame(ref _frame);
            }

            if (_context != 0)
            {
                _freeContext(ref _context);
            }
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private static T Get<T>(IReadOnlyDictionary<string, nint> symbols, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(symbols[name]);

    private static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"FFmpeg {operation} failed: {result}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rational(int numerator, int denominator)
    {
        public readonly int Numerator = numerator;
        public readonly int Denominator = denominator;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Allocate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint AllocateContext(nint codec);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint FindEncoder([MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(ref nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void UnaryVoid(nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UnaryResult(nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SendReceive(nint context, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OpenCodec(nint context, nint codec, nint options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FrameBuffer(nint frame, int alignment);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ChannelLayout(nint layout, int channels);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetInteger(nint context, [MarshalAs(UnmanagedType.LPStr)] string name, long value, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetRational(nint context, [MarshalAs(UnmanagedType.LPStr)] string name, Rational value, int flags);
}

internal readonly record struct NativeEncodedPacket(
    long Dts, long Pts, long Duration, bool IsKeyFrame, byte[] Payload);
