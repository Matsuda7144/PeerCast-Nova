namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextPlaybackCommandPresentation(
    bool IsPlayEnabled,
    bool IsStopEnabled)
{
    public static GenesisGuiNextPlaybackCommandPresentation Resolve(
        GenesisMainViewModel viewModel,
        GenesisGuiNextTab selectedTab,
        GenesisGuiNextNativeTabRow? selectedRow,
        GenesisGuiNextRecordingPageMode recordingMode = GenesisGuiNextRecordingPageMode.ActiveRecordings)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var canPlaySelectedChannel =
            selectedTab is not (GenesisGuiNextTab.Playing or GenesisGuiNextTab.Recording) &&
            viewModel.CanPlay &&
            selectedRow?.IsInteractive == true;
        var history = selectedTab == GenesisGuiNextTab.Recording &&
            recordingMode == GenesisGuiNextRecordingPageMode.History;
        return new(canPlaySelectedChannel, viewModel.CanStop && !history && selectedRow?.RecordingHistory is null);
    }
}

internal static class GenesisGuiNextPlaybackCommandDispatch
{
    public static ValueTask<bool> ActivateSelectedChannelAsync(
        GenesisMainViewModel viewModel,
        GenesisMainWindowInteraction interaction,
        GenesisGuiNextTab selectedTab,
        GenesisGuiNextNativeTabRow? selectedRow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        var presentation = GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            selectedTab,
            selectedRow);
        if (!presentation.IsPlayEnabled ||
            selectedRow?.InteractiveChannel is not { } channel)
        {
            return ValueTask.FromResult(false);
        }

        return interaction.ActivateChannelAsync(channel, cancellationToken);
    }

    public static ValueTask StopAsync(
        GenesisMainViewModel viewModel,
        GenesisMainWindowInteraction interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        return !interaction.IsRecordingHistoryView && viewModel.CanStop
            ? interaction.StopAsync(cancellationToken)
            : ValueTask.CompletedTask;
    }
}
