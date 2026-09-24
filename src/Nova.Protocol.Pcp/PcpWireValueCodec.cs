using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace PeerCast.Nova.Protocol.Pcp;

public static class PcpWireValueCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] EncodeUInt16(ushort value)
    {
        var bytes = new byte[sizeof(ushort)];
        WriteUInt16(bytes, value);
        return bytes;
    }

    public static void WriteUInt16(Span<byte> destination, ushort value)
    {
        RequireDestinationLength(destination, sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    public static ushort DecodeUInt16(ReadOnlySpan<byte> value)
    {
        RequireLength(value, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(value);
    }

    public static byte DecodeByte(ReadOnlySpan<byte> value)
    {
        RequireLength(value, sizeof(byte));
        return value[0];
    }

    public static byte[] EncodeUInt32(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        WriteUInt32(bytes, value);
        return bytes;
    }

    public static void WriteUInt32(Span<byte> destination, uint value)
    {
        RequireDestinationLength(destination, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    public static uint DecodeUInt32(ReadOnlySpan<byte> value)
    {
        RequireLength(value, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(value);
    }

    public static byte[] EncodeNullTerminatedUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A PCP string cannot contain an embedded NUL.",
                nameof(value));
        }

        var contentLength = Encoding.UTF8.GetByteCount(value);
        var bytes = new byte[contentLength + 1];

        Encoding.UTF8.GetBytes(value, bytes);
        bytes[^1] = 0;
        return bytes;
    }

    public static string DecodeNullTerminatedUtf8(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value[^1] != 0)
        {
            throw new InvalidDataException(
                "A PCP string must end with a NUL byte.");
        }

        var textBytes = value[..^1];

        if (textBytes.Contains((byte)0))
        {
            throw new InvalidDataException(
                "A PCP string cannot contain an embedded NUL byte.");
        }

        try
        {
            return StrictUtf8.GetString(textBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "A PCP string contains invalid UTF-8.",
                exception);
        }
    }

    /// <summary>
    /// Encodes a Guid in RFC/network byte order, matching the 16-byte PCP GID.
    /// </summary>
    public static byte[] EncodeGuid(Guid value)
    {
        var networkOrder = new byte[16];
        WriteGuid(networkOrder, value);
        return networkOrder;
    }

    public static void WriteGuid(Span<byte> destination, Guid value)
    {
        RequireDestinationLength(destination, 16);

        Span<byte> littleEndian = stackalloc byte[16];
        value.TryWriteBytes(littleEndian);

        destination[0] = littleEndian[3];
        destination[1] = littleEndian[2];
        destination[2] = littleEndian[1];
        destination[3] = littleEndian[0];
        destination[4] = littleEndian[5];
        destination[5] = littleEndian[4];
        destination[6] = littleEndian[7];
        destination[7] = littleEndian[6];

        littleEndian[8..].CopyTo(destination[8..]);
    }

    public static Guid DecodeGuid(ReadOnlySpan<byte> value)
    {
        RequireLength(value, 16);

        var littleEndian = new byte[16];

        littleEndian[0] = value[3];
        littleEndian[1] = value[2];
        littleEndian[2] = value[1];
        littleEndian[3] = value[0];
        littleEndian[4] = value[5];
        littleEndian[5] = value[4];
        littleEndian[6] = value[7];
        littleEndian[7] = value[6];

        value[8..].CopyTo(littleEndian.AsSpan(8));
        return new Guid(littleEndian);
    }

    /// <summary>
    /// Legacy PCP IPv4 values store the address bytes in reverse order.
    /// </summary>
    public static byte[] EncodeLegacyIpv4(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var ipv4 = address.MapToIPv4();
        var bytes = ipv4.GetAddressBytes();
        Array.Reverse(bytes);
        return bytes;
    }

    public static IPAddress DecodeLegacyIpv4(ReadOnlySpan<byte> value)
    {
        RequireLength(value, 4);

        var bytes = value.ToArray();
        Array.Reverse(bytes);
        return new IPAddress(bytes);
    }

    private static void RequireLength(
        ReadOnlySpan<byte> value,
        int expectedLength)
    {
        if (value.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Expected {expectedLength} bytes, but received {value.Length}.");
        }
    }

    private static void RequireDestinationLength(
        Span<byte> destination,
        int expectedLength)
    {
        if (destination.Length < expectedLength)
        {
            throw new ArgumentException(
                $"The destination must contain at least {expectedLength} bytes.",
                nameof(destination));
        }
    }
}
