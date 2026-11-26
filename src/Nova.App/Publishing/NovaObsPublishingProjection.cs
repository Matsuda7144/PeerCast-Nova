using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

/// <summary>
/// Projects one explicitly selected generation. A delayed old-session update
/// cannot select itself or replace a terminal/newer presentation.
/// </summary>
internal sealed class NovaObsPublishingProjection
{
    private readonly object _gate = new();
    private long _generation;
    private Guid _sessionId;
    private long _revision = -1;
    private bool _terminal;

    public bool SelectGeneration(long generation, Guid sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        if (sessionId == Guid.Empty) throw new ArgumentException("A session identity is required.", nameof(sessionId));
        lock (_gate)
        {
            if (generation <= _generation) return false;
            _generation = generation;
            _sessionId = sessionId;
            _revision = -1;
            _terminal = false;
            return true;
        }
    }

    public NovaObsPublishingPanel? Project(long generation, ObsPublishStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            var terminal = status.State == ChannelSessionState.Stopped || status.EndReason is not null;
            if (generation != _generation || status.SessionId.Value != _sessionId ||
                status.Revision <= _revision || (_terminal && !terminal))
                return null;
            _revision = status.Revision;
            _terminal |= terminal;
            var (label, tone) = MapState(status);
            var diagnostic = status.RejectionReason switch
            {
                ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig =>
                    "AAC設定が現在の対応条件と一致しません。",
                ObsRtmpRejectionReason.InvalidAvcConfiguration or
                    ObsRtmpRejectionReason.UnsupportedAvcConfigurationExtension or
                    ObsRtmpRejectionReason.MissingAvcParameterSets =>
                    "H.264 / AVC設定が現在の対応条件と一致しません。",
                _ when status.EndReason == ObsPublishChannelEndReason.ProtocolRejected =>
                    "RTMP配信を受け付けられませんでした。OBSの配信設定を確認してください。",
                _ when status.EndReason == ObsPublishChannelEndReason.StartupFailed =>
                    "待受を開始できませんでした。ほかのアプリが同じポートを使用していないか確認してください。",
                _ when tone == NovaObsPublishingTone.Warning =>
                    "配信接続が終了しました。OBSとNovaの状態を確認してください。",
                _ => string.Empty,
            };
            var details = status.RejectionStage is not null && status.RejectionReason is not null
                ? $"Stage: {status.RejectionStage} / Reason: {status.RejectionReason}"
                : status.EndReason is not null ? $"終了理由: {status.EndReason}" : string.Empty;
            return new(generation, _sessionId, status.Revision, terminal,
                status.RtmpServer ?? "待受開始後に表示します", status.PublishName,
                terminal ? "OBS配信待受: 停止" :
                    status.ListenerReady ? "OBS配信待受: 準備完了（このPCのみ）" : "OBS配信待受: 準備中",
                label, diagnostic, details,
                label == "OBSから配信を受信中" ? status.HttpStreamUrl : null, tone);
        }
    }

    private static (string Label, NovaObsPublishingTone Tone) MapState(ObsPublishStatusSnapshot status)
    {
        if (status.EndReason == ObsPublishChannelEndReason.HostCancelled)
            return ("停止（キャンセル）", NovaObsPublishingTone.Special);
        if (status.EndReason is ObsPublishChannelEndReason.PublisherStopped or ObsPublishChannelEndReason.Disconnected)
            return ("停止", NovaObsPublishingTone.Normal);
        if (status.EndReason is not null || status.State == ChannelSessionState.Faulted)
            return ("配信エラー", NovaObsPublishingTone.Warning);
        return status.State switch
        {
            ChannelSessionState.Stopping => ("停止中", NovaObsPublishingTone.Normal),
            ChannelSessionState.Stopped => ("停止", NovaObsPublishingTone.Normal),
            ChannelSessionState.Receiving when status.HeadAvailable =>
                ("OBSから配信を受信中", NovaObsPublishingTone.Active),
            _ when status.PublisherConnected => ("OBS接続中", NovaObsPublishingTone.Normal),
            _ when status.ListenerReady => ("OBS待機中", NovaObsPublishingTone.Normal),
            _ => ("待受準備中", NovaObsPublishingTone.Normal),
        };
    }
}
