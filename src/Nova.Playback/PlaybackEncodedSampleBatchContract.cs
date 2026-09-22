using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackEncodedMediaRole
{
    Video,
    Audio,
}

public enum PlaybackEncodedCodecKind
{
    Avc,
    Aac,
}

public enum PlaybackEncodedCodecVariant
{
    Avc1,
    Avc3,
    Aac,
}

public readonly record struct PlaybackEncodedTimeBase
{
    public PlaybackEncodedTimeBase(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        Numerator = numerator;
        Denominator = denominator;
    }

    public long Numerator { get; }

    public long Denominator { get; }
}

public sealed class PlaybackEncodedTrackDescription
{
    private readonly ReadOnlyCollection<byte> _codecConfiguration;

    internal PlaybackEncodedTrackDescription(
        uint trackId,
        PlaybackEncodedMediaRole mediaRole,
        PlaybackEncodedCodecKind codecKind,
        PlaybackEncodedCodecVariant codecVariant,
        byte[] codecConfiguration,
        PlaybackEncodedTimeBase timeBase,
        byte? nalLengthSize = null,
        int? width = null,
        int? height = null,
        int? sampleRate = null,
        int? channelCount = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(trackId);

        ArgumentNullException.ThrowIfNull(codecConfiguration);
        if (codecConfiguration.Length == 0)
        {
            throw new ArgumentException(
                "Codec configuration cannot be empty.",
                nameof(codecConfiguration));
        }

        if (!IsValidShape(
                mediaRole,
                codecKind,
                codecVariant,
                nalLengthSize,
                width,
                height,
                sampleRate,
                channelCount))
        {
            throw new ArgumentException("Track description is inconsistent.");
        }

        TrackId = trackId;
        MediaRole = mediaRole;
        CodecKind = codecKind;
        CodecVariant = codecVariant;
        _codecConfiguration = Array.AsReadOnly(codecConfiguration.ToArray());
        TimeBase = timeBase;
        NalLengthSize = nalLengthSize;
        Width = width;
        Height = height;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
    }

    public uint TrackId { get; }

    public PlaybackEncodedMediaRole MediaRole { get; }

    public PlaybackEncodedCodecKind CodecKind { get; }

    public PlaybackEncodedCodecVariant CodecVariant { get; }

    public IReadOnlyList<byte> CodecConfiguration => _codecConfiguration;

    public PlaybackEncodedTimeBase TimeBase { get; }

    public byte? NalLengthSize { get; }

    public int? Width { get; }

    public int? Height { get; }

    public int? SampleRate { get; }

    public int? ChannelCount { get; }

    private static bool IsValidShape(
        PlaybackEncodedMediaRole mediaRole,
        PlaybackEncodedCodecKind codecKind,
        PlaybackEncodedCodecVariant codecVariant,
        byte? nalLengthSize,
        int? width,
        int? height,
        int? sampleRate,
        int? channelCount) =>
        mediaRole switch
        {
            PlaybackEncodedMediaRole.Video =>
                codecKind == PlaybackEncodedCodecKind.Avc &&
                (codecVariant is PlaybackEncodedCodecVariant.Avc1 or
                    PlaybackEncodedCodecVariant.Avc3) &&
                nalLengthSize is >= 1 and <= 4 &&
                width is > 0 && height is > 0 &&
                sampleRate is null && channelCount is null,
            PlaybackEncodedMediaRole.Audio =>
                codecKind == PlaybackEncodedCodecKind.Aac &&
                codecVariant == PlaybackEncodedCodecVariant.Aac &&
                nalLengthSize is null && width is null && height is null &&
                sampleRate is > 0 && channelCount is > 0,
            _ => false,
        };
}

public sealed class PlaybackEncodedStreamDescription
{
    private readonly ReadOnlyCollection<PlaybackEncodedTrackDescription>
        _trackDescriptions;

    internal PlaybackEncodedStreamDescription(
        Guid streamGenerationId,
        PlaybackEncodedTrackDescription[] trackDescriptions)
    {
        if (streamGenerationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Stream generation identity cannot be empty.",
                nameof(streamGenerationId));
        }

        ArgumentNullException.ThrowIfNull(trackDescriptions);
        if (trackDescriptions.Length is < 1 or > 2 ||
            trackDescriptions.Any(static track => track is null) ||
            trackDescriptions.Select(static track => track.TrackId).Distinct()
                .Count() != trackDescriptions.Length)
        {
            throw new ArgumentException(
                "Track descriptions must be non-empty and uniquely identified.",
                nameof(trackDescriptions));
        }

        StreamGenerationId = streamGenerationId;
        _trackDescriptions = Array.AsReadOnly(trackDescriptions.ToArray());
    }

    public Guid StreamGenerationId { get; }

    public IReadOnlyList<PlaybackEncodedTrackDescription> TrackDescriptions =>
        _trackDescriptions;

    internal PlaybackEncodedTrackDescription? FindTrack(uint trackId)
    {
        foreach (var track in _trackDescriptions)
        {
            if (track.TrackId == trackId)
            {
                return track;
            }
        }

        return null;
    }
}
