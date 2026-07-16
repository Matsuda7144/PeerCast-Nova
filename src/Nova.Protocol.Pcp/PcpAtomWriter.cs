using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp;

public static class PcpAtomWriter
{
    public static void WriteLeaf(
        IBufferWriter<byte> writer,
        PcpAtomName name,
        ReadOnlySpan<byte> payload,
        PcpProtocolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        WriteLeafHeader(writer, name, payload.Length, limits);

        if (payload.IsEmpty)
        {
            return;
        }

        var destination = writer.GetSpan(payload.Length);
        payload.CopyTo(destination);
        writer.Advance(payload.Length);
    }

    /// <summary>
    /// Writes only a leaf header. The caller must write exactly
    /// <paramref name="contentLength"/> payload bytes before another atom.
    /// This permits a bounded stream payload to follow a small encoded prefix
    /// without first copying that payload into the prefix buffer.
    /// </summary>
    public static void WriteLeafHeader(
        IBufferWriter<byte> writer,
        PcpAtomName name,
        int contentLength,
        PcpProtocolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var effectiveLimits = limits ?? new PcpProtocolLimits();
        effectiveLimits.Validate();

        if (contentLength < 0 ||
            contentLength > effectiveLimits.MaxContentLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentLength),
                contentLength,
                $"The content length must be between zero and {effectiveLimits.MaxContentLength} bytes.");
        }

        WriteHeader(
            writer,
            new PcpAtomHeader(
                name,
                isParent: false,
                contentLength));
    }

    public static void WriteParentHeader(
        IBufferWriter<byte> writer,
        PcpAtomName name,
        int childCount,
        PcpProtocolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var effectiveLimits = limits ?? new PcpProtocolLimits();
        effectiveLimits.Validate();

        if (childCount < 0 || childCount > effectiveLimits.MaxChildCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childCount),
                childCount,
                $"The child count must be between zero and {effectiveLimits.MaxChildCount}.");
        }

        WriteHeader(
            writer,
            new PcpAtomHeader(
                name,
                isParent: true,
                childCount));
    }

    private static void WriteHeader(
        IBufferWriter<byte> writer,
        PcpAtomHeader header)
    {
        var destination = writer.GetSpan(PcpAtomCodec.HeaderSize);
        PcpAtomCodec.WriteHeader(destination, header);
        writer.Advance(PcpAtomCodec.HeaderSize);
    }
}
