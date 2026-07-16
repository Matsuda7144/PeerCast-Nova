using System.Collections.Immutable;
using System.Text;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// A content-free description of one observed PCP atom tree.
/// No payload bytes or decoded user-generated strings are retained.
/// </summary>
public sealed record PcpAtomShape
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private PcpAtomShape(
        PcpAtomName name,
        bool isParent,
        int value,
        long encodedLength,
        bool looksLikeNullTerminatedUtf8,
        ImmutableArray<PcpAtomShape> children)
    {
        Name = name;
        IsParent = isParent;
        Value = value;
        EncodedLength = encodedLength;
        LooksLikeNullTerminatedUtf8 = looksLikeNullTerminatedUtf8;
        Children = children;
    }

    public PcpAtomName Name { get; }

    public bool IsParent { get; }

    /// <summary>
    /// Gets the child count for a parent or content length for a leaf.
    /// </summary>
    public int Value { get; }

    public long EncodedLength { get; }

    public bool LooksLikeNullTerminatedUtf8 { get; }

    public ImmutableArray<PcpAtomShape> Children { get; }

    internal static PcpAtomShape FromAtom(PcpAtom atom)
    {
        ArgumentNullException.ThrowIfNull(atom);

        if (!atom.IsParent)
        {
            return new PcpAtomShape(
                atom.Name,
                isParent: false,
                atom.Content.Length,
                checked(PcpAtomCodec.HeaderSize + atom.Content.Length),
                LooksLikeStrictNullTerminatedUtf8(atom.Content.Span),
                ImmutableArray<PcpAtomShape>.Empty);
        }

        var children = ImmutableArray.CreateBuilder<PcpAtomShape>(
            atom.Children.Count);

        long encodedLength = PcpAtomCodec.HeaderSize;

        foreach (var child in atom.Children)
        {
            var childShape = FromAtom(child);
            children.Add(childShape);
            encodedLength = checked(encodedLength + childShape.EncodedLength);
        }

        return new PcpAtomShape(
            atom.Name,
            isParent: true,
            atom.Children.Count,
            encodedLength,
            looksLikeNullTerminatedUtf8: false,
            children.MoveToImmutable());
    }

    private static bool LooksLikeStrictNullTerminatedUtf8(
        ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty || content[^1] != 0)
        {
            return false;
        }

        var textBytes = content[..^1];

        if (textBytes.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetCharCount(textBytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
