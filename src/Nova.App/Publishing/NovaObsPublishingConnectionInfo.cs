using System.Security.Cryptography;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal readonly record struct NovaObsPublishingConnectionIdentity(
    long Generation, Guid ChannelId, Guid SessionId, long SourceRevision);

/// <summary>Explicit UI disclosure only. Never serialize or log this object.</summary>
internal sealed class NovaObsPublishingConnectionInfo(
    NovaObsPublishingConnectionIdentity identity, string? server, string key,
    bool notStarted, bool listenerReady, bool publisherConnected, bool headAvailable)
{
    internal NovaObsPublishingConnectionIdentity Identity { get; } = identity;
    internal string? Server { get; } = server;
    internal string Key { get; } = key;
    internal bool NotStarted { get; } = notStarted;
    internal bool ListenerReady { get; } = listenerReady;
    internal bool PublisherConnected { get; } = publisherConnected;
    internal bool HeadAvailable { get; } = headAvailable;
    internal bool CanCopyServer => Server is not null && (NotStarted || ListenerReady);
    internal bool CanUseKey => NotStarted || ListenerReady;
    public override string ToString() => "Publishing connection information [redacted]";
}

internal interface INovaObsPublishingConnectionInfo
{
    NovaObsPublishingConnectionInfo? ReadConnectionInfo(NovaObsPublishingReadOnlySnapshot status);
    // Bounded application-owned validation only; never reads a borrowed Snapshot.
    bool IsConnectionInfoCurrent(NovaObsPublishingConnectionIdentity identity);
}

/// <summary>
/// Borrows one owner's status without invoking its lifecycle. Keeps only a hash
/// for equal-revision consistency, never a cached key or raw status document.
/// </summary>
internal sealed class NovaObsPublishingConnectionReader(INovaObsPublishingStatusSource? source)
{
    private readonly object _gate = new();
    private NovaObsPublishingConnectionIdentity? _current;
    private long _revision = -1;
    private string? _fingerprint;
    private bool _terminal;

    internal NovaObsPublishingConnectionInfo? Read(long generation, NovaObsPublishingReadOnlySnapshot status)
    {
        ObsPublishStatusSnapshot raw;
        try { raw = source?.Snapshot!; }
        catch { Invalidate(); return null; }
        if (raw is null || raw.ChannelId is null || raw.SessionId is null ||
            raw.ChannelId.Value == Guid.Empty || raw.SessionId.Value == Guid.Empty ||
            raw.ChannelId.Value != status.ChannelId || raw.SessionId.Value != status.SessionId ||
            generation != status.Generation || raw.Revision < 0 || !Enum.IsDefined(raw.State))
        { Invalidate(); return null; }

        if (raw.EndReason is not null || raw.State is ChannelSessionState.Stopping or ChannelSessionState.Stopped or ChannelSessionState.Faulted ||
            status.Availability == NovaObsPublishingAvailability.Stopped ||
            status.LifecycleState is NovaObsPublishingLifecycleState.Stopping or NovaObsPublishingLifecycleState.Stopped or NovaObsPublishingLifecycleState.Failed)
        {
            lock (_gate) { _terminal = true; _current = null; }
            return null;
        }
        if (status.Availability == NovaObsPublishingAvailability.Unavailable) { Invalidate(); return null; }
        if (string.IsNullOrWhiteSpace(raw.PublishName)) { Invalidate(); return null; }
        var notStarted = status.LifecycleState == NovaObsPublishingLifecycleState.NotStarted &&
            status.IsSourceNotStarted && raw.State == ChannelSessionState.Created &&
            !raw.ListenerReady && !raw.PublisherConnected && !raw.HeadAvailable;
        var identity = new NovaObsPublishingConnectionIdentity(generation, raw.ChannelId.Value, raw.SessionId.Value, raw.Revision);
        var bytes = Encoding.UTF8.GetBytes($"{raw.State}\0{raw.ListenerReady}\0{raw.PublisherConnected}\0{raw.HeadAvailable}\0{raw.RtmpServer}\0{raw.PublishName}");
        string fingerprint;
        try { fingerprint = Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        lock (_gate)
        {
            if (raw.Revision < _revision) return null; // A late read cannot invalidate newer accepted facts.
            if (_terminal || (raw.Revision == _revision && _fingerprint != fingerprint))
            { _current = null; return null; }
            _revision = raw.Revision;
            _fingerprint = fingerprint;
            _current = identity;
        }
        return new(identity, raw.RtmpServer, raw.PublishName, notStarted, raw.ListenerReady,
            raw.PublisherConnected, raw.HeadAvailable && raw.State == ChannelSessionState.Receiving);
    }

    internal bool IsCurrent(NovaObsPublishingConnectionIdentity identity)
    {
        lock (_gate) return !_terminal && _current == identity;
    }

    internal void Invalidate() { lock (_gate) _current = null; }
}

internal sealed record NovaObsPublishingConnectionPresentation(
    NovaObsPublishingConnectionIdentity? Identity, string ServerText, string StateText,
    bool CanCopyServer, bool CanUseKey, NovaObsPublishingTone Tone)
{
    internal const string Mask = "********";
    internal const string Instructions =
        "1. Novaで配信開始し、待受準備完了を確認\r\n2. OBSの設定 → 配信 → カスタムにサーバーとキーを入力\r\n3. OBSで配信開始（Novaの終了はローカル終了、中断はキャンセル）";
    internal static readonly NovaObsPublishingConnectionPresentation Checking =
        new(null, "確認中", "接続情報を確認中", false, false, NovaObsPublishingTone.Normal);

    internal static NovaObsPublishingConnectionPresentation From(
        NovaObsPublishingReadOnlySnapshot status, NovaObsPublishingConnectionInfo? info)
    {
        var failed = status.LifecycleState == NovaObsPublishingLifecycleState.Failed ||
            status.LifecycleFailure is { } failure && failure != NovaObsPublishingLifecycleFailure.None;
        if (failed) return Closed("異常終了・結果不明", NovaObsPublishingTone.Warning);
        if (status.Availability == NovaObsPublishingAvailability.Unavailable)
            return Closed("異常終了・結果不明", NovaObsPublishingTone.Warning);
        if (status.LifecycleState == NovaObsPublishingLifecycleState.Stopping)
            return Closed("終了処理中", NovaObsPublishingTone.Special);
        if (status.CompletionKind is { } kind && kind != NovaObsPublishingCompletionKind.None)
        {
            var proven = status.LifecycleFailure == NovaObsPublishingLifecycleFailure.None && status.CleanupCompleted == true;
            return kind switch
            {
                NovaObsPublishingCompletionKind.ObsNormalEos when proven => Closed("正常終了（OBS停止）", NovaObsPublishingTone.Active, true),
                NovaObsPublishingCompletionKind.LocalCompleted when proven => Closed("終了（Novaローカル）", NovaObsPublishingTone.Active, true),
                NovaObsPublishingCompletionKind.Cancelled when proven => Closed("中断済み", NovaObsPublishingTone.Special, true),
                _ => Closed("異常終了・結果不明", NovaObsPublishingTone.Warning),
            };
        }
        if (status.Availability == NovaObsPublishingAvailability.Stopped)
            return Closed("異常終了・結果不明", NovaObsPublishingTone.Warning);
        if (info is null) return Checking;
        var text = info.NotStarted ? "停止中（未開始）・配信開始前／未待受" :
            info.HeadAvailable ? "メディア初期化受信済み" :
            info.PublisherConnected ? "入力接続あり・メディア待ち" :
            info.ListenerReady ? "待受準備完了（このPCのみ）" : "待受準備中";
        return new(info.Identity, info.Server ?? "待受開始後に確定", text, info.CanCopyServer, info.CanUseKey,
            info.ListenerReady ? NovaObsPublishingTone.Active : NovaObsPublishingTone.Normal);
    }

    private static NovaObsPublishingConnectionPresentation Closed(string text, NovaObsPublishingTone tone, bool stopped = false) =>
        new(null, stopped ? "待受停止" : "現在の接続情報は利用できません", text, false, false, tone);
    public override string ToString() => "Publishing connection presentation [redacted]";
}
