using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Atomically reads or writes one complete owned PCP atom tree.
///
/// A failed or incomplete read does not advance the caller's SequenceReader.
/// </summary>
public static class PcpAtomTreeCodec
{
    public static bool TryRead(
        ref SequenceReader<byte> reader,
        PcpProtocolLimits limits,
        out PcpAtom atom,
        out PcpParseError error)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        var probe = reader;

        if (!TryReadCore(
            ref probe,
            limits,
            depth: 0,
            out atom,
            out error))
        {
            return false;
        }

        reader = probe;
        return true;
    }

    public static void Write(
        IBufferWriter<byte> writer,
        PcpAtom atom,
        PcpProtocolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(atom);

        var effectiveLimits = limits ?? new PcpProtocolLimits();
        effectiveLimits.Validate();

        WriteCore(writer, atom, effectiveLimits, depth: 0);
    }

    private static bool TryReadCore(
        ref SequenceReader<byte> reader,
        PcpProtocolLimits limits,
        int depth,
        out PcpAtom atom,
        out PcpParseError error)
    {
        atom = null!;

        if (!PcpAtomSequenceCodec.TryReadHeader(
            ref reader,
            limits,
            out var header,
            out error))
        {
            return false;
        }

        if (header.IsParent)
        {
            if (depth >= limits.MaxNestingDepth)
            {
                error = PcpParseError.NestingDepthLimitExceeded;
                return false;
            }

            var children = new PcpAtom[header.Value];

            for (var index = 0; index < children.Length; index++)
            {
                if (!TryReadCore(
                    ref reader,
                    limits,
                    depth + 1,
                    out children[index],
                    out error))
                {
                    return false;
                }
            }

            atom = PcpAtom.Parent(header.Name, children);
            error = PcpParseError.None;
            return true;
        }

        if (reader.Remaining < header.Value)
        {
            error = PcpParseError.NeedMoreData;
            return false;
        }

        var content = reader.Sequence
            .Slice(reader.Position, header.Value)
            .ToArray();

        reader.Advance(header.Value);

        atom = PcpAtom.Leaf(header.Name, content);
        error = PcpParseError.None;
        return true;
    }

    private static void WriteCore(
        IBufferWriter<byte> writer,
        PcpAtom atom,
        PcpProtocolLimits limits,
        int depth)
    {
        if (atom.IsParent)
        {
            if (depth >= limits.MaxNestingDepth)
            {
                throw new InvalidOperationException(
                    $"The atom tree exceeds {limits.MaxNestingDepth} levels.");
            }

            PcpAtomWriter.WriteParentHeader(
                writer,
                atom.Name,
                atom.Children.Count,
                limits);

            foreach (var child in atom.Children)
            {
                WriteCore(writer, child, limits, depth + 1);
            }

            return;
        }

        PcpAtomWriter.WriteLeaf(
            writer,
            atom.Name,
            atom.Content.Span,
            limits);
    }
}
