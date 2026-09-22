using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationAacDescriptorLimitProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Soun = 0x736f756e;
    private const int SampleDescriptionOffset = 81;

    [Fact]
    public void Project_Mp4aTrackOverDescriptorCountLimitIsRejected()
    {
        var children = new List<byte[]> { Descriptor(0x04) };
        for (var index = 0; index < 63; index++)
        {
            children.Add(Descriptor(0x05));
        }

        children.Add(Descriptor(0x06));
        var elementaryStream = ElementaryStream(children);
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests.CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(elementaryStream));
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .DescriptorLimitExceeded,
            result);
        Assert.Null(projection);
    }

    private static byte[] ElementaryStream(IEnumerable<byte[]> children)
    {
        var payload = new List<byte> { 0, 1, 0 };
        foreach (var child in children)
        {
            payload.AddRange(child);
        }

        return Descriptor(0x03, payload.ToArray());
    }

    private static byte[] Descriptor(byte tag, byte[]? payload = null)
    {
        payload ??= [];
        var lengthBytes = EncodeDescriptorLength(payload.Length);
        return [tag, .. lengthBytes, .. payload];
    }

    private static byte[] EncodeDescriptorLength(int length)
    {
        Assert.InRange(length, 0, 0x0FFFFFFF);
        Span<byte> encoded = stackalloc byte[4];
        var offset = encoded.Length;
        encoded[--offset] = (byte)(length & 0x7F);
        while ((length >>= 7) != 0)
        {
            encoded[--offset] = (byte)((length & 0x7F) | 0x80);
        }

        return encoded[offset..].ToArray();
    }

    private static byte[] CreateInitializationSegment(
        ReadOnlySpan<byte> sampleDescriptionBox)
    {
        var segment = new byte[
            SampleDescriptionOffset + sampleDescriptionBox.Length];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(
            segment.AsSpan(8, 8),
            checked((uint)(segment.Length - 8)),
            Moov);
        WriteHeader(
            segment.AsSpan(16, 8),
            checked((uint)(segment.Length - 16)),
            Trak);
        WriteHeader(
            segment.AsSpan(24, 8),
            checked((uint)(segment.Length - 24)),
            Mdia);
        WriteHandler(segment.AsSpan(32, 33), Soun);
        WriteHeader(
            segment.AsSpan(65, 8),
            checked((uint)(segment.Length - 65)),
            Minf);
        WriteHeader(
            segment.AsSpan(73, 8),
            checked((uint)(segment.Length - 73)),
            Stbl);
        sampleDescriptionBox.CopyTo(segment.AsSpan(SampleDescriptionOffset));
        return segment;
    }

    private static void WriteHandler(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 33, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.Slice(16, 4),
            handlerType);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
