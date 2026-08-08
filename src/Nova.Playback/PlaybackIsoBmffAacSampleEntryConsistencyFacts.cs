namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacSampleEntryConsistencyFactsResult
{
    Projected,
    MissingAudioEntry,
    MissingFormatIntent,
    UnsupportedAudioEntry,
}

public sealed class PlaybackIsoBmffAacSampleEntryConsistencyFacts
{
    internal PlaybackIsoBmffAacSampleEntryConsistencyFacts(
        PlaybackIsoBmffAudioSampleEntry audioEntry,
        PlaybackIsoBmffAacDecoderFormatIntent formatIntent)
    {
        AudioEntry = audioEntry;
        FormatIntent = formatIntent;
    }

    public PlaybackIsoBmffAudioSampleEntry AudioEntry { get; }

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent { get; }

    public uint DeclaredSampleRateFixedPoint => AudioEntry.SampleRateFixedPoint;

    public ushort DeclaredChannelCount => AudioEntry.ChannelCount;

    public uint EffectiveSamplingFrequency =>
        FormatIntent.EffectiveSamplingFrequency;

    public int EffectiveChannelCount => FormatIntent.EffectiveChannelCount;

    public bool HasIntegralDeclaredSampleRate =>
        AudioEntry.FractionalSampleRate == 0;

    public bool SamplingFrequencyMatches =>
        HasIntegralDeclaredSampleRate &&
        AudioEntry.IntegerSampleRate == EffectiveSamplingFrequency;

    public bool ChannelCountMatches =>
        AudioEntry.ChannelCount == EffectiveChannelCount;

    public bool IsExactMatch => SamplingFrequencyMatches && ChannelCountMatches;
}

public static class PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector
{
    public static PlaybackIsoBmffAacSampleEntryConsistencyFactsResult Project(
        PlaybackIsoBmffAudioSampleEntry? audioEntry,
        PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
        out PlaybackIsoBmffAacSampleEntryConsistencyFacts? facts)
    {
        facts = null;
        if (audioEntry is null)
        {
            return PlaybackIsoBmffAacSampleEntryConsistencyFactsResult
                .MissingAudioEntry;
        }

        if (formatIntent is null)
        {
            return PlaybackIsoBmffAacSampleEntryConsistencyFactsResult
                .MissingFormatIntent;
        }

        if (audioEntry.Selection.CodecCandidate !=
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a ||
            audioEntry.Selection.MediaKind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return PlaybackIsoBmffAacSampleEntryConsistencyFactsResult
                .UnsupportedAudioEntry;
        }

        facts = new(audioEntry, formatIntent);
        return PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.Projected;
    }
}
