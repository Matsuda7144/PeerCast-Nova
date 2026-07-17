namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegCodecParametersTests
{
    [Fact]
    public void Create_H264OwnsBoundedImmutableParametersAndZeroPadding()
    {
        var extradata = new byte[] { 1, 100, 0, 31, 255 };

        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                width: 1920,
                height: 1080,
                extradata));

        Assert.Equal(PlaybackDecoderFfmpegCodecParametersResult.Created, result.Result);
        using var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(result.Lease);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.H264, lease.CodecId);
        Assert.Equal(1920, lease.Width);
        Assert.Equal(1080, lease.Height);
        Assert.Equal(0, lease.SampleRate);
        Assert.Equal(0, lease.ChannelCount);
        Assert.Equal(extradata.Length, lease.ExtradataLength);

        var owned = lease.UseOwnedParameters(static parameters => parameters);
        Assert.Equal(extradata, owned.Extradata.ToArray());
        Assert.Equal(
            extradata.Length +
            PlaybackDecoderFfmpegCodecParametersFactory.FfmpegInputBufferPaddingSize,
            owned.PaddedExtradata.Length);
        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersFactory.FfmpegInputBufferPaddingSize,
            owned.Padding.Length);
        Assert.All(owned.Padding.ToArray(), static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void Create_AacOwnsBoundedImmutableParameters()
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                sampleRate: 48_000,
                channelCount: 2,
                new byte[] { 0x12, 0x10 }));

        Assert.Equal(PlaybackDecoderFfmpegCodecParametersResult.Created, result.Result);
        using var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(result.Lease);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, lease.CodecId);
        Assert.Equal(0, lease.Width);
        Assert.Equal(0, lease.Height);
        Assert.Equal(48_000, lease.SampleRate);
        Assert.Equal(2, lease.ChannelCount);
    }

    [Fact]
    public void Create_DefensivelyCopiesCallerExtradata()
    {
        var extradata = new byte[] { 1, 2, 3, 4 };
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(640, 480, extradata));
        using var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(result.Lease);

        extradata[0] = 99;
        extradata[1] = 98;

        var owned = lease.UseOwnedParameters(
            static parameters => parameters.Extradata.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, owned);
    }

    [Fact]
    public void Create_EmptyExtradataFailsClosed()
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                width: 640,
                height: 480,
                ReadOnlyMemory<byte>.Empty));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.EmptyExtradata,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Fact]
    public void Create_MaximumExtradataLengthIsAccepted()
    {
        var extradata = new byte[
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumExtradataLength];
        extradata[0] = 1;

        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(48_000, 2, extradata));

        Assert.Equal(PlaybackDecoderFfmpegCodecParametersResult.Created, result.Result);
        using var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(result.Lease);
        Assert.Equal(extradata.Length, lease.ExtradataLength);
    }

    [Fact]
    public void Create_ExtradataAboveMaximumFailsClosed()
    {
        var extradata = new byte[
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumExtradataLength + 1];

        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(48_000, 2, extradata));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.ExtradataTooLarge,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Fact]
    public void Create_H264MaximumDimensionsAreAccepted()
    {
        var maximum = PlaybackDecoderFfmpegCodecParametersFactory.MaximumVideoDimension;

        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                maximum,
                maximum,
                new byte[] { 1 }));

        Assert.Equal(PlaybackDecoderFfmpegCodecParametersResult.Created, result.Result);
        using var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(result.Lease);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(-1, 1080)]
    [InlineData(1920, 0)]
    [InlineData(1920, -1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Create_InvalidH264NumericsFailClosed(int width, int height)
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                width,
                height,
                new byte[] { 1 }));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Theory]
    [InlineData(7_999, 2)]
    [InlineData(384_001, 2)]
    [InlineData(48_000, 0)]
    [InlineData(48_000, -1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Create_InvalidAacNumericsFailClosed(int sampleRate, int channelCount)
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                sampleRate,
                channelCount,
                new byte[] { 1 }));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Fact]
    public void Create_H264WithAudioFieldsFailsAsInconsistent()
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            new PlaybackDecoderFfmpegCodecParametersInput(
                PlaybackDecoderFfmpegCodecId.H264,
                width: 1920,
                height: 1080,
                sampleRate: 48_000,
                channelCount: 2,
                new byte[] { 1 }));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.InconsistentParameters,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Fact]
    public void Create_AacWithVideoFieldsFailsAsInconsistent()
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            new PlaybackDecoderFfmpegCodecParametersInput(
                PlaybackDecoderFfmpegCodecId.Aac,
                width: 640,
                height: 480,
                sampleRate: 48_000,
                channelCount: 2,
                new byte[] { 1 }));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.InconsistentParameters,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Fact]
    public void Create_UndefinedCodecIdFailsBeforeOwnership()
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            new PlaybackDecoderFfmpegCodecParametersInput(
                (PlaybackDecoderFfmpegCodecId)int.MaxValue,
                width: 1,
                height: 1,
                sampleRate: 0,
                channelCount: 0,
                new byte[] { 1 }));

        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.UnknownCodecId,
            result.Result);
        Assert.Null(result.Lease);
    }

    [Fact]
    public void Dispose_IsIdempotentClearsOwnedBufferAndRejectsFurtherUse()
    {
        var result = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                48_000,
                2,
                new byte[] { 1, 2, 3, 4 }));
        var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(result.Lease);
        var captured = lease.UseOwnedParameters(
            static parameters => parameters.PaddedExtradata);

        lease.Dispose();
        lease.Dispose();

        Assert.True(lease.IsDisposed);
        Assert.All(captured.ToArray(), static value => Assert.Equal((byte)0, value));
        Assert.Throws<ObjectDisposedException>(() =>
            lease.UseOwnedParameters(static parameters => parameters.ExtradataLength));
    }

    [Fact]
    public void ResultsAndDisposedFailureDoNotExposeExtradataContent()
    {
        var sensitive = new byte[] { 0x53, 0x45, 0x43, 0x52, 0x45, 0x54 };
        var invalid = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(1, 2, sensitive));

        Assert.DoesNotContain(
            "SECRET",
            invalid.Result.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [nameof(invalid.Lease), nameof(invalid.Result)],
            invalid.GetType().GetProperties().Select(static property => property.Name).Order());

        var valid = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            PlaybackDecoderFfmpegCodecParametersInput.ForAac(48_000, 2, sensitive));
        var lease = Assert.IsType<PlaybackDecoderFfmpegCodecParametersLease>(valid.Lease);
        lease.Dispose();
        var exception = Assert.Throws<ObjectDisposedException>(() =>
            lease.UseOwnedParameters(static parameters => parameters.ExtradataLength));
        Assert.DoesNotContain(
            "SECRET",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
