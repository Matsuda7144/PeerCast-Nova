using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisAutomaticRecordingEvaluationController
{
    public event EventHandler? SuppressionChanged;

    public IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> SuppressionRows => _suppression.Snapshot();

    public bool CanStopAutomatic(ChannelId channelId, RecordingSessionId sessionId)
    {
        if (Volatile.Read(ref _disposed) != 0 || _suppression.IsPending(channelId)) return false;
        var state = _recording.GetState(channelId);
        return state.Session?.Origin == RecordingSessionOrigin.AutomaticRule &&
            state.Session.SessionId == sessionId && state.AutomationState.IsRecording && state.AutomationState.ActiveRuleId is not null;
    }

    public bool CanAllowAutomatic(ChannelId channelId, Guid ruleId) =>
        Volatile.Read(ref _disposed) == 0 && _suppression.ContainsCommitted(channelId, ruleId);

    public bool IsStoppedAutomaticGeneration(ChannelId channelId, RecordingSessionId sessionId) =>
        _suppression.IsStoppedGeneration(channelId, sessionId);

    public async ValueTask<string> StopAutomaticAsync(
        ChannelId channelId, RecordingSessionId sessionId, CancellationToken token = default)
    {
        using var admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdownCancellation.Token);
        try { await _snapshotGate.WaitAsync(admissionCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return "録画を停止できませんでした。"; }
        try
        {
            if (!CanStopAutomatic(channelId, sessionId)) return "録画状態が更新されています。最新の状態を確認してください。";
            var reserved = _suppression.Reserve(channelId, sessionId);
            if (reserved is null) return "自動録画の再開抑止を準備できないため、停止できませんでした。";
            RecordingAutomaticStopAdmission? receipt = null;
            try
            {
                var result = await _recording.RequestAutomaticStopAsync(channelId, sessionId, admissionCancellation.Token).ConfigureAwait(false);
                receipt = result.Admission;
            }
            catch
            {
                _recording.TryGetAutomaticStopAdmission(channelId, sessionId, out receipt);
            }

            if (receipt is null)
            {
                _suppression.Rollback(reserved);
                return "録画を停止できませんでした。";
            }

            _suppression.Bind(reserved, receipt, () => ObserveStopAsync(reserved, receipt));
            PublishSuppressionChanged();
            return "停止処理中です。";
        }
        finally { _snapshotGate.Release(); }
    }

    public async ValueTask<string> AllowAutomaticAsync(ChannelId channelId, Guid ruleId, CancellationToken token = default)
    {
        try { await _snapshotGate.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return "自動録画の抑止を解除できませんでした。"; }
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return "自動録画の抑止を解除できませんでした。";
            if (!_suppression.Allow(channelId, ruleId)) return "自動録画の抑止状態に変更はありません。";
            PublishSuppressionChanged();
            return "自動録画の抑止を解除しました。次回のチャンネル更新から通常の判定に戻ります。";
        }
        finally { _snapshotGate.Release(); }
    }

    private async Task ObserveStopAsync(GenesisAutomaticRecordingSuppression.Entry entry, RecordingAutomaticStopAdmission receipt)
    {
        var terminal = await receipt.Completion.ConfigureAwait(false);
        await _snapshotGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try { _suppression.Commit(entry, terminal.Kind); }
        finally { _snapshotGate.Release(); }
        PublishSuppressionChanged();
    }

    private void PublishSuppressionChanged()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            try { SuppressionChanged?.Invoke(this, EventArgs.Empty); }
            catch { /* Projection observers cannot change recorder ownership. */ }
        }
    }
}
