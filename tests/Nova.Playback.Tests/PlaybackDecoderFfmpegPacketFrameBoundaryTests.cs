namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegPacketFrameBoundaryTests
{
    [Fact]
    public void SendPacket_OpenedContextSendsBoundedPacket()
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi();
        var boundary = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true);

        var result = boundary.SendPacket(owners.Context, new byte[] { 1, 2, 3, 4 });

        Assert.Equal(PlaybackDecoderFfmpegPacketSendResult.Sent, result);
        Assert.Equal(1, native.SendCount);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, native.Packet);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void SendPacket_UnopenedContextRejectsBeforeNativeAccess()
    {
        using var owners = new TestOwners(opened: false);
        var native = new FakeNativeApi();

        var result = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true)
            .SendPacket(owners.Context, new byte[] { 1 });

        Assert.Equal(PlaybackDecoderFfmpegPacketSendResult.ContextNotOpened, result);
        Assert.Equal(0, native.SendCount);
    }

    [Theory]
    [InlineData(1, PlaybackDecoderFfmpegPacketSendResult.NeedFrameDrain)]
    [InlineData(2, PlaybackDecoderFfmpegPacketSendResult.EndOfStream)]
    [InlineData(3, PlaybackDecoderFfmpegPacketSendResult.NativeAllocationFailed)]
    [InlineData(4, PlaybackDecoderFfmpegPacketSendResult.NativeRejected)]
    public void SendPacket_MapsBoundedNativeResults(
        int nativeResultValue,
        PlaybackDecoderFfmpegPacketSendResult expected)
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi
        {
            SendResult = (PlaybackDecoderFfmpegNativeIoResult)nativeResultValue,
        };

        var result = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true)
            .SendPacket(owners.Context, new byte[] { 7 });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void SendPacket_EmptyAndOversizedPacketsFailBeforeNativeAccess()
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi();
        var boundary = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketSendResult.EmptyPacket,
            boundary.SendPacket(owners.Context, ReadOnlyMemory<byte>.Empty));
        Assert.Equal(
            PlaybackDecoderFfmpegPacketSendResult.PacketTooLarge,
            boundary.SendPacket(
                owners.Context,
                new byte[PlaybackDecoderFfmpegPacketFrameBoundary.MaximumPacketBytes + 1]));
        Assert.Equal(0, native.SendCount);
    }

    [Fact]
    public void ReceiveFrame_OpenedContextReturnsFrameAvailable()
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi();

        var result = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true)
            .ReceiveFrame(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable, result);
        Assert.Equal(1, native.ReceiveCount);
    }

    [Theory]
    [InlineData(1, PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput)]
    [InlineData(2, PlaybackDecoderFfmpegFrameReceiveResult.EndOfStream)]
    [InlineData(3, PlaybackDecoderFfmpegFrameReceiveResult.NativeAllocationFailed)]
    [InlineData(4, PlaybackDecoderFfmpegFrameReceiveResult.NativeRejected)]
    public void ReceiveFrame_MapsBoundedNativeResults(
        int nativeResultValue,
        PlaybackDecoderFfmpegFrameReceiveResult expected)
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi
        {
            ReceiveResult = (PlaybackDecoderFfmpegNativeIoResult)nativeResultValue,
        };

        var result = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true)
            .ReceiveFrame(owners.Context);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MissingSymbolsFailClosedBeforeNativeAccess()
    {
        using var owners = new TestOwners(opened: true, omitSymbol: "av_frame_alloc");
        var native = new FakeNativeApi();

        var result = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true)
            .ReceiveFrame(owners.Context);

        Assert.Equal(PlaybackDecoderFfmpegFrameReceiveResult.GateRejected, result);
        Assert.Equal(0, native.ReceiveCount);
    }

    [Fact]
    public void NativeExceptionDisposesContextWithoutLeakingText()
    {
        using var owners = new TestOwners(opened: true);
        var native = new FakeNativeApi { ThrowOnSend = true };

        var result = new PlaybackDecoderFfmpegPacketFrameBoundary(native, true)
            .SendPacket(owners.Context, new byte[] { 0x53, 0x45, 0x43, 0x52, 0x45, 0x54 });

        Assert.Equal(PlaybackDecoderFfmpegPacketSendResult.NativeFailure, result);
        Assert.True(owners.Context.IsDisposed);
        Assert.DoesNotContain("SECRET", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeNativeApi : IPlaybackDecoderFfmpegPacketFrameNativeApi
    {
        public PlaybackDecoderFfmpegNativeIoResult SendResult { get; init; } =
            PlaybackDecoderFfmpegNativeIoResult.Success;
        public PlaybackDecoderFfmpegNativeIoResult ReceiveResult { get; init; } =
            PlaybackDecoderFfmpegNativeIoResult.Success;
        public bool ThrowOnSend { get; init; }
        public int SendCount { get; private set; }
        public int ReceiveCount { get; private set; }
        public byte[] Packet { get; private set; } = [];

        public PlaybackDecoderFfmpegNativeIoResult SendPacket(
            nint packetAllocFunction,
            nint packetFreeFunction,
            nint memoryAllocFunction,
            nint memoryFreeFunction,
            nint packetFromDataFunction,
            nint sendPacketFunction,
            nint context,
            ReadOnlyMemory<byte> packet)
        {
            SendCount++;
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("sensitive packet failure");
            }

            Assert.All(
                new[]
                {
                    packetAllocFunction,
                    packetFreeFunction,
                    memoryAllocFunction,
                    memoryFreeFunction,
                    packetFromDataFunction,
                    sendPacketFunction,
                    context,
                },
                value => Assert.NotEqual(0, value));
            Packet = packet.ToArray();
            return SendResult;
        }

        public PlaybackDecoderFfmpegNativeIoResult ReceiveFrame(
            nint frameAllocFunction,
            nint frameFreeFunction,
            nint receiveFrameFunction,
            nint context)
        {
            ReceiveCount++;
            Assert.All(
                new[] { frameAllocFunction, frameFreeFunction, receiveFrameFunction, context },
                value => Assert.NotEqual(0, value));
            return ReceiveResult;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners(bool opened, string? omitSymbol = null)
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "runtime.bin");
            File.WriteAllBytes(path, [1]);
            _runtime = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = new(path, FileMode.Open, FileAccess.Read, FileShare.Read),
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = path,
                },
                []);

            var names = new[]
            {
                "av_packet_alloc", "av_packet_free", "av_malloc", "av_free",
                "av_packet_from_data", "avcodec_send_packet", "av_frame_alloc",
                "av_frame_free", "avcodec_receive_frame",
            };
            var symbols = names
                .Where(name => !string.Equals(name, omitSymbol, StringComparison.Ordinal))
                .Select((name, index) => (name, value: (nint)(index + 1)))
                .ToDictionary(pair => pair.name, pair => pair.value, StringComparer.Ordinal);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)100],
                symbols);
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                new FakeContextNativeApi(),
                PlaybackDecoderFfmpegCodecId.H264,
                (nint)200,
                (nint)300);
            if (opened)
            {
                Context.MarkParametersTransferred();
                Context.MarkOpened();
            }
        }

        public PlaybackDecoderFfmpegContextLease Context { get; }

        public void Dispose()
        {
            Context.Dispose();
            _runtime.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) =>
            throw new NotSupportedException();
        public nint GetSymbol(nint module, string symbol) => throw new NotSupportedException();
        public uint InvokeVersion(nint symbol) => throw new NotSupportedException();
        public void Free(nint module)
        {
        }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) => 0;
        public nint AllocateContext(nint function, nint decoder) => 0;
        public void FreeContext(nint function, ref nint context) => context = 0;
    }
}
