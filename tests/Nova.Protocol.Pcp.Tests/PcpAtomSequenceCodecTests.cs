using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomSequenceCodecTests
{
    [Fact]
    public void HeaderAcrossSegments_IsDecodedWithoutCopyingTheSequence()
    {
        var bytes = new byte[PcpAtomCodec.HeaderSize];

        PcpAtomCodec.WriteHeader(
            bytes,
            new PcpAtomHeader(
                PcpAtomName.FromAscii("test"),
                isParent: false,
                value: 7));

        var sequence = SegmentedSequence.Create(
            bytes[..3],
            bytes[3..6],
            bytes[6..]);

        var reader = new SequenceReader<byte>(sequence);

        var success = PcpAtomSequenceCodec.TryReadHeader(
            ref reader,
            new PcpProtocolLimits(),
            out var header,
            out var error);

        Assert.True(success);
        Assert.Equal(PcpParseError.None, error);
        Assert.Equal(PcpAtomName.FromAscii("test"), header.Name);
        Assert.Equal(7, header.Value);
        Assert.Equal(PcpAtomCodec.HeaderSize, reader.Consumed);
    }

    [Fact]
    public void IncompleteHeader_DoesNotAdvanceReader()
    {
        var sequence = new ReadOnlySequence<byte>(new byte[7]);
        var reader = new SequenceReader<byte>(sequence);

        var success = PcpAtomSequenceCodec.TryReadHeader(
            ref reader,
            new PcpProtocolLimits(),
            out _,
            out var error);

        Assert.False(success);
        Assert.Equal(PcpParseError.NeedMoreData, error);
        Assert.Equal(0, reader.Consumed);
    }

    private static class SegmentedSequence
    {
        public static ReadOnlySequence<byte> Create(params byte[][] segments)
        {
            if (segments.Length == 0)
            {
                return ReadOnlySequence<byte>.Empty;
            }

            Segment? first = null;
            Segment? last = null;

            foreach (var bytes in segments)
            {
                var segment = new Segment(bytes);

                if (first is null)
                {
                    first = segment;
                    last = segment;
                    continue;
                }

                last = last!.Append(segment);
            }

            return new ReadOnlySequence<byte>(
                first!,
                0,
                last!,
                last!.Memory.Length);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public Segment Append(Segment next)
            {
                next.RunningIndex = RunningIndex + Memory.Length;
                Next = next;
                return next;
            }
        }
    }
}
