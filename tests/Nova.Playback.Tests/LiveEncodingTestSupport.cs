using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.MediaEncoding;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Playback.Tests;

internal sealed class ImmediateLiveClock : ILiveMediaClock
{
    public List<long> Targets { get; } = [];
    public void Start() { }
    public ValueTask WaitUntilAsync(long mediaTicks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Targets.Add(mediaTicks);
        return ValueTask.CompletedTask;
    }
}

internal sealed class CapturingLiveEncoder(ILiveEncodingSession inner) : ILiveEncodingSession
{
    public Dictionary<(uint Track, long Dts), Fmp4MuxEncodedSample> Expected { get; } = [];
    public int InitialPaddingSamples => inner.InitialPaddingSamples;
    public IReadOnlyList<Fmp4MuxTrackDescription> Tracks => inner.Tracks;
    public MediaEncoderSnapshot VideoSnapshot => inner.VideoSnapshot;
    public MediaEncoderSnapshot AudioSnapshot => inner.AudioSnapshot;
    public IReadOnlyList<Fmp4MuxEncodedSample> Encode(LiveMediaInput input) => Capture(inner.Encode(input));
    public IReadOnlyList<Fmp4MuxEncodedSample> Complete() => Capture(inner.Complete());
    private IReadOnlyList<Fmp4MuxEncodedSample> Capture(IReadOnlyList<Fmp4MuxEncodedSample> samples)
    {
        foreach (var sample in samples)
        {
            Expected.Add((sample.TrackId, sample.DtsTicks), sample with { Payload = sample.Payload.ToArray() });
        }
        return samples;
    }
    public void Dispose() => inner.Dispose();
}

// Only managed unit tests use this synthetic codec configuration and packets.
internal sealed class FakeLiveEncoder : ILiveEncodingSession
{
    private int _video, _audio, _packets;
    private readonly List<byte[]> _borrowed = [];
    public bool Completed { get; private set; }
    public bool Disposed { get; private set; }
    public bool MutatePreviousBuffers { get; init; }
    public bool InvalidRandomAccess { get; init; }
    public string? BadTimeline { get; init; }
    public Exception? EncodeFailure { get; init; }
    public int InitialPaddingSamples => 1024;
    public IReadOnlyList<Fmp4MuxTrackDescription> Tracks { get; } = [
        new(1, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1, 48_000,
            new byte[] { 1, 66, 0, 30, 0xff, 0xe1, 0, 2, 0x67, 0x42, 1, 0, 1, 0x68 }, width: 64, height: 48),
        new(2, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc, 48_000,
            new byte[] { 0x11, 0x90 }, sampleRate: 48_000, channelCount: 2),
    ];
    public MediaEncoderSnapshot VideoSnapshot => new(_video, _video, 0, Completed);
    public MediaEncoderSnapshot AudioSnapshot => new(_audio, _packets, Completed ? 2 : 0, Completed);
    public IReadOnlyList<Fmp4MuxEncodedSample> Encode(LiveMediaInput input)
    {
        if (EncodeFailure is { } failure) { throw failure; }
        if (MutatePreviousBuffers) { foreach (var bytes in _borrowed) { Array.Fill(bytes, (byte)0xee); } }
        _borrowed.Clear();
        if (input.Video is { } video)
        {
            Assert.Equal(_video, video.PresentationTimestamp);
            var index = _video++;
            var bytes = new byte[] { 0, 0, 0, 2, (byte)(index % 24 == 0 ? 0x65 : 0x41), (byte)index };
            _borrowed.Add(bytes);
            return [new(1, index == 1 && BadTimeline == "DTS" ? 1024 : index * 1600L + 1024,
                index == 1 && BadTimeline == "PTS" ? 1024 : index * 1600L + 1024, 1600, bytes,
                InvalidRandomAccess ? Fmp4MuxRandomAccessKind.Indeterminate :
                index % 24 == 0 ? Fmp4MuxRandomAccessKind.IndependentlyDecodable :
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable)];
        }
        Assert.Equal(_audio * 1024L, input.Audio!.PresentationTimestamp);
        _audio++;
        return _audio < 2 ? [] : [AudioPacket()];
    }
    private Fmp4MuxEncodedSample AudioPacket()
    {
        var index = _packets++;
        var bytes = new byte[] { 0x21, (byte)index, (byte)(index >> 8) };
        _borrowed.Add(bytes);
        return new(2, index * 1024L, index * 1024L, 1024, bytes, Fmp4MuxRandomAccessKind.NotApplicable);
    }
    public IReadOnlyList<Fmp4MuxEncodedSample> Complete()
    {
        Completed = true;
        return [AudioPacket(), AudioPacket()];
    }
    public void Dispose() => Disposed = true;
}

internal sealed class LiveDecodedOutput(
    IPlaybackPcpFmp4PresentationOutputGenerationFactory? downstreamFactory = null) : IPlaybackPcpFmp4PresentationOutputGeneration,
    IPlaybackPcpFmp4PresentationOutputGenerationFactory
{
    private IPlaybackPcpFmp4PresentationOutputGeneration? _downstream;
    public int Generations { get; private set; }
    public int Completions { get; private set; }
    public bool Disposed { get; private set; }
    public int VideoFrames { get; private set; }
    public int AudioFrames { get; private set; }
    public long AudioSamples { get; private set; }
    public float MaximumAmplitude { get; private set; }
    public HashSet<string> VideoHashes { get; } = [];
    public IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation)
    {
        Assert.Equal(0, Generations++);
        _downstream = downstreamFactory?.Create(generation);
        return this;
    }
    public async ValueTask WriteVideoAsync(PlaybackDecodedVideoFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(64, frame.Width); Assert.Equal(48, frame.Height);
        VideoFrames++;
        VideoHashes.Add(Convert.ToHexString(SHA256.HashData(frame.Payload.Span)));
        if (_downstream is not null) { await _downstream.WriteVideoAsync(frame, cancellationToken); }
    }
    public async ValueTask WriteAudioAsync(PlaybackDecodedAudioFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(48_000, frame.SampleRate); Assert.Equal(2, frame.ChannelCount);
        Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, frame.SampleFormat);
        AudioFrames++; AudioSamples += frame.SampleCount;
        foreach (var sample in MemoryMarshal.Cast<byte, float>(frame.Payload.Span))
        {
            Assert.True(float.IsFinite(sample));
            MaximumAmplitude = Math.Max(MaximumAmplitude, Math.Abs(sample));
        }
        if (_downstream is not null) { await _downstream.WriteAudioAsync(frame, cancellationToken); }
    }
    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_downstream is not null) { await _downstream.CompleteAsync(cancellationToken); }
        Completions++;
    }
    public async ValueTask DisposeAsync()
    {
        if (Disposed) { return; }
        Disposed = true;
        if (_downstream is not null) { await _downstream.DisposeAsync(); }
    }
}

internal sealed class LiveIdentityDecoderFactory(CapturingLiveEncoder encoder,
    IPlaybackDecoderFactory? nativeFactory = null) : IPlaybackDecoderFactory
{
    public HashSet<(uint Track, long Dts)> Received { get; } = [];
    public List<uint> FragmentSequences { get; } = [];
    public int Completions { get; private set; }
    public bool Disposed { get; private set; }
    public IPlaybackDecoder Create(PlaybackDecoderCreationContext context)
    {
        var stream = Assert.IsType<PlaybackEncodedStreamDescription>(context.EncodedStreamDescription);
        Assert.Equal(2, stream.TrackDescriptions.Count);
        var inner = nativeFactory?.Create(context);
        if (nativeFactory is not null) { Assert.NotNull(inner); }
        return new Decoder(this, inner);
    }
    private sealed class Decoder(LiveIdentityDecoderFactory owner, IPlaybackDecoder? inner) : IPlaybackDecoder
    {
        public async ValueTask DecodeAsync(PlaybackEncodedInput input, CancellationToken cancellationToken)
        {
            var metadata = Assert.IsType<PlaybackFmp4SampleMetadata>(input.Fmp4SampleMetadata);
            var key = (metadata.TrackId, metadata.DtsTicks);
            Assert.True(owner.Received.Add(key));
            var expected = owner.Expected(key);
            Assert.Equal(expected.PtsTicks, metadata.PtsTicks);
            Assert.Equal(expected.DurationTicks, metadata.DurationTicks);
            Assert.Equal(expected.Payload.ToArray(), input.Payload.ToArray());
            var access = expected.RandomAccessKind switch
            {
                Fmp4MuxRandomAccessKind.IndependentlyDecodable => PlaybackEncodedRandomAccessClassification.IndependentlyDecodable,
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable => PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable,
                _ => PlaybackEncodedRandomAccessClassification.NotApplicable,
            };
            Assert.Equal(access, metadata.RandomAccessClassification);
            Assert.Equal(new PlaybackEncodedTimeBase(1, 48_000), metadata.TimeBase);
            if (owner.FragmentSequences.Count == 0 || owner.FragmentSequences[^1] != metadata.FragmentSequence)
            { owner.FragmentSequences.Add(metadata.FragmentSequence); }
            if (inner is not null) { await inner.DecodeAsync(input, cancellationToken); }
        }
        public ValueTask ResetAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected live reset.");
        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        { if (inner is not null) { await inner.CompleteAsync(cancellationToken); } owner.Completions++; }
        public async ValueTask DisposeAsync()
        { if (inner is not null) { await inner.DisposeAsync(); } owner.Disposed = true; }
    }
    private Fmp4MuxEncodedSample Expected((uint Track, long Dts) key) => encoder.Expected[key];
}

// Bounded, post-handshake PCP wire loopback. Each publisher callback waits for
// the production reader AND composition callback, not merely Pipe admission.
internal sealed class LivePcpWireLoopback : IPcpChannelPacketConsumer, IAsyncDisposable
{
    private readonly Pipe _pipe = new(new PipeOptions(pauseWriterThreshold: 64 * 1024,
        resumeWriterThreshold: 32 * 1024, useSynchronizationContext: false));
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<PcpChannelStreamResult> _reader;
    private TaskCompletionSource? _ack;
    private PcpChannelPacket? _sent;
    private int _disposed;
    public ChannelId Channel { get; } = new(Guid.NewGuid());
    public ChannelSessionId Session { get; } = new(Guid.NewGuid());
    public PlaybackPcpFmp4CompositionBoundary Composition { get; }
    public int SentPackets { get; private set; }
    public LivePcpWireLoopback(IPlaybackDecoderFactory decoder,
        IPlaybackPcpFmp4PresentationOutputGenerationFactory output)
    {
        Composition = new(Channel, Session, decoder, output,
            () => new(contentType: "MP4", streamType: "fMP4", streamExtension: "mp4"),
            new() { MaximumSegmentBytes = 2 * 1024 * 1024 });
        var tracker = new ChannelStatusTracker(Channel, Session);
        tracker.TransitionTo(ChannelSessionState.Connecting);
        tracker.TransitionTo(ChannelSessionState.Negotiating);
        tracker.TransitionTo(ChannelSessionState.Receiving);
        var reader = new PcpChannelStreamReader(_pipe.Reader, new PcpChannelStateAdapter(tracker),
            new Receiver(this), new() { MaxAtomContentBytes = 128 * 1024, MaxPacketBytes = 64 * 1024 });
        _reader = reader.ReadToEndAsync(_stop.Token);
    }
    public async ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
    {
        _sent = packet;
        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ack = ack;
        var atom = PcpAtom.Parent(PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(Channel.Value)),
            PcpAtom.Parent(PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(PcpKnownAtomNames.Type, PcpWireValueCodec.EncodeUInt32(packet.Type)),
                PcpAtom.Leaf(PcpKnownAtomNames.Position, PcpWireValueCodec.EncodeUInt32(packet.WirePosition)),
                PcpAtom.Leaf(PcpKnownAtomNames.Data, packet.Payload.Span)));
        await WriteAsync(atom, cancellationToken);
        // Propagate a reader fault even if it occurs before the packet callback.
        var terminal = await Task.WhenAny(ack.Task, _reader).WaitAsync(cancellationToken);
        if (terminal == _reader) { await _reader; throw new InvalidOperationException("PCP reader ended before acknowledgement."); }
        await ack.Task.WaitAsync(cancellationToken);
        SentPackets++;
    }
    public async Task<PcpChannelStreamResult> CompleteAsync(CancellationToken cancellationToken)
    {
        await WriteAsync(PcpAtom.Leaf(PcpKnownAtomNames.Quit, PcpWireValueCodec.EncodeUInt32(0)), cancellationToken);
        await _pipe.Writer.CompleteAsync();
        var result = await _reader.WaitAsync(cancellationToken);
        Assert.Equal(PcpChannelStreamEndReason.QuitReceived, result.EndReason);
        await Composition.CompleteInputAsync(cancellationToken);
        Assert.Equal(PlaybackPcpFmp4CompositionResult.Completed, Composition.Snapshot.Result);
        return result;
    }
    private async ValueTask WriteAsync(PcpAtom atom, CancellationToken cancellationToken)
    {
        var bytes = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(bytes, atom);
        var result = await _pipe.Writer.WriteAsync(bytes.WrittenMemory, cancellationToken);
        Assert.False(result.IsCanceled); Assert.False(result.IsCompleted);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        _stop.Cancel();
        await _pipe.Writer.CompleteAsync();
        try { await _reader; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { await Composition.DisposeAsync(); _stop.Dispose(); }
    }
    private sealed class Receiver(LivePcpWireLoopback owner) : IPcpChannelPacketConsumer
    {
        public async ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
        {
            var ack = owner._ack!;
            try
            {
                var sent = owner._sent!;
                Assert.Equal(sent.Sequence, packet.Sequence); Assert.Equal(sent.Type, packet.Type);
                Assert.Equal(sent.ChannelId, packet.ChannelId); Assert.Equal(sent.SessionId, packet.SessionId);
                Assert.Equal(sent.WirePosition, packet.WirePosition);
                Assert.Equal(sent.Payload.ToArray(), packet.Payload.ToArray());
                Assert.Null(packet.Discontinuity);
                await owner.Composition.ConsumeAsync(packet, cancellationToken);
                Assert.Equal(PlaybackPcpFmp4CompositionResult.PacketAccepted, owner.Composition.Snapshot.Result);
                ack.TrySetResult();
            }
            catch (Exception exception) { ack.TrySetException(exception); throw; }
        }
    }
}
