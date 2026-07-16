namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// An owned PCP atom tree intended for small control messages.
///
/// Stream payload processing should continue to use the callback-based
/// incremental decoder instead of allocating complete trees.
/// </summary>
public sealed class PcpAtom
{
    private PcpAtom(
        PcpAtomName name,
        bool isParent,
        IReadOnlyList<PcpAtom> children,
        ReadOnlyMemory<byte> content)
    {
        Name = name;
        IsParent = isParent;
        Children = children;
        Content = content;
    }

    public PcpAtomName Name { get; }

    public bool IsParent { get; }

    public IReadOnlyList<PcpAtom> Children { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public static PcpAtom Parent(
        PcpAtomName name,
        params PcpAtom[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return Parent(name, (IReadOnlyList<PcpAtom>)children);
    }

    public static PcpAtom Parent(
        PcpAtomName name,
        IReadOnlyList<PcpAtom> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        return new PcpAtom(
            name,
            isParent: true,
            children.ToArray(),
            ReadOnlyMemory<byte>.Empty);
    }

    public static PcpAtom Leaf(
        PcpAtomName name,
        ReadOnlySpan<byte> content)
    {
        return new PcpAtom(
            name,
            isParent: false,
            Array.Empty<PcpAtom>(),
            content.ToArray());
    }

    public PcpAtom? FindFirst(PcpAtomName name)
    {
        if (!IsParent)
        {
            return null;
        }

        foreach (var child in Children)
        {
            if (child.Name == name)
            {
                return child;
            }
        }

        return null;
    }
}
