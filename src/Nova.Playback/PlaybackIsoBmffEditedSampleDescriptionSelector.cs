namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleEntryCodecCandidate
{
    Avc1,
    Avc3,
    Mp4a,
}

public enum PlaybackIsoBmffEditedSampleDescriptionSelectionResult
{
    Selected,
    MissingEditedRange,
    MissingSampleDescription,
    MissingHandler,
    SampleDescriptionIndexOutOfRange,
    UnsupportedSampleEntry,
    HandlerMismatch,
}

public sealed class PlaybackIsoBmffEditedSampleDescriptionSelection
{
    internal PlaybackIsoBmffEditedSampleDescriptionSelection(
        uint sampleDescriptionIndex,
        PlaybackIsoBmffBoxLocation entry,
        PlaybackIsoBmffSampleEntryCodecCandidate codecCandidate,
        PlaybackIsoBmffHandlerKind mediaKind)
    {
        SampleDescriptionIndex = sampleDescriptionIndex;
        Entry = entry;
        CodecCandidate = codecCandidate;
        MediaKind = mediaKind;
    }

    public uint SampleDescriptionIndex { get; }

    public PlaybackIsoBmffBoxLocation Entry { get; }

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate { get; }

    public PlaybackIsoBmffHandlerKind MediaKind { get; }
}

public static class PlaybackIsoBmffEditedSampleDescriptionSelector
{
    private const uint Avc1 = 0x61766331;
    private const uint Avc3 = 0x61766333;
    private const uint Mp4a = 0x6D703461;

    public static PlaybackIsoBmffEditedSampleDescriptionSelectionResult Select(
        PlaybackIsoBmffEditedSampleByteRange? editedRange,
        PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
        PlaybackIsoBmffHandlerReferenceBox? handler,
        out PlaybackIsoBmffEditedSampleDescriptionSelection? selection)
    {
        selection = null;
        if (editedRange is null)
        {
            return PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .MissingEditedRange;
        }

        if (sampleDescription is null)
        {
            return PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .MissingSampleDescription;
        }

        if (handler is null)
        {
            return PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .MissingHandler;
        }

        var descriptionIndex = editedRange.SampleDescriptionIndex;
        if (descriptionIndex == 0 ||
            descriptionIndex > sampleDescription.Entries.Count)
        {
            return PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .SampleDescriptionIndexOutOfRange;
        }

        var entry = sampleDescription.Entries[checked((int)descriptionIndex - 1)];
        var (codecCandidate, expectedHandler) = entry.Header.Type switch
        {
            Avc1 => (PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
                PlaybackIsoBmffHandlerKind.Video),
            Avc3 => (PlaybackIsoBmffSampleEntryCodecCandidate.Avc3,
                PlaybackIsoBmffHandlerKind.Video),
            Mp4a => (PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
                PlaybackIsoBmffHandlerKind.Audio),
            _ => ((PlaybackIsoBmffSampleEntryCodecCandidate?)null,
                PlaybackIsoBmffHandlerKind.Other),
        };
        if (codecCandidate is null)
        {
            return PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .UnsupportedSampleEntry;
        }

        if (handler.Kind != expectedHandler)
        {
            return PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .HandlerMismatch;
        }

        selection = new(
            descriptionIndex,
            entry,
            codecCandidate.Value,
            expectedHandler);
        return PlaybackIsoBmffEditedSampleDescriptionSelectionResult.Selected;
    }
}
