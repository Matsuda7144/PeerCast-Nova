namespace PeerCast.Nova.App;

internal static class GenesisStatusPresentation
{
    public static string Format(GenesisMainViewModel viewModel) =>
        FormatCore(viewModel, includeCatalogStatus: true);

    internal static string Format(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogRefreshDiagnostics? refreshDiagnostics,
        DateTimeOffset now)
    {
        if (refreshDiagnostics is null)
        {
            return Format(viewModel);
        }

        var statusText = FormatCore(viewModel, includeCatalogStatus: false);
        var refreshStatus = GenesisChannelCatalogRefreshStatusPresentation.Format(
            refreshDiagnostics,
            viewModel.IsChannelCatalogRefreshing,
            now);
        return $"{statusText} · {refreshStatus}";
    }

    private static string FormatCore(
        GenesisMainViewModel viewModel,
        bool includeCatalogStatus)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var statusText = includeCatalogStatus
            ? viewModel.StatusText
            : FormatPrimaryStatus(viewModel);
        if (string.Equals(statusText, "Ready", StringComparison.Ordinal))
        {
            statusText = viewModel.ChannelCount switch
            {
                1 => "Ready · 1 channel",
                > 1 => $"Ready · {viewModel.ChannelCount} channels",
                _ => statusText,
            };
        }

        var errorMessage = viewModel.ErrorMessage;
        if (includeCatalogStatus)
        {
            var catalogErrorMessage = viewModel.ChannelCatalogErrorMessage;
            if (!string.IsNullOrWhiteSpace(catalogErrorMessage) &&
                !string.Equals(statusText, catalogErrorMessage, StringComparison.Ordinal))
            {
                errorMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? catalogErrorMessage
                    : $"{errorMessage} · {catalogErrorMessage}";
            }
        }

        return string.IsNullOrWhiteSpace(errorMessage)
            ? statusText
            : $"{statusText}: {errorMessage}";
    }

    private static string FormatPrimaryStatus(GenesisMainViewModel viewModel) =>
        viewModel.PlaybackState switch
        {
            PlaybackSessionState.Starting => "Starting",
            PlaybackSessionState.Playing => "Playing",
            PlaybackSessionState.Stopping => "Stopping",
            PlaybackSessionState.Faulted => "Playback error",
            _ => "Ready",
        };
}
