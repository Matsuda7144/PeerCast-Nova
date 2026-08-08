namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigSpeakerPosition
{
    FrontLeft,
    FrontRight,
    FrontCenter,
    LowFrequency,
    SideLeft,
    SideRight,
    BackLeft,
    BackRight,
    BackCenter,
}

public enum PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
{
    Assigned,
    MissingSlotMap,
    MissingPositions,
    PositionCountMismatch,
    UndefinedPosition,
    DuplicatePosition,
    LowFrequencyMismatch,
}

public sealed record PlaybackIsoBmffProgramConfigSpeakerPositionBinding(
    PlaybackIsoBmffProgramConfigChannelSlot Slot,
    PlaybackIsoBmffProgramConfigSpeakerPosition Position);

public sealed class PlaybackIsoBmffProgramConfigSpeakerPositionAssignment
{
    internal PlaybackIsoBmffProgramConfigSpeakerPositionAssignment(
        PlaybackIsoBmffProgramConfigChannelSlotMap slotMap,
        IReadOnlyList<PlaybackIsoBmffProgramConfigSpeakerPositionBinding> bindings)
    {
        SlotMap = slotMap;
        Bindings = bindings;
    }

    public PlaybackIsoBmffProgramConfigChannelSlotMap SlotMap { get; }

    public IReadOnlyList<PlaybackIsoBmffProgramConfigSpeakerPositionBinding>
        Bindings
    {
        get;
    }
}

public static class PlaybackIsoBmffProgramConfigSpeakerPositionAssigner
{
    public static PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
        Assign(
            PlaybackIsoBmffProgramConfigChannelSlotMap? slotMap,
            IReadOnlyList<PlaybackIsoBmffProgramConfigSpeakerPosition>? positions,
            out PlaybackIsoBmffProgramConfigSpeakerPositionAssignment? assignment)
    {
        assignment = null;
        if (slotMap is null)
        {
            return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .MissingSlotMap;
        }

        if (positions is null)
        {
            return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .MissingPositions;
        }

        if (positions.Count != slotMap.Slots.Count)
        {
            return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                .PositionCountMismatch;
        }

        var seen = new HashSet<PlaybackIsoBmffProgramConfigSpeakerPosition>();
        var bindings =
            new PlaybackIsoBmffProgramConfigSpeakerPositionBinding[positions.Count];
        for (var index = 0; index < positions.Count; index++)
        {
            var position = positions[index];
            if (!Enum.IsDefined(position))
            {
                return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                    .UndefinedPosition;
            }

            if (!seen.Add(position))
            {
                return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                    .DuplicatePosition;
            }

            var slot = slotMap.Slots[index];
            var slotIsLowFrequency =
                slot.Component == PlaybackIsoBmffProgramConfigChannelComponent.Lfe;
            var positionIsLowFrequency =
                position == PlaybackIsoBmffProgramConfigSpeakerPosition.LowFrequency;
            if (slotIsLowFrequency != positionIsLowFrequency)
            {
                return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult
                    .LowFrequencyMismatch;
            }

            bindings[index] = new(slot, position);
        }

        assignment = new(slotMap, Array.AsReadOnly(bindings));
        return PlaybackIsoBmffProgramConfigSpeakerPositionAssignmentResult.Assigned;
    }
}
