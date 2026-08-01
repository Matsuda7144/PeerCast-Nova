using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFileTypeBoxParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    InvalidPayload,
    BrandLimitExceeded,
}

public sealed record PlaybackIsoBmffFileTypeBoxParserOptions
{
    public const int DefaultMaximumCompatibleBrands = 32;
    public const int MaximumCompatibleBrandsHardLimit = 256;

    public int MaximumCompatibleBrands { get; init; } =
        DefaultMaximumCompatibleBrands;
}

public sealed class PlaybackIsoBmffFileTypeBox
{
    internal PlaybackIsoBmffFileTypeBox(
        uint majorBrand,
        uint minorVersion,
        ReadOnlyCollection<uint> compatibleBrands)
    {
        MajorBrand = majorBrand;
        MinorVersion = minorVersion;
        CompatibleBrands = compatibleBrands;
    }

    public uint MajorBrand { get; }

    public uint MinorVersion { get; }

    public IReadOnlyList<uint> CompatibleBrands { get; }
}

public static class PlaybackIsoBmffFileTypeBoxParser
{
    private const uint FileTypeBoxType = 0x66747970;
    private const int FixedPayloadSize = 8;
    private const int BrandSize = 4;

    public static PlaybackIsoBmffFileTypeBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffFileTypeBox? fileType,
        PlaybackIsoBmffFileTypeBoxParserOptions? options = null)
    {
        fileType = null;
        options ??= new PlaybackIsoBmffFileTypeBoxParserOptions();
        if (options.MaximumCompatibleBrands is < 0 or
            > PlaybackIsoBmffFileTypeBoxParserOptions.MaximumCompatibleBrandsHardLimit)
        {
            return PlaybackIsoBmffFileTypeBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffFileTypeBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffFileTypeBoxParseResult.InvalidPayload;
        }

        if (header.Type != FileTypeBoxType)
        {
            return PlaybackIsoBmffFileTypeBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffFileTypeBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffFileTypeBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            (payload.Length - FixedPayloadSize) % BrandSize != 0)
        {
            return PlaybackIsoBmffFileTypeBoxParseResult.InvalidPayload;
        }

        var brandCount = (payload.Length - FixedPayloadSize) / BrandSize;
        if (brandCount > options.MaximumCompatibleBrands)
        {
            return PlaybackIsoBmffFileTypeBoxParseResult.BrandLimitExceeded;
        }

        var brands = new uint[brandCount];
        for (var index = 0; index < brands.Length; index++)
        {
            brands[index] = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(FixedPayloadSize + (index * BrandSize), BrandSize));
        }

        fileType = new(
            BinaryPrimitives.ReadUInt32BigEndian(payload[..BrandSize]),
            BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(BrandSize, BrandSize)),
            Array.AsReadOnly(brands));
        return PlaybackIsoBmffFileTypeBoxParseResult.Parsed;
    }
}
