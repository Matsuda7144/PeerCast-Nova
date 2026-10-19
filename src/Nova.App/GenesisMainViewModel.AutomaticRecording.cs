using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App;

public sealed partial class GenesisMainViewModel
{
    private readonly IGenesisAutomaticRecordingUserCommands? _automaticRecordingCommands;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ChannelId, byte> _automaticAllowCommands = new();
    private string _automaticRecordingCommandMessage = string.Empty;
    internal string AutomaticRecordingCommandMessage => Volatile.Read(ref _automaticRecordingCommandMessage);
    internal IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> SuppressionRows =>
        _automaticRecordingCommands?.SuppressionRows ?? [];

    private void OnSuppressionChanged(object? sender, EventArgs args) => NotifyRecordingPresentationChanged();

    internal bool CanAllowAutomatic(GenesisAutomaticRecordingSuppressionRow row) =>
        !_automaticAllowCommands.ContainsKey(row.ChannelId) &&
        (_automaticRecordingCommands?.CanAllowAutomatic(row.ChannelId, row.RuleId) ?? false);

    internal async ValueTask StopAutomaticRecordingAsync(GenesisGuiNextActiveRecording row, CancellationToken token)
    {
        if (_automaticRecordingCommands is null || !GetActiveRecordingPresentation(row).CanStop ||
            !_recordingCommandLanes.TryEnter(row.ChannelId)) return;
        _recordingCommands[row.ChannelId] = new(RecordingCommandState.Stopping, row.SessionId);
        NotifyRecordingPresentationChanged();
        try
        {
            _recordingNotices[row.ChannelId] = await _automaticRecordingCommands.StopAutomaticAsync(row.ChannelId, row.SessionId, token).ConfigureAwait(false);
        }
        finally
        {
            _recordingCommands.TryRemove(row.ChannelId, out _);
            _recordingCommandLanes.Exit(row.ChannelId);
            NotifyRecordingPresentationChanged();
        }
    }

    internal async ValueTask AllowAutomaticRecordingAsync(GenesisAutomaticRecordingSuppressionRow row, CancellationToken token)
    {
        if (_automaticRecordingCommands is null || !CanAllowAutomatic(row) ||
            !_automaticAllowCommands.TryAdd(row.ChannelId, 0)) return;
        NotifyRecordingPresentationChanged();
        try
        {
            var message = await _automaticRecordingCommands.AllowAutomaticAsync(row.ChannelId, row.RuleId, token).ConfigureAwait(false);
            _recordingNotices[row.ChannelId] = message;
            Volatile.Write(ref _automaticRecordingCommandMessage, message);
        }
        finally
        {
            _automaticAllowCommands.TryRemove(row.ChannelId, out _);
            NotifyRecordingPresentationChanged();
        }
    }
}
