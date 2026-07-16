namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Describes the fixed eight-byte PCP atom header.
/// For a leaf atom, Value is the content length.
/// For a parent atom, Value is the number of child atoms.
/// </summary>
public readonly record struct PcpAtomHeader
{
    public PcpAtomHeader(PcpAtomName name, bool isParent, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The atom value cannot be negative.");
        }

        Name = name;
        IsParent = isParent;
        Value = value;
    }

    public PcpAtomName Name { get; }

    public bool IsParent { get; }

    public int Value { get; }
}
