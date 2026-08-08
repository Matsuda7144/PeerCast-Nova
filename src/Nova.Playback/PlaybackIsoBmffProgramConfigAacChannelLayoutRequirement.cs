using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
{
    Projected,
    MissingPreparation,
    PreparationUnavailable,
    ChannelCountMismatch,
    InvalidChannelMask,
}

public sealed class PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement
{
    public const string InitializationSymbol = "av_channel_layout_from_mask";

    internal PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement(
        PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation preparation,
        ulong channelMask,
        int channelCount)
    {
        Preparation = preparation;
        ChannelMask = channelMask;
        ChannelCount = channelCount;
    }

    public PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation Preparation
    {
        get;
    }

    public ulong ChannelMask { get; }

    public int ChannelCount { get; }
}

public static class
    PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementProjector
{
    public static PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
        Project(
            PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation?
                preparation,
            out PlaybackIsoBmffProgramConfigAacChannelLayoutRequirement?
                requirement)
    {
        requirement = null;
        if (preparation is null)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .MissingPreparation;
        }

        var decoderPlan = preparation.DecoderPreparation.CapabilityBinding.Plan;
        if (decoderPlan.ManagedParameters.Lease.IsDisposed)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .PreparationUnavailable;
        }

        var reorderPlan = preparation.ReorderPlan;
        var channelCount = reorderPlan.Channels.Count;
        if (channelCount != decoderPlan.ChannelCount)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .ChannelCountMismatch;
        }

        var channelMask = (ulong)reorderPlan.Layout.ChannelMask;
        if (channelMask == 0 ||
            BitOperations.PopCount(channelMask) != channelCount)
        {
            return PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
                .InvalidChannelMask;
        }

        requirement = new(preparation, channelMask, channelCount);
        return PlaybackIsoBmffProgramConfigAacChannelLayoutRequirementResult
            .Projected;
    }
}
