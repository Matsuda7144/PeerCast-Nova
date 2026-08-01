using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditedSamplePayloadViewTests
{
    [Fact]
    public void TryGet_ReturnsExactNonOwningSampleSlice()
    {
        var file = CreateFile();
        var mediaData = Locate(file, new(12, 4, 16));

        var result = PlaybackIsoBmffEditedSamplePayloadView.TryGet(
            file,
            mediaData,
            out var payload);

        Assert.Equal(PlaybackIsoBmffEditedSamplePayloadViewResult.Found, result);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, payload.ToArray());

        file[12] = 9;
        Assert.Equal((byte)9, payload[0]);
    }

    [Fact]
    public void TryGet_ZeroSizeSampleAtPayloadEndIsSupported()
    {
        var file = CreateFile();
        var mediaData = Locate(file, new(20, 0, 20));

        var result = PlaybackIsoBmffEditedSamplePayloadView.TryGet(
            file,
            mediaData,
            out var payload);

        Assert.Equal(PlaybackIsoBmffEditedSamplePayloadViewResult.Found, result);
        Assert.True(payload.IsEmpty);
    }

    [Fact]
    public void TryGet_MissingMetadataFailsBeforeFileAccess()
    {
        var result = PlaybackIsoBmffEditedSamplePayloadView.TryGet(
            [],
            null,
            out var payload);

        Assert.Equal(
            PlaybackIsoBmffEditedSamplePayloadViewResult.MissingMediaData,
            result);
        Assert.True(payload.IsEmpty);
    }

    [Fact]
    public void TryGet_DifferentOrShorterFileFailsClosed()
    {
        var original = CreateFile();
        var mediaData = Locate(original, new(12, 4, 16));

        var shorterResult = PlaybackIsoBmffEditedSamplePayloadView.TryGet(
            original.AsSpan(0, 15),
            mediaData,
            out var shorterPayload);
        Assert.Equal(
            PlaybackIsoBmffEditedSamplePayloadViewResult.SampleOutsideFile,
            shorterResult);
        Assert.True(shorterPayload.IsEmpty);

        var changed = CreateFile();
        BinaryPrimitives.WriteUInt32BigEndian(changed.AsSpan(0, 4), 12);
        var changedResult = PlaybackIsoBmffEditedSamplePayloadView.TryGet(
            changed,
            mediaData,
            out var changedPayload);
        Assert.Equal(
            PlaybackIsoBmffEditedSamplePayloadViewResult
                .MediaDataLocationMismatch,
            changedResult);
        Assert.True(changedPayload.IsEmpty);
    }

    private static PlaybackIsoBmffEditedSampleMediaData Locate(
        byte[] file,
        PlaybackIsoBmffSampleByteRange range)
    {
        var editedRange = CreateEditedRange(range);
        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.Found,
            PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
                file,
                editedRange,
                out var mediaData));
        return Assert.IsType<PlaybackIsoBmffEditedSampleMediaData>(mediaData);
    }

    private static PlaybackIsoBmffEditedSampleByteRange CreateEditedRange(
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
        return new(
            admission,
            range,
            new(0, 1, 1, 0, 1, 1));
    }

    private static byte[] CreateFile()
    {
        var file = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(0, 4), 20);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4, 4), 0x6D646174);
        file[12] = 1;
        file[13] = 2;
        file[14] = 3;
        file[15] = 4;
        return file;
    }
}
