namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigChannelRegion
{
    Front,
    Side,
    Back,
    Lfe,
}

public enum PlaybackIsoBmffProgramConfigChannelComponent
{
    SingleChannel,
    PairFirst,
    PairSecond,
    Lfe,
}

public enum PlaybackIsoBmffProgramConfigChannelSlotMappingResult
{
    Mapped,
    MissingTopology,
    InconsistentTopology,
}

public sealed record PlaybackIsoBmffProgramConfigChannelSlot(
    int ChannelIndex,
    PlaybackIsoBmffProgramConfigChannelRegion Region,
    int ElementIndex,
    byte ElementTag,
    PlaybackIsoBmffProgramConfigChannelComponent Component);

public sealed class PlaybackIsoBmffProgramConfigChannelSlotMap
{
    internal PlaybackIsoBmffProgramConfigChannelSlotMap(
        PlaybackIsoBmffProgramConfigChannelTopology topology,
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelSlot> slots)
    {
        Topology = topology;
        Slots = slots;
    }

    public PlaybackIsoBmffProgramConfigChannelTopology Topology { get; }

    public IReadOnlyList<PlaybackIsoBmffProgramConfigChannelSlot> Slots { get; }
}

public static class PlaybackIsoBmffProgramConfigChannelSlotMapper
{
    public static PlaybackIsoBmffProgramConfigChannelSlotMappingResult Map(
        PlaybackIsoBmffProgramConfigChannelTopology? topology,
        out PlaybackIsoBmffProgramConfigChannelSlotMap? map)
    {
        map = null;
        if (topology is null)
        {
            return PlaybackIsoBmffProgramConfigChannelSlotMappingResult
                .MissingTopology;
        }

        var slots = new List<PlaybackIsoBmffProgramConfigChannelSlot>(
            topology.TotalChannelCount);
        var element = topology.ProgramConfigElement;
        AddChannels(
            element.FrontChannelElements,
            PlaybackIsoBmffProgramConfigChannelRegion.Front,
            slots);
        AddChannels(
            element.SideChannelElements,
            PlaybackIsoBmffProgramConfigChannelRegion.Side,
            slots);
        AddChannels(
            element.BackChannelElements,
            PlaybackIsoBmffProgramConfigChannelRegion.Back,
            slots);
        for (var index = 0; index < element.LfeChannelElementTags.Count; index++)
        {
            slots.Add(new(
                slots.Count,
                PlaybackIsoBmffProgramConfigChannelRegion.Lfe,
                index,
                element.LfeChannelElementTags[index],
                PlaybackIsoBmffProgramConfigChannelComponent.Lfe));
        }

        if (slots.Count != topology.TotalChannelCount)
        {
            return PlaybackIsoBmffProgramConfigChannelSlotMappingResult
                .InconsistentTopology;
        }

        map = new(topology, slots.AsReadOnly());
        return PlaybackIsoBmffProgramConfigChannelSlotMappingResult.Mapped;
    }

    private static void AddChannels(
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> elements,
        PlaybackIsoBmffProgramConfigChannelRegion region,
        List<PlaybackIsoBmffProgramConfigChannelSlot> slots)
    {
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            if (!element.IsChannelPair)
            {
                slots.Add(new(
                    slots.Count,
                    region,
                    index,
                    element.ElementTag,
                    PlaybackIsoBmffProgramConfigChannelComponent.SingleChannel));
                continue;
            }

            slots.Add(new(
                slots.Count,
                region,
                index,
                element.ElementTag,
                PlaybackIsoBmffProgramConfigChannelComponent.PairFirst));
            slots.Add(new(
                slots.Count,
                region,
                index,
                element.ElementTag,
                PlaybackIsoBmffProgramConfigChannelComponent.PairSecond));
        }
    }
}
