namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
{
    Planned,
    MissingLayout,
    EmptyLayout,
    InconsistentLayout,
}

public sealed record PlaybackIsoBmffProgramConfigChannelReorder(
    int DestinationChannelIndex,
    int SourceChannelIndex,
    PlaybackIsoBmffProgramConfigWaveFormatChannelBinding LayoutBinding);

public sealed class PlaybackIsoBmffProgramConfigChannelReorderPlan
{
    internal PlaybackIsoBmffProgramConfigChannelReorderPlan(
        PlaybackIsoBmffProgramConfigWaveFormatChannelLayout layout,
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelReorder> channels,
        bool requiresReordering)
    {
        Layout = layout;
        Channels = channels;
        RequiresReordering = requiresReordering;
    }

    public PlaybackIsoBmffProgramConfigWaveFormatChannelLayout Layout { get; }

    public IReadOnlyList<PlaybackIsoBmffProgramConfigChannelReorder> Channels
    {
        get;
    }

    public bool RequiresReordering { get; }
}

public static class PlaybackIsoBmffProgramConfigChannelReorderPlanner
{
    public static PlaybackIsoBmffProgramConfigChannelReorderPlanningResult Plan(
        PlaybackIsoBmffProgramConfigWaveFormatChannelLayout? layout,
        out PlaybackIsoBmffProgramConfigChannelReorderPlan? plan)
    {
        plan = null;
        if (layout is null)
        {
            return PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                .MissingLayout;
        }

        var sourceBindings = layout.Assignment.Bindings;
        if (sourceBindings.Count == 0 || layout.Bindings.Count == 0)
        {
            return PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                .EmptyLayout;
        }

        if (layout.Bindings.Count != sourceBindings.Count)
        {
            return PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                .InconsistentLayout;
        }

        var seenSources = new bool[sourceBindings.Count];
        var channels = new PlaybackIsoBmffProgramConfigChannelReorder[
            layout.Bindings.Count];
        var requiresReordering = false;
        for (var destinationIndex = 0;
            destinationIndex < layout.Bindings.Count;
            destinationIndex++)
        {
            var binding = layout.Bindings[destinationIndex];
            var sourceIndex = binding.SourceChannelIndex;
            if (binding.PlatformChannelIndex != destinationIndex ||
                sourceIndex < 0 || sourceIndex >= sourceBindings.Count ||
                seenSources[sourceIndex] ||
                !ReferenceEquals(
                    binding.SourceBinding, sourceBindings[sourceIndex]))
            {
                return PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                    .InconsistentLayout;
            }

            seenSources[sourceIndex] = true;
            requiresReordering |= sourceIndex != destinationIndex;
            channels[destinationIndex] = new(
                destinationIndex, sourceIndex, binding);
        }

        if (seenSources.Any(seen => !seen))
        {
            return PlaybackIsoBmffProgramConfigChannelReorderPlanningResult
                .InconsistentLayout;
        }

        plan = new(layout, Array.AsReadOnly(channels), requiresReordering);
        return PlaybackIsoBmffProgramConfigChannelReorderPlanningResult.Planned;
    }
}
