using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffElementaryStreamDescriptorParserTests
{
    [Fact]
    public void Parse_ProjectsRequiredHierarchyAndOwnedDescriptorBytes()
    {
        var elementaryStream = ElementaryStream(
            0x1234,
            5,
            null,
            null,
            null,
            Descriptor(0x04, [0x40]),
            Descriptor(0x06, [0x02]));
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(
            elementaryStream));

        var result = PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
            box,
            audioEntry,
            out var descriptor);

        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffElementaryStreamDescriptor>(
            descriptor);
        Assert.Same(audioEntry, parsed.AudioEntry);
        Assert.Equal((ushort)0x1234, parsed.ElementaryStreamId);
        Assert.Equal((byte)5, parsed.StreamPriority);
        Assert.Null(parsed.DependsOnElementaryStreamId);
        Assert.Empty(parsed.Url.ToArray());
        Assert.Null(parsed.OcrElementaryStreamId);
        Assert.Collection(
            parsed.Children,
            decoder => Assert.Equal((byte)0x04, decoder.Header.Tag),
            sl => Assert.Equal((byte)0x06, sl.Header.Tag));
        Assert.Same(parsed.Children[0], parsed.DecoderConfigDescriptor);
        Assert.Same(parsed.Children[1], parsed.SlConfigDescriptor);
        Assert.True(parsed.TryGetPayload(
            parsed.DecoderConfigDescriptor,
            out var decoderPayload));
        Assert.Equal(new byte[] { 0x40 }, decoderPayload.ToArray());
        Assert.Equal(elementaryStream, parsed.DescriptorBytes.ToArray());

        box[audioEntry.Children[0].PayloadOffset + 4] = 0;
        Assert.Equal((byte)0x03, parsed.DescriptorBytes.Span[0]);
    }

    [Fact]
    public void Parse_ProjectsOptionalDependencyUrlAndOcrFields()
    {
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(
            ElementaryStream(
                7,
                31,
                8,
                [0x61, 0x62],
                9,
                Descriptor(0x04, []),
                Descriptor(0x06, []))));

        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult.Parsed,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                box,
                audioEntry,
                out var descriptor));
        Assert.Equal((ushort)8, descriptor!.DependsOnElementaryStreamId);
        Assert.Equal(new byte[] { 0x61, 0x62 }, descriptor.Url.ToArray());
        Assert.Equal((ushort)9, descriptor.OcrElementaryStreamId);
        Assert.Equal((byte)31, descriptor.StreamPriority);
    }

    [Fact]
    public void Parse_MissingAndDuplicateEsdsBoxesAreDistinct()
    {
        var (missingBox, missingEntry) = CreateAudioEntry();
        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingElementaryStreamBox,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                missingBox,
                missingEntry,
                out var missing));
        Assert.Null(missing);

        var descriptor = BaselineElementaryStream();
        var (duplicateBox, duplicateEntry) = CreateAudioEntry(
            ElementaryStreamBox(descriptor),
            ElementaryStreamBox(descriptor));
        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .DuplicateElementaryStreamBox,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                duplicateBox,
                duplicateEntry,
                out var duplicate));
        Assert.Null(duplicate);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(257, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1_048_577)]
    public void Parse_InvalidLimitsRejectBeforeInputAccess(
        int maximumDescriptors,
        int maximumBytes)
    {
        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .InvalidConfiguration,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                [],
                null,
                out var descriptor,
                new()
                {
                    MaximumDescriptorCount = maximumDescriptors,
                    MaximumConfigurationBytes = maximumBytes,
                }));
        Assert.Null(descriptor);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Parse_RequiresVersionZeroAndZeroFlags(byte version, int flags)
    {
        var child = ElementaryStreamBox(BaselineElementaryStream());
        child[8] = version;
        child[11] = (byte)flags;
        var (box, audioEntry) = CreateAudioEntry(child);

        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult.InvalidFullBox,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                box,
                audioEntry,
                out var descriptor));
        Assert.Null(descriptor);
    }

    [Fact]
    public void Parse_RequiresOuterElementaryStreamDescriptorTag()
    {
        var outer = BaselineElementaryStream();
        outer[0] = 0x04;
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(outer));

        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .UnsupportedDescriptor,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                box,
                audioEntry,
                out var descriptor));
        Assert.Null(descriptor);
    }

    [Theory]
    [MemberData(nameof(InvalidHierarchies))]
    public void Parse_InvalidRequiredHierarchyFailsClosed(
        byte[] elementaryStream,
        PlaybackIsoBmffElementaryStreamDescriptorParseResult expected)
    {
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(
            elementaryStream));

        Assert.Equal(
            expected,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                box,
                audioEntry,
                out var descriptor));
        Assert.Null(descriptor);
    }

    [Fact]
    public void Parse_DescriptorCountIsBounded()
    {
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(
            ElementaryStream(
                1,
                0,
                null,
                null,
                null,
                Descriptor(0x04, []),
                Descriptor(0x05, []),
                Descriptor(0x06, []))));

        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .DescriptorLimitExceeded,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                box,
                audioEntry,
                out var descriptor,
                new() { MaximumDescriptorCount = 2 }));
        Assert.Null(descriptor);
    }

    [Fact]
    public void Parse_StaleEsdsLocationFailsClosed()
    {
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(
            BaselineElementaryStream()));
        var changed = box.ToArray();
        changed[audioEntry.Children[0].Offset + 4] = (byte)'x';

        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .BoxLocationMismatch,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                changed,
                audioEntry,
                out var descriptor));
        Assert.Null(descriptor);
    }

    [Fact]
    public void TryGetPayloadRejectsForeignDescriptorLocation()
    {
        var (box, audioEntry) = CreateAudioEntry(ElementaryStreamBox(
            BaselineElementaryStream()));
        Assert.Equal(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult.Parsed,
            PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                box,
                audioEntry,
                out var descriptor));

        Assert.False(descriptor!.TryGetPayload(null, out var missing));
        Assert.True(missing.IsEmpty);
        Assert.False(descriptor.TryGetPayload(
            new(new(0x04, 2, 0), 0),
            out var foreign));
        Assert.True(foreign.IsEmpty);
    }

    public static TheoryData<byte[],
        PlaybackIsoBmffElementaryStreamDescriptorParseResult>
        InvalidHierarchies => new()
        {
            {
                ElementaryStream(1, 0, null, null, null,
                    Descriptor(0x06, [])),
                PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidChildSequence
            },
            {
                ElementaryStream(1, 0, null, null, null,
                    Descriptor(0x04, [])),
                PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .MissingSlConfigDescriptor
            },
            {
                ElementaryStream(1, 0, null, null, null,
                    Descriptor(0x04, []), Descriptor(0x04, []),
                    Descriptor(0x06, [])),
                PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .DuplicateDecoderConfigDescriptor
            },
            {
                ElementaryStream(1, 0, null, null, null,
                    Descriptor(0x04, []), Descriptor(0x06, []),
                    Descriptor(0x06, [])),
                PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .DuplicateSlConfigDescriptor
            },
            {
                Descriptor(0x03, [0, 1, 0x40, 2, 0x61]),
                PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidDescriptor
            },
        };

    internal static byte[] BaselineElementaryStream() =>
        ElementaryStream(
            0x1234,
            0,
            null,
            null,
            null,
            Descriptor(0x04, []),
            Descriptor(0x06, []));

    private static byte[] ElementaryStream(
        ushort elementaryStreamId,
        byte priority,
        ushort? dependsOn,
        byte[]? url,
        ushort? ocr,
        params byte[][] children)
    {
        var flags = priority;
        if (dependsOn is not null)
        {
            flags |= 0x80;
        }

        if (url is not null)
        {
            flags |= 0x40;
        }

        if (ocr is not null)
        {
            flags |= 0x20;
        }

        var payload = new List<byte>
        {
            (byte)(elementaryStreamId >> 8),
            (byte)elementaryStreamId,
            flags,
        };
        if (dependsOn is { } dependency)
        {
            payload.Add((byte)(dependency >> 8));
            payload.Add((byte)dependency);
        }

        if (url is not null)
        {
            payload.Add(checked((byte)url.Length));
            payload.AddRange(url);
        }

        if (ocr is { } ocrId)
        {
            payload.Add((byte)(ocrId >> 8));
            payload.Add((byte)ocrId);
        }

        foreach (var child in children)
        {
            payload.AddRange(child);
        }

        return Descriptor(0x03, payload.ToArray());
    }

    private static byte[] Descriptor(byte tag, byte[] payload)
    {
        Assert.True(payload.Length < 128);
        return [tag, (byte)payload.Length, .. payload];
    }

    internal static byte[] ElementaryStreamBox(byte[] descriptor)
    {
        var child = new byte[12 + descriptor.Length];
        WriteHeader(child, 0x65736473);
        descriptor.CopyTo(child, 12);
        return child;
    }

    internal static (byte[] Box, PlaybackIsoBmffAudioSampleEntry Entry)
        CreateAudioEntry(params byte[][] children)
    {
        var payload = new byte[28 + children.Sum(child => child.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(16, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(18, 2), 16);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(24, 4),
            0xBB800000);
        var offset = 28;
        foreach (var child in children)
        {
            child.CopyTo(payload, offset);
            offset += child.Length;
        }

        var box = new byte[24 + payload.Length];
        WriteHeader(box, 0x73747364);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4),
            (uint)(8 + payload.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), 0x6D703461);
        payload.CopyTo(box, 24);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var description));
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            description!.Entries[0],
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            PlaybackIsoBmffHandlerKind.Audio);
        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.Parsed,
            PlaybackIsoBmffAudioSampleEntryParser.Parse(
                box,
                selection,
                out var audioEntry));
        return (box, Assert.IsType<PlaybackIsoBmffAudioSampleEntry>(audioEntry));
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
