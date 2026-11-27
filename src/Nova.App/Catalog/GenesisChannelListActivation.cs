namespace PeerCast.Nova.App;

internal static class GenesisChannelListActivation
{
    internal static ValueTask<bool> ActivateAsync(
        int notification, int item, IReadOnlyList<GenesisGuiNextNativeTabRow> rows,
        GenesisMainWindowInteraction interaction, bool available)
    {
        if (notification != -3 || !available || item < 0 || item >= rows.Count ||
            !rows[item].IsInteractive || rows[item].InteractiveChannel is not { } rendered ||
            string.IsNullOrWhiteSpace(rendered.Source))
            return ValueTask.FromResult(false);

        // Resolve against the current catalog too: a rendered row may outlive a
        // refresh. Never fall back to the managed selection or an old row index.
        var current = interaction.ViewModel.Channels.FirstOrDefault(channel =>
            string.Equals(channel.Source, rendered.Source, StringComparison.Ordinal));
        return current is null || !GenesisChannelIdentity.Matches(current, rendered)
            ? ValueTask.FromResult(false) : interaction.ActivateChannelForDoubleClickAsync(current);
    }
}
