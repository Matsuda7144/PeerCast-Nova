using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffOwnedEditedSamplePayloadTests
{
    [Fact]
    public void Copy_OwnsExactSampleBytesAndMetadata()
    {
        var file = CreateFile();
        var mediaData = Locate(file, new(8, 4, 12));

        var result = PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
            file,
            mediaData,
            out var ownedPayload);

        Assert.Equal(
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.Copied,
            result);
        var copied = Assert.IsType<PlaybackIsoBmffOwnedEditedSamplePayload>(
            ownedPayload);
        Assert.Same(mediaData, copied.MediaData);
        Assert.Equal((ulong)1, copied.SampleNumber);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, copied.Payload.ToArray());

        file[8] = 9;
        Assert.Equal((byte)1, copied.Payload.Span[0]);
    }

    [Fact]
    public void Copy_PayloadLimitIsCheckedBeforeAllocation()
    {
        var file = CreateFile();
        var result = PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
            file,
            Locate(file, new(8, 4, 12)),
            out var ownedPayload,
            new() { MaximumPayloadBytes = 3 });

        Assert.Equal(
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.PayloadLimitExceeded,
            result);
        Assert.Null(ownedPayload);
    }

    [Fact]
    public void Copy_EmptySampleIsNotPromotedToOwnedPayload()
    {
        var file = CreateFile();
        var result = PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
            file,
            Locate(file, new(12, 0, 12)),
            out var ownedPayload);

        Assert.Equal(
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.EmptyPayload,
            result);
        Assert.Null(ownedPayload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4 * 1024 * 1024 + 1)]
    public void Copy_InvalidLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
            [],
            null,
            out var ownedPayload,
            new() { MaximumPayloadBytes = maximum });

        Assert.Equal(
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.InvalidConfiguration,
            result);
        Assert.Null(ownedPayload);
    }

    [Fact]
    public void Copy_MissingMetadataFailsClosed()
    {
        var result = PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
            [],
            null,
            out var ownedPayload);

        Assert.Equal(
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.MissingMediaData,
            result);
        Assert.Null(ownedPayload);
    }

    private static PlaybackIsoBmffEditedSampleMediaData Locate(
        byte[] file,
        PlaybackIsoBmffSampleByteRange range)
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(0, 1, 1, 0,
            false);
        var appearances = Array.AsReadOnly(
            new[]
            {
                new PlaybackIsoBmffSampleEditMovieOverlap(
                    0,
                    0,
                    1,
                    1,
                    true),
            });
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            1,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
        var editedRange = new PlaybackIsoBmffEditedSampleByteRange(
            admission,
            range,
            new(0, 1, 1, 0, 1, 1));
        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.Found,
            PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
                file,
                editedRange,
                out var mediaData));
        return Assert.IsType<PlaybackIsoBmffEditedSampleMediaData>(mediaData);
    }

    private static byte[] CreateFile()
    {
        var file = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(0, 4), 20);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4, 4), 0x6D646174);
        file[8] = 1;
        file[9] = 2;
        file[10] = 3;
        file[11] = 4;
        return file;
    }
}
