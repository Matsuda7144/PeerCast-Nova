using System.Buffers.Binary;

namespace PeerCast.Nova.MediaEncoding;

/// <summary>Encoder-derived AVC configuration, normalized to four-byte NAL lengths.</summary>
public sealed class H264EncoderCodecConfiguration
{
    private readonly byte[] _record;
    private readonly byte[][] _sequenceParameterSets;
    private readonly byte[][] _pictureParameterSets;

    internal H264EncoderCodecConfiguration(byte[][] sequenceParameterSets,
        byte[][] pictureParameterSets)
    {
        if (sequenceParameterSets.Length is < 1 or > 31 ||
            pictureParameterSets.Length is < 1 or > 255 ||
            sequenceParameterSets.Any(static nal => nal.Length is < 4 or > ushort.MaxValue ||
                (nal[0] & 0x9f) != 7) ||
            pictureParameterSets.Any(static nal => nal.Length is < 2 or > ushort.MaxValue ||
                (nal[0] & 0x9f) != 8))
        {
            throw new InvalidOperationException("Encoder SPS/PPS inventory is invalid.");
        }

        _sequenceParameterSets = sequenceParameterSets.Select(static nal => nal.ToArray()).ToArray();
        _pictureParameterSets = pictureParameterSets.Select(static nal => nal.ToArray()).ToArray();
        Profile = sequenceParameterSets[0][1];
        Compatibility = sequenceParameterSets[0][2];
        Level = sequenceParameterSets[0][3];
        if (Profile != 66 || sequenceParameterSets.Any(nal =>
                nal[1] != Profile || nal[2] != Compatibility || nal[3] != Level))
        {
            throw new InvalidOperationException("The initial H.264 profile requires consistent baseline SPS.");
        }

        using var stream = new MemoryStream();
        stream.Write([1, Profile, Compatibility, Level, 0xff,
            (byte)(0xe0 | sequenceParameterSets.Length)]);
        WriteParameterSets(stream, sequenceParameterSets);
        stream.WriteByte((byte)pictureParameterSets.Length);
        WriteParameterSets(stream, pictureParameterSets);
        _record = stream.ToArray();
    }

    public byte Profile { get; }
    public byte Compatibility { get; }
    public byte Level { get; }
    public int NalLengthSize => (_record[4] & 3) + 1;
    public ReadOnlyMemory<byte> ConfigurationRecord => _record.ToArray();
    public IReadOnlyList<ReadOnlyMemory<byte>> SequenceParameterSets =>
        _sequenceParameterSets.Select(static nal => new ReadOnlyMemory<byte>(nal.ToArray())).ToArray();
    public IReadOnlyList<ReadOnlyMemory<byte>> PictureParameterSets =>
        _pictureParameterSets.Select(static nal => new ReadOnlyMemory<byte>(nal.ToArray())).ToArray();

    internal bool ContainsParameterSet(byte[] nal) =>
        _sequenceParameterSets.Concat(_pictureParameterSets)
            .Any(parameterSet => parameterSet.AsSpan().SequenceEqual(nal));

    private static void WriteParameterSets(Stream stream, byte[][] parameterSets)
    {
        Span<byte> length = stackalloc byte[2];
        foreach (var nal in parameterSets)
        {
            BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)nal.Length));
            stream.Write(length);
            stream.Write(nal);
        }
    }
}

internal sealed class AvcEncoderOutputFraming
{
    private readonly int _nativeLengthSize;

    private AvcEncoderOutputFraming(H264EncoderCodecConfiguration configuration, int nativeLengthSize)
    {
        Configuration = configuration;
        _nativeLengthSize = nativeLengthSize;
    }

    public H264EncoderCodecConfiguration Configuration { get; }
    public string NativePacketFraming => _nativeLengthSize == 0 ? "Annex B" : "AVCC";

    public static AvcEncoderOutputFraming Create(ReadOnlySpan<byte> extradata)
    {
        if (extradata.Length is < 7 or > 1_048_576)
        {
            throw new InvalidOperationException("Encoder AVC extradata is missing or too large.");
        }

        if (extradata[0] != 1)
        {
            var nals = SplitAnnexB(extradata);
            if (nals.Any(static nal => (nal[0] & 0x1f) is not (7 or 8)))
            {
                throw new InvalidOperationException("Encoder AVC extradata contains unsupported NALs.");
            }

            return new(new(
                nals.Where(static nal => (nal[0] & 0x1f) == 7).ToArray(),
                nals.Where(static nal => (nal[0] & 0x1f) == 8).ToArray()), 0);
        }

        if ((extradata[4] & 0xfc) != 0xfc || (extradata[5] & 0xe0) != 0xe0)
        {
            throw new InvalidOperationException("Encoder avcC reserved fields are invalid.");
        }

        var lengthSize = (extradata[4] & 3) + 1;
        if (lengthSize == 3)
        {
            throw new InvalidOperationException("Encoder avcC uses the reserved NAL length size.");
        }

        var cursor = 6;
        var sps = ReadParameterSets(extradata, ref cursor, extradata[5] & 31);
        if (cursor >= extradata.Length)
        {
            throw new InvalidOperationException("Encoder avcC PPS inventory is truncated.");
        }

        var pps = ReadParameterSets(extradata, ref cursor, extradata[cursor++]);
        if (cursor != extradata.Length)
        {
            throw new InvalidOperationException("Baseline encoder avcC has trailing bytes.");
        }

        var configuration = new H264EncoderCodecConfiguration(sps, pps);
        if (configuration.Profile != extradata[1] ||
            configuration.Compatibility != extradata[2] || configuration.Level != extradata[3])
        {
            throw new InvalidOperationException("Encoder avcC disagrees with its SPS.");
        }

        return new(configuration, lengthSize);
    }

    public byte[] NormalizePacket(ReadOnlySpan<byte> payload, bool isKeyFrame)
    {
        var nals = _nativeLengthSize == 0
            ? SplitAnnexB(payload)
            : SplitLengthPrefixed(payload, _nativeLengthSize);
        if (!nals.Any(static nal => (nal[0] & 0x1f) is 1 or 5) ||
            nals.Any(static nal => (nal[0] & 0x80) != 0 || (nal[0] & 0x1f) is 0 or > 23) ||
            isKeyFrame != nals.Any(static nal => (nal[0] & 0x1f) == 5))
        {
            throw new InvalidOperationException("Encoder AVC access unit/keyframe metadata is invalid.");
        }

        using var stream = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var nal in nals)
        {
            if ((nal[0] & 0x1f) is 7 or 8)
            {
                if (!Configuration.ContainsParameterSet(nal))
                {
                    throw new InvalidOperationException("Encoder changed AVC configuration mid-stream.");
                }

                // avc1 keeps matching parameter sets in the initialization segment.
                continue;
            }

            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)nal.Length));
            stream.Write(length);
            stream.Write(nal);
        }

        return stream.ToArray();
    }

    private static byte[][] ReadParameterSets(ReadOnlySpan<byte> data, ref int cursor, int count)
    {
        var result = new byte[count][];
        for (var index = 0; index < count; index++)
        {
            if (data.Length - cursor < 2)
            {
                throw new InvalidOperationException("Encoder avcC parameter-set length is truncated.");
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[cursor..]);
            cursor += 2;
            if (length == 0 || length > data.Length - cursor)
            {
                throw new InvalidOperationException("Encoder avcC parameter set is truncated.");
            }

            result[index] = data.Slice(cursor, length).ToArray();
            cursor += length;
        }

        return result;
    }

    private static byte[][] SplitLengthPrefixed(ReadOnlySpan<byte> data, int lengthSize)
    {
        var nals = new List<byte[]>();
        for (var cursor = 0; cursor < data.Length;)
        {
            if (data.Length - cursor < lengthSize)
            {
                throw new InvalidOperationException("Encoder AVC NAL length is truncated.");
            }

            uint length = 0;
            for (var index = 0; index < lengthSize; index++)
            {
                length = (length << 8) | data[cursor++];
            }

            if (length == 0 || length > data.Length - cursor)
            {
                throw new InvalidOperationException("Encoder AVC NAL payload is truncated.");
            }

            nals.Add(data.Slice(cursor, checked((int)length)).ToArray());
            cursor += checked((int)length);
        }

        return nals.ToArray();
    }

    private static byte[][] SplitAnnexB(ReadOnlySpan<byte> data)
    {
        var nals = new List<byte[]>();
        var prefix = FindStartCode(data, 0, out var prefixLength);
        if (prefix < 0 || data[..prefix].ContainsAnyExcept((byte)0))
        {
            throw new InvalidOperationException("Encoder Annex B start code is missing.");
        }

        while (prefix >= 0)
        {
            var start = prefix + prefixLength;
            var next = FindStartCode(data, start, out var nextLength);
            var end = next < 0 ? data.Length : next;
            while (end > start && data[end - 1] == 0)
            {
                end--;
            }

            if (start == end)
            {
                throw new InvalidOperationException("Encoder Annex B contains an empty NAL.");
            }

            nals.Add(data[start..end].ToArray());
            prefix = next;
            prefixLength = nextLength;
        }

        return nals.ToArray();
    }

    private static int FindStartCode(ReadOnlySpan<byte> data, int start, out int length)
    {
        for (var index = start; index <= data.Length - 3; index++)
        {
            if (data[index] == 0 && data[index + 1] == 0)
            {
                if (data[index + 2] == 1)
                {
                    length = 3;
                    return index;
                }

                if (index <= data.Length - 4 && data[index + 2] == 0 && data[index + 3] == 1)
                {
                    length = 4;
                    return index;
                }
            }
        }

        length = 0;
        return -1;
    }
}
