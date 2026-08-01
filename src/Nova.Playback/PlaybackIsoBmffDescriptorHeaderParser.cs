namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffDescriptorHeaderParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    InvalidLengthEncoding,
    PayloadLimitExceeded,
    TrailingData,
}

public sealed record PlaybackIsoBmffDescriptorHeaderParserOptions
{
    public int MaximumPayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffDescriptorHeader
{
    internal PlaybackIsoBmffDescriptorHeader(
        byte tag,
        int headerLength,
        int payloadLength)
    {
        Tag = tag;
        HeaderLength = headerLength;
        PayloadLength = payloadLength;
    }

    public byte Tag { get; }

    public int HeaderLength { get; }

    public int PayloadLength { get; }

    public int TotalLength => checked(HeaderLength + PayloadLength);
}

public static class PlaybackIsoBmffDescriptorHeaderParser
{
    public static PlaybackIsoBmffDescriptorHeaderParseResult Parse(
        ReadOnlySpan<byte> descriptor,
        out PlaybackIsoBmffDescriptorHeader? header,
        PlaybackIsoBmffDescriptorHeaderParserOptions? options = null,
        bool requireExactLength = true)
    {
        header = null;
        options ??= new PlaybackIsoBmffDescriptorHeaderParserOptions();
        if (options.MaximumPayloadBytes is < 0 or
            > PlaybackBoundedDemuxOptions.MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffDescriptorHeaderParseResult
                .InvalidConfiguration;
        }

        if (descriptor.Length < 2)
        {
            return PlaybackIsoBmffDescriptorHeaderParseResult.NeedMoreData;
        }

        var payloadLength = 0;
        var lengthByteCount = 0;
        var terminated = false;
        for (var index = 1; index <= 4; index++)
        {
            if (index >= descriptor.Length)
            {
                return PlaybackIsoBmffDescriptorHeaderParseResult.NeedMoreData;
            }

            var current = descriptor[index];
            payloadLength = checked((payloadLength << 7) | (current & 0x7F));
            lengthByteCount++;
            if ((current & 0x80) == 0)
            {
                terminated = true;
                break;
            }
        }

        if (!terminated)
        {
            return PlaybackIsoBmffDescriptorHeaderParseResult
                .InvalidLengthEncoding;
        }

        if (payloadLength > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffDescriptorHeaderParseResult
                .PayloadLimitExceeded;
        }

        var headerLength = 1 + lengthByteCount;
        var totalLength = headerLength + payloadLength;
        if (descriptor.Length < totalLength)
        {
            return PlaybackIsoBmffDescriptorHeaderParseResult.NeedMoreData;
        }

        if (requireExactLength && descriptor.Length != totalLength)
        {
            return PlaybackIsoBmffDescriptorHeaderParseResult.TrailingData;
        }

        header = new(descriptor[0], headerLength, payloadLength);
        return PlaybackIsoBmffDescriptorHeaderParseResult.Parsed;
    }
}
