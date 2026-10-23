using System.Buffers.Binary;
using System.Text;

namespace PeerCast.Nova.Network.Rtmp;

/// <summary>Small, budgeted AMF0 command codec; no executable or reference values.</summary>
internal static class RtmpAmf0
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static List<object?> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 64 * 1024)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                ObsRtmpRejectionReason.AmfPayloadLimit, "RTMP command exceeds its budget.",
                new() { DeclaredPayloadLength = bytes.Length, AssembledPayloadLength = bytes.Length });
        }

        try
        {
            var reader = new Reader(bytes);
            var values = new List<object?>();
            while (reader.Remaining > 0)
            {
                values.Add(reader.ReadValue(0));
            }

            return values;
        }
        catch (InvalidDataException exception)
        {
            ObsRtmpRejection.Enrich(exception, new() { DeclaredPayloadLength = bytes.Length, AssembledPayloadLength = bytes.Length });
            throw;
        }
    }

    public static byte[] Encode(params object?[] values)
    {
        using var output = new MemoryStream();
        foreach (var value in values)
        {
            WriteValue(output, value);
        }

        return output.ToArray();
    }

    private static void WriteValue(Stream output, object? value)
    {
        Span<byte> scratch = stackalloc byte[8];
        switch (value)
        {
            case null:
                output.WriteByte(5);
                break;
            case string text:
                output.WriteByte(2);
                WriteString(output, text);
                break;
            case double number:
                output.WriteByte(0);
                BinaryPrimitives.WriteDoubleBigEndian(scratch, number);
                output.Write(scratch);
                break;
            case Dictionary<string, object?> properties:
                output.WriteByte(3);
                foreach (var (key, property) in properties)
                {
                    WriteString(output, key);
                    WriteValue(output, property);
                }

                output.Write([0, 0, 9]);
                break;
            default:
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfResponse,
                    ObsRtmpRejectionReason.UnsupportedAmfResponseValue, "Unsupported AMF response value.");
        }
    }

    private static void WriteString(Stream output, string text)
    {
        var bytes = Utf8.GetBytes(text);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
        output.Write(length);
        output.Write(bytes);
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _bytes = bytes;
        private int _values;

        public readonly int Remaining => _bytes.Length;

        public object? ReadValue(int depth)
        {
            if (++_values > 256 || depth > 4)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                    ObsRtmpRejectionReason.AmfValueOrDepthLimit, "AMF nesting or value budget exceeded.");
            }

            var type = Take(1)[0];
            switch (type)
            {
                case 0:
                    var number = BinaryPrimitives.ReadDoubleBigEndian(Take(8));
                    if (!double.IsFinite(number))
                    {
                        throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                            ObsRtmpRejectionReason.NonFiniteAmfNumber, "Non-finite AMF number.");
                    }

                    return number;
                case 1:
                    var boolean = Take(1)[0];
                    if (boolean > 1)
                    {
                        throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                            ObsRtmpRejectionReason.InvalidAmfBoolean, "Invalid AMF boolean.");
                    }

                    return boolean == 1;
                case 2:
                    return ReadString();
                case 3:
                case 8:
                    if (type == 8)
                    {
                        Take(4);
                    }

                    var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                    while (true)
                    {
                        if (_bytes.Length >= 3 && _bytes[0] == 0 && _bytes[1] == 0 && _bytes[2] == 9)
                        {
                            Take(3);
                            return properties;
                        }

                        var name = ReadString();
                        if (!properties.TryAdd(name, ReadValue(depth + 1)))
                        {
                            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                                ObsRtmpRejectionReason.DuplicateAmfProperty, "Duplicate AMF property.");
                        }
                    }
                case 5:
                case 6:
                    return null;
                case 10:
                    var count = BinaryPrimitives.ReadUInt32BigEndian(Take(4));
                    if (count > 128)
                    {
                        throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                            ObsRtmpRejectionReason.AmfArrayLimit, "AMF array exceeds its budget.");
                    }

                    var array = new object?[count];
                    for (var i = 0; i < array.Length; i++)
                    {
                        array[i] = ReadValue(depth + 1);
                    }

                    return array;
                default:
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                        ObsRtmpRejectionReason.UnsupportedAmfType, "Unsupported AMF0 type.");
            }
        }

        private string ReadString()
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(Take(2));
            if (length > 4096)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                    ObsRtmpRejectionReason.AmfStringLimit, "AMF string exceeds its budget.");
            }

            try
            {
                return Utf8.GetString(Take(length));
            }
            catch (DecoderFallbackException)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                    ObsRtmpRejectionReason.InvalidAmfUtf8, "Invalid AMF UTF-8.");
            }
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_bytes.Length < count)
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfDecode,
                    ObsRtmpRejectionReason.TruncatedAmfValue, "Truncated AMF0 value.");
            }

            var result = _bytes[..count];
            _bytes = _bytes[count..];
            return result;
        }
    }
}
