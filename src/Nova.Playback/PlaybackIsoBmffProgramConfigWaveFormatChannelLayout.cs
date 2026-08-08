namespace PeerCast.Nova.Playback;

[Flags]
public enum PlaybackIsoBmffWaveFormatSpeakerMask : uint
{
    None = 0,
    FrontLeft = 0x0001,
    FrontRight = 0x0002,
    FrontCenter = 0x0004,
    LowFrequency = 0x0008,
    BackLeft = 0x0010,
    BackRight = 0x0020,
    BackCenter = 0x0100,
    SideLeft = 0x0200,
    SideRight = 0x0400,
}

public enum PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
{
    Projected,
    MissingAssignment,
    InconsistentAssignment,
    UnsupportedPosition,
}

public sealed record PlaybackIsoBmffProgramConfigWaveFormatChannelBinding(
    int PlatformChannelIndex,
    int SourceChannelIndex,
    PlaybackIsoBmffProgramConfigSpeakerPositionBinding SourceBinding,
    PlaybackIsoBmffWaveFormatSpeakerMask SpeakerMask);

public sealed class PlaybackIsoBmffProgramConfigWaveFormatChannelLayout
{
    internal PlaybackIsoBmffProgramConfigWaveFormatChannelLayout(
        PlaybackIsoBmffProgramConfigSpeakerPositionAssignment assignment,
        PlaybackIsoBmffWaveFormatSpeakerMask channelMask,
        IReadOnlyList<PlaybackIsoBmffProgramConfigWaveFormatChannelBinding>
            bindings)
    {
        Assignment = assignment;
        ChannelMask = channelMask;
        Bindings = bindings;
    }

    public PlaybackIsoBmffProgramConfigSpeakerPositionAssignment Assignment
    {
        get;
    }

    public PlaybackIsoBmffWaveFormatSpeakerMask ChannelMask { get; }

    public IReadOnlyList<PlaybackIsoBmffProgramConfigWaveFormatChannelBinding>
        Bindings
    {
        get;
    }
}

public static class PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutProjector
{
    public static PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
        Project(
            PlaybackIsoBmffProgramConfigSpeakerPositionAssignment? assignment,
            out PlaybackIsoBmffProgramConfigWaveFormatChannelLayout? layout)
    {
        layout = null;
        if (assignment is null)
        {
            return PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                .MissingAssignment;
        }

        var slots = assignment.SlotMap.Slots;
        if (assignment.Bindings.Count != slots.Count)
        {
            return PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                .InconsistentAssignment;
        }

        var projected = new List<(
            int SourceIndex,
            PlaybackIsoBmffProgramConfigSpeakerPositionBinding Binding,
            PlaybackIsoBmffWaveFormatSpeakerMask Mask)>(slots.Count);
        var channelMask = PlaybackIsoBmffWaveFormatSpeakerMask.None;
        for (var index = 0; index < slots.Count; index++)
        {
            var binding = assignment.Bindings[index];
            if (!ReferenceEquals(binding.Slot, slots[index]))
            {
                return PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                    .InconsistentAssignment;
            }

            var mask = ToMask(binding.Position);
            if (mask == PlaybackIsoBmffWaveFormatSpeakerMask.None)
            {
                return PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                    .UnsupportedPosition;
            }

            if ((channelMask & mask) != 0)
            {
                return PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult
                    .InconsistentAssignment;
            }

            channelMask |= mask;
            projected.Add((index, binding, mask));
        }

        var ordered = projected
            .OrderBy(item => (uint)item.Mask)
            .Select((item, platformIndex) =>
                new PlaybackIsoBmffProgramConfigWaveFormatChannelBinding(
                    platformIndex,
                    item.SourceIndex,
                    item.Binding,
                    item.Mask))
            .ToArray();
        layout = new(assignment, channelMask, Array.AsReadOnly(ordered));
        return PlaybackIsoBmffProgramConfigWaveFormatChannelLayoutResult.Projected;
    }

    private static PlaybackIsoBmffWaveFormatSpeakerMask ToMask(
        PlaybackIsoBmffProgramConfigSpeakerPosition position) => position switch
        {
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontLeft =>
                PlaybackIsoBmffWaveFormatSpeakerMask.FrontLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontRight =>
                PlaybackIsoBmffWaveFormatSpeakerMask.FrontRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.FrontCenter =>
                PlaybackIsoBmffWaveFormatSpeakerMask.FrontCenter,
            PlaybackIsoBmffProgramConfigSpeakerPosition.LowFrequency =>
                PlaybackIsoBmffWaveFormatSpeakerMask.LowFrequency,
            PlaybackIsoBmffProgramConfigSpeakerPosition.SideLeft =>
                PlaybackIsoBmffWaveFormatSpeakerMask.SideLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.SideRight =>
                PlaybackIsoBmffWaveFormatSpeakerMask.SideRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackLeft =>
                PlaybackIsoBmffWaveFormatSpeakerMask.BackLeft,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackRight =>
                PlaybackIsoBmffWaveFormatSpeakerMask.BackRight,
            PlaybackIsoBmffProgramConfigSpeakerPosition.BackCenter =>
                PlaybackIsoBmffWaveFormatSpeakerMask.BackCenter,
            _ => PlaybackIsoBmffWaveFormatSpeakerMask.None,
        };
}
