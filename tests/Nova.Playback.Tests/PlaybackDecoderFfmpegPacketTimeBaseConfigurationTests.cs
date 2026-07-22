using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegPacketTimeBaseConfigurationTests
{
    private static readonly PlaybackBoundedDemuxTimeBase TimeBase = new(1, 1_000);

    [Fact]
    public void Configure_TransferredContextUsesExactPublicOptionContract()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi();

        var result = CreateBoundary(native).Configure(owners.Context, TimeBase);

        Assert.Equal(PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured, result);
        Assert.Equal(1, native.CallCount);
        Assert.Equal((nint)42, native.Function);
        Assert.Equal((nint)900, native.Context);
        Assert.Equal("pkt_timebase", native.OptionName);
        Assert.Equal(new PlaybackDecoderFfmpegAvRational(1, 1_000), native.Value);
        Assert.Equal(0, native.Flags);
        Assert.Equal(TimeBase, owners.Context.ConfiguredPacketTimeBase);
        Assert.False(owners.Context.IsOpened);
    }

    [Fact]
    public void AvRational_HasSequentialTwoIntLayout()
    {
        Assert.Equal(8, Marshal.SizeOf<PlaybackDecoderFfmpegAvRational>());
        Assert.Equal(0, Marshal.OffsetOf<PlaybackDecoderFfmpegAvRational>("<Numerator>k__BackingField").ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<PlaybackDecoderFfmpegAvRational>("<Denominator>k__BackingField").ToInt32());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1_000_000_001, 1)]
    [InlineData(1, 1_000_000_001)]
    public void Configure_InvalidTimeBaseRejectsBeforeNativeAccess(int numerator, int denominator)
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi();

        var result = CreateBoundary(native).Configure(
            owners.Context,
            new(numerator, denominator));

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.InvalidTimeBase,
            result);
        Assert.Equal(0, native.CallCount);
        Assert.Null(owners.Context.ConfiguredPacketTimeBase);
    }

    [Fact]
    public void Configure_RequiresTransferredParameters()
    {
        using var owners = new TestOwners();
        var native = new FakeNativeApi();

        var result = CreateBoundary(native).Configure(owners.Context, TimeBase);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.ParametersNotTransferred,
            result);
        Assert.Equal(0, native.CallCount);
    }

    [Fact]
    public void Configure_DuplicateConfigurationIsRejectedBeforeNativeAccess()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi();
        var boundary = CreateBoundary(native);
        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured,
            boundary.Configure(owners.Context, TimeBase));

        var result = boundary.Configure(owners.Context, TimeBase);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.AlreadyConfigured,
            result);
        Assert.Equal(1, native.CallCount);
    }

    [Fact]
    public void Configure_OpenedContextIsRejectedBeforeNativeAccess()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        owners.Context.MarkOpened();
        var native = new FakeNativeApi();

        var result = CreateBoundary(native).Configure(owners.Context, TimeBase);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.ContextAlreadyOpened,
            result);
        Assert.Equal(0, native.CallCount);
    }

    [Fact]
    public void Configure_MissingRequiredSymbolIsGateRejected()
    {
        using var owners = new TestOwners(includeSymbol: false);
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi();

        var result = CreateBoundary(native).Configure(owners.Context, TimeBase);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.GateRejected,
            result);
        Assert.Equal(0, native.CallCount);
    }

    [Fact]
    public void Configure_NativeNegativeResultDoesNotRetainTimeBase()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi { ReturnCode = -22 };

        var result = CreateBoundary(native).Configure(owners.Context, TimeBase);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeRejected,
            result);
        Assert.Null(owners.Context.ConfiguredPacketTimeBase);
        Assert.True(owners.Context.IsDisposed);
        Assert.DoesNotContain("22", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_NativeExceptionDisposesContextFailClosed()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi { Throw = true };

        var result = CreateBoundary(native).Configure(owners.Context, TimeBase);

        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeFailure,
            result);
        Assert.True(owners.Context.IsDisposed);
        Assert.Null(owners.Context.ConfiguredPacketTimeBase);
        Assert.DoesNotContain("sensitive", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configure_UnsupportedAndUnavailableNeverCallNative()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        var native = new FakeNativeApi();
        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Unsupported,
            new PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary(native, false)
                .Configure(owners.Context, TimeBase));
        owners.Context.Dispose();
        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.ContextUnavailable,
            CreateBoundary(native).Configure(owners.Context, TimeBase));
        Assert.Equal(0, native.CallCount);
    }

    [Fact]
    public void Dispose_ClearsRetainedPacketTimeBase()
    {
        using var owners = new TestOwners();
        owners.Context.MarkParametersTransferred();
        Assert.Equal(
            PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured,
            CreateBoundary(new FakeNativeApi()).Configure(owners.Context, TimeBase));

        owners.Context.Dispose();

        Assert.Null(owners.Context.ConfiguredPacketTimeBase);
    }

    private static PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary CreateBoundary(
        FakeNativeApi native) => new(native, true);

    private sealed class FakeNativeApi : IPlaybackDecoderFfmpegPacketTimeBaseNativeApi
    {
        public int ReturnCode { get; init; }
        public bool Throw { get; init; }
        public int CallCount { get; private set; }
        public nint Function { get; private set; }
        public nint Context { get; private set; }
        public string? OptionName { get; private set; }
        public PlaybackDecoderFfmpegAvRational Value { get; private set; }
        public int Flags { get; private set; }

        public int SetRational(
            nint function,
            nint context,
            string optionName,
            PlaybackDecoderFfmpegAvRational value,
            int flags)
        {
            CallCount++;
            Function = function;
            Context = context;
            OptionName = optionName;
            Value = value;
            Flags = flags;
            if (Throw)
            {
                throw new InvalidOperationException("sensitive native detail");
            }

            return ReturnCode;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners(bool includeSymbol = true)
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
            var symbols = includeSymbol
                ? new Dictionary<string, nint>(StringComparer.Ordinal)
                {
                    ["av_opt_set_q"] = (nint)42,
                }
                : new Dictionary<string, nint>(StringComparer.Ordinal);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)1],
                symbols);
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                new FakeContextNativeApi(),
                PlaybackDecoderFfmpegCodecId.H264,
                (nint)2,
                (nint)900);
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
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) => 0;
        public nint GetSymbol(nint module, string symbol) => 0;
        public uint InvokeVersion(nint symbol) => 0;
        public void Free(nint module) { }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) => 0;
        public nint AllocateContext(nint function, nint decoder) => 0;
        public void FreeContext(nint function, ref nint context) => context = 0;
    }
}
