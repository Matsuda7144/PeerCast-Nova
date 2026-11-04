using System.Diagnostics;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal static class GenesisContactUrl
{
    internal const int MaximumCharacters = 2048;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumCharacters ||
            value.Any(char.IsControl) || value.Contains('\\')) return null;
        var text = value.Trim();
        if (text.Any(char.IsWhiteSpace) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.AbsoluteUri.Length > MaximumCharacters ||
            Uri.UnescapeDataString(text).Any(char.IsControl)) return null;
        return uri.AbsoluteUri;
    }
}

/// <summary>Immutable targets captured before TrackPopupMenu enters its message loop.</summary>
internal sealed record GenesisChannelContextTarget(
    string? ContactUrl, GenesisChannelItem? RecordingChannel, bool StopRecording,
    bool CanRecord, ChannelId? ChannelId, RecordingSessionId? SessionId, string? RecordingDirectory)
{
    public string RecordingLabel => StopRecording ? "録画停止" : "録画開始";
}

internal interface IGenesisChannelContextEffects
{
    void OpenContact(string url);
    void CopyContact(string url);
    bool DirectoryExists(string path);
    void OpenDirectory(string path);
    void Report(GenesisGuiNextChannelContextFailure failure);
}

internal static class GenesisChannelContextActions
{
    public static GenesisChannelContextTarget Capture(GenesisMainViewModel viewModel, GenesisGuiNextNativeTabRow row)
    {
        var channel = row.IsInteractive ? row.InteractiveChannel : null;
        var presentation = viewModel.GetManualRecordingPresentation(channel);
        var manual = channel is null ? null : viewModel.ActiveManualRecordings.FirstOrDefault(recording =>
            GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var id) && recording.ChannelId == id);
        var stop = presentation.IsRecording && (manual is not null || presentation.CanStop);
        return new(GenesisContactUrl.Normalize(channel?.ContactUrl), channel, stop,
            stop ? presentation.CanStop && manual is not null : presentation.CanRecord,
            manual?.ChannelId, manual?.SessionId,
            viewModel.RecordingOutputSettings?.CurrentRuntimeDirectory);
    }

    public static bool IsAction(GenesisChannelContextMenuCommand command) => command is
        GenesisChannelContextMenuCommand.OpenContact or GenesisChannelContextMenuCommand.CopyContact or
        GenesisChannelContextMenuCommand.Recording or GenesisChannelContextMenuCommand.OpenRecordingDirectory;

    public static async Task ExecuteAsync(GenesisMainViewModel viewModel, GenesisChannelContextTarget target,
        GenesisChannelContextMenuCommand command, IGenesisChannelContextEffects effects)
    {
        try
        {
            switch (command)
            {
                case GenesisChannelContextMenuCommand.OpenContact:
                    if (GenesisContactUrl.Normalize(target.ContactUrl) is { } openUrl) effects.OpenContact(openUrl);
                    break;
                case GenesisChannelContextMenuCommand.CopyContact:
                    if (GenesisContactUrl.Normalize(target.ContactUrl) is { } copyUrl) effects.CopyContact(copyUrl);
                    break;
                case GenesisChannelContextMenuCommand.Recording when target.CanRecord:
                    if (target.StopRecording && target.ChannelId is { } channelId && target.SessionId is { } sessionId)
                        await viewModel.StopManualRecordingAsync(channelId, sessionId).ConfigureAwait(false);
                    else if (!target.StopRecording && target.RecordingChannel is { } channel)
                        await viewModel.StartManualRecordingAsync(channel).ConfigureAwait(false);
                    break;
                case GenesisChannelContextMenuCommand.OpenRecordingDirectory:
                    if (!GenesisRecordingOutputDirectorySettings.TryNormalize(target.RecordingDirectory, out var directory) ||
                        !effects.DirectoryExists(directory!))
                        effects.Report(GenesisGuiNextChannelContextFailure.RecordingDirectoryMissing);
                    else effects.OpenDirectory(directory!);
                    break;
            }
        }
        catch
        {
            effects.Report(command switch
            {
                GenesisChannelContextMenuCommand.Recording => GenesisGuiNextChannelContextFailure.RecordingCommand,
                GenesisChannelContextMenuCommand.CopyContact => GenesisGuiNextChannelContextFailure.ContactClipboardCopy,
                _ => GenesisGuiNextChannelContextFailure.ExternalOpen,
            });
        }
    }
}

internal sealed class SystemGenesisChannelContextEffects(nint owner) : IGenesisChannelContextEffects
{
    public void OpenContact(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public void CopyContact(string url) => GenesisChannelContextMenuNative.CopySourceToClipboard(owner, url,
        reportFailure: () => Report(GenesisGuiNextChannelContextFailure.ContactClipboardCopy));
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void OpenDirectory(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(path);
        _ = Process.Start(start);
    }
    public void Report(GenesisGuiNextChannelContextFailure failure) =>
        SystemGenesisGuiNextChannelContextFeedback.Report(owner, failure);
}
