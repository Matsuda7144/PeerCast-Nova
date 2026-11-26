namespace PeerCast.Nova.App;

internal readonly record struct GenesisGuiNextRecordingCommandPresentation(
    bool IsRecordEnabled,
    bool IsStopRecordingEnabled)
{
    public static GenesisGuiNextRecordingCommandPresentation Resolve(
        GenesisMainViewModel viewModel,
        GenesisGuiNextTab selectedTab,
        GenesisGuiNextNativeTabRow? selectedRow)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (selectedTab is GenesisGuiNextTab.Playing or GenesisGuiNextTab.Recording ||
            selectedRow?.IsInteractive != true || selectedRow.InteractiveChannel is not { } channel)
        {
            return default;
        }

        var presentation = viewModel.GetManualRecordingPresentation(channel);
        return new(presentation.CanRecord, presentation.CanStop);
    }
}

internal static class GenesisGuiNextRecordingCommandDispatch
{
    internal const string AllowAutomaticLabel = "自動録画を再許可";

    public static async ValueTask<bool> AllowRecordingRowAsync(
        GenesisMainViewModel viewModel, GenesisGuiNextNativeTabRow? row, CancellationToken token = default)
    {
        if (row?.Suppression is not { State: GenesisAutomaticRecordingSuppressionState.Committed } suppressed ||
            !viewModel.CanAllowAutomatic(suppressed)) return false;
        await viewModel.AllowAutomaticRecordingAsync(suppressed, token).ConfigureAwait(false);
        return true;
    }

    public static ValueTask<bool> StopRecordingRowAsync(
        GenesisMainViewModel viewModel,
        GenesisGuiNextNativeTabRow? selectedRow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (selectedRow?.Suppression is not null || selectedRow?.ActiveRecording is not { } recording ||
            !viewModel.GetActiveRecordingPresentation(recording).CanStop)
        {
            return ValueTask.FromResult(false);
        }

        return recording.Origin == PeerCast.Nova.Core.Recording.RecordingSessionOrigin.AutomaticRule
            ? StopAutomaticRecordingAsync(viewModel, recording, cancellationToken)
            : StopManualRecordingAsync(viewModel, recording, cancellationToken);
    }

    private static async ValueTask<bool> StopAutomaticRecordingAsync(
        GenesisMainViewModel viewModel, GenesisGuiNextActiveRecording recording, CancellationToken token)
    {
        await viewModel.StopAutomaticRecordingAsync(recording, token).ConfigureAwait(false);
        return true;
    }

    public static ValueTask<bool> StartSelectedAsync(
        GenesisMainViewModel viewModel,
        GenesisGuiNextTab selectedTab,
        GenesisGuiNextNativeTabRow? selectedRow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var presentation = GenesisGuiNextRecordingCommandPresentation.Resolve(
            viewModel,
            selectedTab,
            selectedRow);
        if (!presentation.IsRecordEnabled ||
            selectedRow?.InteractiveChannel is not { } channel)
        {
            return ValueTask.FromResult(false);
        }

        return StartAsync(viewModel, channel, cancellationToken);
    }

    public static ValueTask<bool> StopSelectedAsync(
        GenesisMainViewModel viewModel,
        GenesisGuiNextTab selectedTab,
        GenesisGuiNextNativeTabRow? selectedRow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var presentation = GenesisGuiNextRecordingCommandPresentation.Resolve(
            viewModel,
            selectedTab,
            selectedRow);
        if (!presentation.IsStopRecordingEnabled ||
            selectedRow?.InteractiveChannel is not { } channel)
        {
            return ValueTask.FromResult(false);
        }

        return StopAsync(viewModel, channel, cancellationToken);
    }

    private static async ValueTask<bool> StartAsync(
        GenesisMainViewModel viewModel,
        GenesisChannelItem channel,
        CancellationToken cancellationToken)
    {
        await viewModel.StartManualRecordingAsync(channel, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static async ValueTask<bool> StopAsync(
        GenesisMainViewModel viewModel,
        GenesisChannelItem channel,
        CancellationToken cancellationToken)
    {
        await viewModel.StopManualRecordingAsync(channel, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static async ValueTask<bool> StopManualRecordingAsync(
        GenesisMainViewModel viewModel,
        GenesisGuiNextActiveRecording recording,
        CancellationToken cancellationToken)
    {
        await viewModel.StopManualRecordingAsync(
                recording.ChannelId,
                recording.SessionId,
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
