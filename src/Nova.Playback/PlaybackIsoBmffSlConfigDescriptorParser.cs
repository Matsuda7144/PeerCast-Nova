namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSlConfigDescriptorParseResult
{
    Parsed,
    MissingElementaryStreamDescriptor,
    InvalidHierarchy,
    InvalidPayload,
    UnsupportedPredefinedConfiguration,
}

public sealed class PlaybackIsoBmffSlConfigDescriptor
{
    internal PlaybackIsoBmffSlConfigDescriptor(
        PlaybackIsoBmffElementaryStreamDescriptor elementaryStream,
        byte predefined)
    {
        ElementaryStream = elementaryStream;
        Predefined = predefined;
    }

    public PlaybackIsoBmffElementaryStreamDescriptor ElementaryStream
    {
        get;
    }

    public byte Predefined { get; }
}

public static class PlaybackIsoBmffSlConfigDescriptorParser
{
    private const byte Mp4PredefinedConfiguration = 2;

    public static PlaybackIsoBmffSlConfigDescriptorParseResult Parse(
        PlaybackIsoBmffElementaryStreamDescriptor? elementaryStream,
        out PlaybackIsoBmffSlConfigDescriptor? slConfig)
    {
        slConfig = null;
        if (elementaryStream is null)
        {
            return PlaybackIsoBmffSlConfigDescriptorParseResult
                .MissingElementaryStreamDescriptor;
        }

        if (!elementaryStream.TryGetPayload(
                elementaryStream.SlConfigDescriptor,
                out var payloadMemory))
        {
            return PlaybackIsoBmffSlConfigDescriptorParseResult
                .InvalidHierarchy;
        }

        var payload = payloadMemory.Span;
        if (payload.Length != 1)
        {
            return PlaybackIsoBmffSlConfigDescriptorParseResult.InvalidPayload;
        }

        if (payload[0] != Mp4PredefinedConfiguration)
        {
            return PlaybackIsoBmffSlConfigDescriptorParseResult
                .UnsupportedPredefinedConfiguration;
        }

        slConfig = new(elementaryStream, payload[0]);
        return PlaybackIsoBmffSlConfigDescriptorParseResult.Parsed;
    }
}
