namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticNativeParameterTransactionTests
{
    [Fact]
    public void ExecuteCore_RejectsSharedParentAndDisposesParameters()
    {
        var operations = new CountingOperations();
        var parameters = CreateParameters();
        var parent = new object();

        var result = new PlaybackDiagnosticNativeParameterTransaction(operations)
            .ExecuteCore(parent, parent, parameters);

        Assert.Equal(
            PlaybackDiagnosticNativeParameterTransactionResult.InvalidConfiguration,
            result.Result);
        Assert.Null(result.Lease);
        Assert.True(parameters.IsDisposed);
        Assert.Equal(0, operations.CreateCount);
    }

    [Fact]
    public void ExecuteCore_VideoContextFailureDisposesParameters()
    {
        var operations = new CountingOperations();
        var parameters = CreateParameters();

        var result = new PlaybackDiagnosticNativeParameterTransaction(operations)
            .ExecuteCore(new object(), new object(), parameters);

        Assert.Equal(
            PlaybackDiagnosticNativeParameterTransactionResult.VideoContextRejected,
            result.Result);
        Assert.True(parameters.IsDisposed);
        Assert.Equal(1, operations.CreateCount);
    }

    private static PlaybackDiagnosticDecoderParameterLease CreateParameters()
    {
        var preparation = PlaybackDiagnosticDecoderConfigurationAdapter.Prepare(
            PlaybackDiagnosticDecoderConfiguration.Baseline);
        return Assert.IsType<PlaybackDiagnosticDecoderParameterLease>(preparation.Lease);
    }

    private sealed class CountingOperations : IPlaybackDiagnosticNativeParameterOperations
    {
        public int CreateCount { get; private set; }

        public PlaybackDiagnosticNativeContextCreation CreateContext(
            object parent,
            PlaybackDecoderFfmpegCodecId codecId)
        {
            CreateCount++;
            return new(PlaybackDecoderFfmpegContextLeaseResult.DecoderUnavailable, null);
        }

        public PlaybackDecoderFfmpegParameterTransferResult Transfer(
            IPlaybackDiagnosticNativeContextOwner context,
            PlaybackDecoderFfmpegCodecParametersLease parameters) =>
            PlaybackDecoderFfmpegParameterTransferResult.NativeFailure;
    }
}
