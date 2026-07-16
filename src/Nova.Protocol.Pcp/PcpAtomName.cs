using System.Text;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Four wire bytes identifying a PCP atom.
///
/// Legacy PCP implementations encode names as a NUL-padded four-byte ASCII
/// field (equivalent to Ruby's Z4). For example, "sid" is written as
/// 73 69 64 00 and "ok" as 6F 6B 00 00.
/// </summary>
public readonly record struct PcpAtomName(
    byte Byte0,
    byte Byte1,
    byte Byte2,
    byte Byte3)
{
    public const int Size = 4;

    public static PcpAtomName FromAscii(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length is < 1 or > Size)
        {
            throw new ArgumentException(
                "A PCP atom name must contain between one and four ASCII characters.",
                nameof(value));
        }

        Span<byte> bytes = stackalloc byte[Size];
        bytes.Clear();

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character is '\0' or > '\x7F')
            {
                throw new ArgumentException(
                    "A PCP atom name must contain non-NUL ASCII characters.",
                    nameof(value));
            }

            bytes[index] = (byte)character;
        }

        return new PcpAtomName(bytes[0], bytes[1], bytes[2], bytes[3]);
    }

    public static PcpAtomName Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
        {
            throw new ArgumentException(
                "The source is shorter than a PCP atom name.",
                nameof(source));
        }

        return new PcpAtomName(source[0], source[1], source[2], source[3]);
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException(
                "The destination is shorter than a PCP atom name.",
                nameof(destination));
        }

        destination[0] = Byte0;
        destination[1] = Byte1;
        destination[2] = Byte2;
        destination[3] = Byte3;
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Size];
        WriteTo(bytes);

        var length = Size;

        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        var builder = new StringBuilder(length);
        const string hexDigits = "0123456789ABCDEF";

        for (var index = 0; index < length; index++)
        {
            var value = bytes[index];

            if (value is >= 0x21 and <= 0x7E && value != (byte)'\\')
            {
                builder.Append((char)value);
                continue;
            }

            if (value == (byte)'\\')
            {
                builder.Append("\\\\");
                continue;
            }

            builder.Append("\\x");
            builder.Append(hexDigits[value >> 4]);
            builder.Append(hexDigits[value & 0x0F]);
        }

        return builder.ToString();
    }
}
