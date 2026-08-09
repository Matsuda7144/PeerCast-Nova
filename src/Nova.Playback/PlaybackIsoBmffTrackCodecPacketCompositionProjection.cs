using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
{
    Composed,
    MissingCodecConfigurationProjection,
    EmptyProjection,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidConfiguration,
    MissingAvcRequirements,
    UnexpectedAvcRequirements,
    MixedCodecKinds,
    UnsupportedCodec,
    AvcFramingFailed,
    AvcClassificationFailed,
    AvcCompositionFailed,
    PacketProjectionFailed,
}

public sealed record
    PlaybackIsoBmffTrackCodecPacketCompositionProjectionOptions
{
    public int MaximumNalUnitCount { get; init; } =
        PlaybackIsoBmffAvcSampleFramingOptions.DefaultMaximumNalUnitCount;

    public int MaximumPayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes;
}

public sealed class PlaybackIsoBmffTrackCodecPacketComposition
{
    internal PlaybackIsoBmffTrackCodecPacketComposition(
        PlaybackIsoBmffTrackCodecConfiguration source,
        PlaybackIsoBmffCodecPacketProjection packet)
    {
        Source = source;
        Packet = packet;
    }

    public PlaybackIsoBmffTrackCodecConfiguration Source { get; }

    public PlaybackIsoBmffCodecPacketProjection Packet { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        Source.CodecCandidate;
}

public sealed class PlaybackIsoBmffTrackCodecPacketCompositionProjection
{
    internal PlaybackIsoBmffTrackCodecPacketCompositionProjection(
        PlaybackIsoBmffTrackCodecConfigurationProjection source,
        PlaybackIsoBmffAvcPayloadRequirements? avcRequirements,
        int maximumNalUnitCount,
        int maximumPayloadBytes,
        ulong totalPayloadBytes,
        ReadOnlyCollection<PlaybackIsoBmffTrackCodecPacketComposition> entries)
    {
        Source = source;
        AvcRequirements = avcRequirements;
        MaximumNalUnitCount = maximumNalUnitCount;
        MaximumPayloadBytes = maximumPayloadBytes;
        TotalPayloadBytes = totalPayloadBytes;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackCodecConfigurationProjection Source { get; }

    public PlaybackIsoBmffAvcPayloadRequirements? AvcRequirements { get; }

    public int MaximumNalUnitCount { get; }

    public int MaximumPayloadBytes { get; }

    public ulong TotalPayloadBytes { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackCodecPacketComposition> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackCodecPacketCompositionProjector
{
    public static
        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult Compose(
            PlaybackIsoBmffTrackCodecConfigurationProjection? source,
            PlaybackIsoBmffAvcPayloadRequirements? avcRequirements,
            out PlaybackIsoBmffTrackCodecPacketCompositionProjection?
                projection,
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionOptions?
                options = null)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .MissingCodecConfigurationProjection;
        }

        if (source.Entries.Count == 0)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .EmptyProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .EntryLimitExceeded;
        }

        options ??=
            new PlaybackIsoBmffTrackCodecPacketCompositionProjectionOptions();
        if (options.MaximumNalUnitCount is < 1 or
                > PlaybackIsoBmffAvcSampleFramingOptions
                    .MaximumNalUnitCountHardLimit ||
            options.MaximumPayloadBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumPacketPayloadBytesHardLimit)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .InvalidConfiguration;
        }

        var firstCodec = source.Entries[0].CodecCandidate;
        var isAvc = firstCodec is PlaybackIsoBmffSampleEntryCodecCandidate.Avc1
            or PlaybackIsoBmffSampleEntryCodecCandidate.Avc3;
        if (isAvc && avcRequirements is null)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .MissingAvcRequirements;
        }

        if (firstCodec == PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a &&
            avcRequirements is not null)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .UnexpectedAvcRequirements;
        }

        PlaybackIsoBmffAvcPayloadRequirementsResolution? resolution = null;
        if (isAvc &&
            PlaybackIsoBmffAvcPayloadRequirementsResolver.Resolve(
                avcRequirements,
                out resolution) !=
            PlaybackIsoBmffAvcPayloadRequirementsResolveResult.Resolved)
        {
            return
                PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                    .MissingAvcRequirements;
        }

        var entries = new PlaybackIsoBmffTrackCodecPacketComposition[
            source.Entries.Count];
        ulong totalPayloadBytes = 0;
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]))
            {
                return
                    PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                        .SourceMismatch;
            }

            var entryIsAvc = entry.CodecCandidate is
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 or
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3;
            if (entryIsAvc != isAvc)
            {
                return
                    PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                        .MixedCodecKinds;
            }

            PlaybackIsoBmffCodecPacketProjection? packet;
            if (entryIsAvc)
            {
                if (entry.AvcConfiguration is null ||
                    entry.ElementaryStreamDescriptor is not null ||
                    !ReferenceEquals(
                        entry.AvcConfiguration.VisualEntry,
                        entry.Source.VisualEntry))
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .SourceMismatch;
                }

                if (PlaybackIsoBmffAvcSampleFraming.Frame(
                        entry.Source.Source.Source.Candidate,
                        entry.AvcConfiguration,
                        out var frame,
                        new()
                        {
                            MaximumNalUnitCount = options.MaximumNalUnitCount,
                        }) != PlaybackIsoBmffAvcSampleFramingResult.Framed)
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .AvcFramingFailed;
                }

                if (PlaybackIsoBmffAvcNalUnitClassifier.Classify(
                        frame,
                        out var classification) !=
                    PlaybackIsoBmffAvcNalUnitClassificationResult.Classified)
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .AvcClassificationFailed;
                }

                if (PlaybackIsoBmffAvcPayloadComposer.Compose(
                        classification,
                        resolution,
                        out var composition,
                        new()
                        {
                            MaximumNalUnitCount = options.MaximumNalUnitCount,
                            MaximumPayloadBytes = options.MaximumPayloadBytes,
                        }) != PlaybackIsoBmffAvcPayloadComposeResult.Composed)
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .AvcCompositionFailed;
                }

                if (PlaybackIsoBmffCodecPacketProjector.ProjectAvc(
                        composition,
                        out packet) !=
                    PlaybackIsoBmffCodecPacketProjectionResult.Projected)
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .PacketProjectionFailed;
                }
            }
            else if (entry.CodecCandidate ==
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a)
            {
                if (entry.ElementaryStreamDescriptor is null ||
                    entry.AvcConfiguration is not null ||
                    !ReferenceEquals(
                        entry.ElementaryStreamDescriptor.AudioEntry,
                        entry.Source.AudioEntry))
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .SourceMismatch;
                }

                if (PlaybackIsoBmffCodecPacketProjector.ProjectAac(
                        entry.Source.Source.Source.Candidate,
                        entry.Source.AudioEntry,
                        out packet) !=
                    PlaybackIsoBmffCodecPacketProjectionResult.Projected)
                {
                    return
                        PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                            .PacketProjectionFailed;
                }
            }
            else
            {
                return
                    PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                        .UnsupportedCodec;
            }

            if (packet is null ||
                !ReferenceEquals(
                    packet.Candidate,
                    entry.Source.Source.Source.Candidate) ||
                packet.Payload.Length > options.MaximumPayloadBytes)
            {
                return
                    PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                        .PacketProjectionFailed;
            }

            totalPayloadBytes += (ulong)packet.Payload.Length;
            entries[index] = new(entry, packet);
        }

        projection = new(
            source,
            avcRequirements,
            options.MaximumNalUnitCount,
            options.MaximumPayloadBytes,
            totalPayloadBytes,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
            .Composed;
    }
}
