namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigChannelTopologyResult
{
    Projected,
    MissingProgramConfigElement,
    DuplicateChannelElementIdentity,
    DuplicateLfeElementTag,
    DuplicateAssociatedDataElementTag,
    DuplicateCouplingElementTag,
}

public sealed class PlaybackIsoBmffProgramConfigChannelTopology
{
    internal PlaybackIsoBmffProgramConfigChannelTopology(
        PlaybackIsoBmffProgramConfigElement programConfigElement,
        int singleChannelElementCount,
        int channelPairElementCount,
        int frontChannelCount,
        int sideChannelCount,
        int backChannelCount,
        int lfeChannelCount)
    {
        ProgramConfigElement = programConfigElement;
        SingleChannelElementCount = singleChannelElementCount;
        ChannelPairElementCount = channelPairElementCount;
        FrontChannelCount = frontChannelCount;
        SideChannelCount = sideChannelCount;
        BackChannelCount = backChannelCount;
        LfeChannelCount = lfeChannelCount;
    }

    public PlaybackIsoBmffProgramConfigElement ProgramConfigElement { get; }
    public int SingleChannelElementCount { get; }
    public int ChannelPairElementCount { get; }
    public int FrontChannelCount { get; }
    public int SideChannelCount { get; }
    public int BackChannelCount { get; }
    public int LfeChannelCount { get; }
    public int CoreChannelCount =>
        FrontChannelCount + SideChannelCount + BackChannelCount;
    public int TotalChannelCount => CoreChannelCount + LfeChannelCount;
}

public static class PlaybackIsoBmffProgramConfigChannelTopologyProjector
{
    public static PlaybackIsoBmffProgramConfigChannelTopologyResult Project(
        PlaybackIsoBmffProgramConfigElement? programConfigElement,
        out PlaybackIsoBmffProgramConfigChannelTopology? topology)
    {
        topology = null;
        if (programConfigElement is null)
        {
            return PlaybackIsoBmffProgramConfigChannelTopologyResult
                .MissingProgramConfigElement;
        }

        var channelIdentities = new HashSet<(bool IsPair, byte Tag)>();
        var singleCount = 0;
        var pairCount = 0;
        if (!TryProjectChannels(
                programConfigElement.FrontChannelElements,
                channelIdentities,
                ref singleCount,
                ref pairCount,
                out var frontCount) ||
            !TryProjectChannels(
                programConfigElement.SideChannelElements,
                channelIdentities,
                ref singleCount,
                ref pairCount,
                out var sideCount) ||
            !TryProjectChannels(
                programConfigElement.BackChannelElements,
                channelIdentities,
                ref singleCount,
                ref pairCount,
                out var backCount))
        {
            return PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateChannelElementIdentity;
        }

        if (HasDuplicate(programConfigElement.LfeChannelElementTags))
        {
            return PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateLfeElementTag;
        }

        if (HasDuplicate(programConfigElement.AssociatedDataElementTags))
        {
            return PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateAssociatedDataElementTag;
        }

        if (HasDuplicate(programConfigElement.ValidCcElements.Select(
                element => element.ElementTag)))
        {
            return PlaybackIsoBmffProgramConfigChannelTopologyResult
                .DuplicateCouplingElementTag;
        }

        topology = new(
            programConfigElement,
            singleCount,
            pairCount,
            frontCount,
            sideCount,
            backCount,
            programConfigElement.LfeChannelElementTags.Count);
        return PlaybackIsoBmffProgramConfigChannelTopologyResult.Projected;
    }

    private static bool TryProjectChannels(
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> elements,
        HashSet<(bool IsPair, byte Tag)> identities,
        ref int singleCount,
        ref int pairCount,
        out int channelCount)
    {
        channelCount = 0;
        foreach (var element in elements)
        {
            if (!identities.Add((element.IsChannelPair, element.ElementTag)))
            {
                return false;
            }

            if (element.IsChannelPair)
            {
                pairCount++;
                channelCount += 2;
            }
            else
            {
                singleCount++;
                channelCount++;
            }
        }

        return true;
    }

    private static bool HasDuplicate(IEnumerable<byte> tags)
    {
        var unique = new HashSet<byte>();
        return tags.Any(tag => !unique.Add(tag));
    }
}
