namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegParameterTransferTests
{
    [Fact]
    public void Apply_H264TransfersOwnedParametersAndConsumesManagedLease()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi();

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(PlaybackDecoderFfmpegParameterTransferResult.Applied, result);
        Assert.Equal(1, native.CallCount);
        Assert.Equal((nint)900, native.Context);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.H264, native.CodecId);
        Assert.Equal(1920, native.Width);
        Assert.Equal(1080, native.Height);
        Assert.Equal(0, native.SampleRate);
        Assert.Equal(0, native.ChannelCount);
        Assert.Equal(new byte[] { 1, 100, 0, 31 }, native.Extradata);
        Assert.True(parameters.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void Apply_AacTransfersOwnedParametersAndConsumesManagedLease()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.Aac);
        var parameters = CreateAacParameters();
        var native = new FakeTransferNativeApi();

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(PlaybackDecoderFfmpegParameterTransferResult.Applied, result);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, native.CodecId);
        Assert.Equal(48_000, native.SampleRate);
        Assert.Equal(2, native.ChannelCount);
        Assert.Equal(new byte[] { 0x12, 0x10 }, native.Extradata);
        Assert.True(parameters.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void Apply_CodecMismatchFailsBeforeNativeAccessAndRetainsBothOwners()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        using var parameters = CreateAacParameters();
        var native = new FakeTransferNativeApi();

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(PlaybackDecoderFfmpegParameterTransferResult.CodecMismatch, result);
        Assert.Equal(0, native.CallCount);
        Assert.False(owners.Context.IsDisposed);
        Assert.False(parameters.IsDisposed);
    }

    [Fact]
    public void Apply_UnsupportedFailsBeforeNativeAccessAndRetainsBothOwners()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        using var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi();

        var result = new PlaybackDecoderFfmpegParameterTransfer(
            native,
            isSupportedPlatform: false).Apply(owners.Context, parameters);

        Assert.Equal(PlaybackDecoderFfmpegParameterTransferResult.Unsupported, result);
        Assert.Equal(0, native.CallCount);
        Assert.False(owners.Context.IsDisposed);
        Assert.False(parameters.IsDisposed);
    }

    [Fact]
    public void Apply_DisposedContextIsRejectedBeforeNativeAccess()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        using var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi();
        owners.Context.Dispose();

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(
            PlaybackDecoderFfmpegParameterTransferResult.ContextUnavailable,
            result);
        Assert.Equal(0, native.CallCount);
        Assert.False(parameters.IsDisposed);
    }

    [Fact]
    public void Apply_DisposedParametersAreRejectedBeforeNativeAccess()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi();
        parameters.Dispose();

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(
            PlaybackDecoderFfmpegParameterTransferResult.ParametersUnavailable,
            result);
        Assert.Equal(0, native.CallCount);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public void Apply_MissingSymbolFailsClosedWithoutNativeMutation()
    {
        using var owners = new TestOwners(
            PlaybackDecoderFfmpegCodecId.H264,
            missingSymbol: "avcodec_parameters_to_context");
        using var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi();

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(PlaybackDecoderFfmpegParameterTransferResult.GateRejected, result);
        Assert.Equal(0, native.CallCount);
        Assert.False(owners.Context.IsDisposed);
        Assert.False(parameters.IsDisposed);
    }

    [Fact]
    public void Apply_NativeAllocationFailureRetainsRetryableOwners()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        using var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi
        {
            Result = PlaybackDecoderFfmpegNativeParameterTransferResult.AllocationFailed,
        };

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(
            PlaybackDecoderFfmpegParameterTransferResult.NativeAllocationFailed,
            result);
        Assert.False(owners.Context.IsDisposed);
        Assert.False(parameters.IsDisposed);
    }

    [Fact]
    public void Apply_NativeTransferRejectionDiscardsContextButRetainsParameters()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.H264);
        using var parameters = CreateH264Parameters();
        var native = new FakeTransferNativeApi
        {
            Result = PlaybackDecoderFfmpegNativeParameterTransferResult.TransferRejected,
        };

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(
            PlaybackDecoderFfmpegParameterTransferResult.NativeTransferRejected,
            result);
        Assert.True(owners.Context.IsDisposed);
        Assert.False(parameters.IsDisposed);
        Assert.Equal(1, owners.ContextNative.FreeCount);
    }

    [Fact]
    public void Apply_NativeExceptionDiscardsContextButRetainsParameters()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.Aac);
        using var parameters = CreateAacParameters();
        var native = new FakeTransferNativeApi { ThrowOnTransfer = true };

        var result = CreateTransfer(native).Apply(owners.Context, parameters);

        Assert.Equal(PlaybackDecoderFfmpegParameterTransferResult.NativeFailure, result);
        Assert.True(owners.Context.IsDisposed);
        Assert.False(parameters.IsDisposed);
        Assert.Equal(1, owners.ContextNative.FreeCount);
    }

    [Fact]
    public void Apply_ResultAndFailuresExposeNoExtradataOrNativeValues()
    {
        using var owners = new TestOwners(PlaybackDecoderFfmpegCodecId.Aac);
        using var parameters = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                48_000,
                2,
                new byte[] { 0x53, 0x45, 0x43, 0x52, 0x45, 0x54 }))
            .Lease!;
        var native = new FakeTransferNativeApi
        {
            Result = PlaybackDecoderFfmpegNativeParameterTransferResult.TransferRejected,
        };

        var result = CreateTransfer(native).Apply(owners.Context, parameters);
        var diagnostic = result.ToString();

        Assert.DoesNotContain("SECRET", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("900", diagnostic, StringComparison.Ordinal);
        Assert.Empty(result.GetType().GetProperties());
    }

    private static PlaybackDecoderFfmpegParameterTransfer CreateTransfer(
        FakeTransferNativeApi native) =>
        new(native, isSupportedPlatform: true);

    private static PlaybackDecoderFfmpegCodecParametersLease CreateH264Parameters() =>
        PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                1920,
                1080,
                new byte[] { 1, 100, 0, 31 }))
            .Lease!;

    private static PlaybackDecoderFfmpegCodecParametersLease CreateAacParameters() =>
        PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                48_000,
                2,
                new byte[] { 0x12, 0x10 }))
            .Lease!;

    private sealed class FakeTransferNativeApi :
        IPlaybackDecoderFfmpegParameterTransferNativeApi
    {
        public PlaybackDecoderFfmpegNativeParameterTransferResult Result { get; init; } =
            PlaybackDecoderFfmpegNativeParameterTransferResult.Applied;

        public bool ThrowOnTransfer { get; init; }

        public int CallCount { get; private set; }

        public nint Context { get; private set; }

        public PlaybackDecoderFfmpegCodecId CodecId { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public int SampleRate { get; private set; }

        public int ChannelCount { get; private set; }

        public byte[] Extradata { get; private set; } = [];

        public PlaybackDecoderFfmpegNativeParameterTransferResult Transfer(
            nint allocateParametersFunction,
            nint freeParametersFunction,
            nint allocateZeroedFunction,
            nint initializeChannelLayoutFunction,
            nint transferParametersFunction,
            nint context,
            PlaybackDecoderFfmpegOwnedCodecParameters parameters)
        {
            CallCount++;
            if (ThrowOnTransfer)
            {
                throw new InvalidOperationException("sensitive native transfer failure");
            }

            Assert.NotEqual(0, allocateParametersFunction);
            Assert.NotEqual(0, freeParametersFunction);
            Assert.NotEqual(0, allocateZeroedFunction);
            Assert.NotEqual(0, initializeChannelLayoutFunction);
            Assert.NotEqual(0, transferParametersFunction);
            Context = context;
            CodecId = parameters.CodecId;
            Width = parameters.Width;
            Height = parameters.Height;
            SampleRate = parameters.SampleRate;
            ChannelCount = parameters.ChannelCount;
            Extradata = parameters.Extradata.ToArray();
            Assert.Equal(
                PlaybackDecoderFfmpegCodecParametersFactory.FfmpegInputBufferPaddingSize,
                parameters.Padding.Length);
            Assert.All(parameters.Padding.ToArray(), static value => Assert.Equal((byte)0, value));
            return Result;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners(
            PlaybackDecoderFfmpegCodecId codecId,
            string? missingSymbol = null)
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "runtime.bin");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _runtime = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = stream,
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = path,
                },
                []);

            var symbols = new Dictionary<string, nint>(StringComparer.Ordinal)
            {
                ["avcodec_parameters_alloc"] = (nint)1,
                ["avcodec_parameters_free"] = (nint)2,
                ["av_mallocz"] = (nint)3,
                ["av_channel_layout_default"] = (nint)4,
                ["avcodec_parameters_to_context"] = (nint)5,
            };
            if (missingSymbol is not null)
            {
                symbols.Remove(missingSymbol);
            }

            var loader = new FakeLoader();
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                loader,
                [(nint)10],
                symbols);
            ContextNative = new FakeContextNativeApi();
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                ContextNative,
                codecId,
                freeContext: (nint)6,
                context: (nint)900);
        }

        public PlaybackDecoderFfmpegContextLease Context { get; }

        public FakeContextNativeApi ContextNative { get; }

        public void Dispose()
        {
            Context.Dispose();
            _runtime.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) =>
            throw new NotSupportedException();

        public nint GetSymbol(nint module, string symbol) =>
            throw new NotSupportedException();

        public uint InvokeVersion(nint symbol) => throw new NotSupportedException();

        public void Free(nint module)
        {
        }
    }

    public sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public int FreeCount { get; private set; }

        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) =>
            throw new NotSupportedException();

        public nint AllocateContext(nint function, nint decoder) =>
            throw new NotSupportedException();

        public void FreeContext(nint function, ref nint context)
        {
            FreeCount++;
            context = 0;
        }
    }
}
