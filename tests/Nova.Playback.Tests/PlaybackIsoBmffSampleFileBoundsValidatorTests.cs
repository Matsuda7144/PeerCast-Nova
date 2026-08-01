namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleFileBoundsValidatorTests
{
    [Theory]
    [InlineData(120)]
    [InlineData(200)]
    public void Validate_RangeEndingAtOrBeforeFileLengthIsInBounds(
        ulong fileLength)
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            new(100, 20, 120),
            fileLength);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.InBounds,
            result);
    }

    [Fact]
    public void Validate_RangeEndingAfterFileLengthIsOutsideFile()
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            new(100, 20, 120),
            119);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.OutsideFile,
            result);
    }

    [Fact]
    public void Validate_ZeroSizeRangeAtFileEndIsInBounds()
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            new(120, 0, 120),
            120);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.InBounds,
            result);
    }

    [Fact]
    public void Validate_ZeroSizeRangeAfterFileEndIsOutsideFile()
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            new(121, 0, 121),
            120);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.OutsideFile,
            result);
    }

    [Fact]
    public void Validate_InconsistentEndOffsetIsInvalid()
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            new(100, 20, 121),
            200);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.InvalidRange,
            result);
    }

    [Fact]
    public void Validate_OverflowingRangeIsInvalid()
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            new(ulong.MaxValue, 1, 0),
            ulong.MaxValue);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.InvalidRange,
            result);
    }

    [Fact]
    public void Validate_MissingRangeIsInvalid()
    {
        var result = PlaybackIsoBmffSampleFileBoundsValidator.Validate(
            null,
            200);

        Assert.Equal(
            PlaybackIsoBmffSampleFileBoundsValidationResult.InvalidRange,
            result);
    }
}
