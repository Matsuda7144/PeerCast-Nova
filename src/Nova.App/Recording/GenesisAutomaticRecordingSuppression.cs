using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisAutomaticRecordingSuppressionState { Reserved, AdmittedPending, Committed }

internal sealed record GenesisAutomaticRecordingSuppressionRow(
    ChannelId ChannelId,
    Guid RuleId,
    GenesisAutomaticRecordingSuppressionState State,
    string StatusText);

internal interface IGenesisAutomaticRecordingUserCommands
{
    event EventHandler? SuppressionChanged;
    IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> SuppressionRows { get; }
    bool CanStopAutomatic(ChannelId channelId, RecordingSessionId sessionId);
    bool IsStoppedAutomaticGeneration(ChannelId channelId, RecordingSessionId sessionId);
    bool CanAllowAutomatic(ChannelId channelId, Guid ruleId);
    ValueTask<string> StopAutomaticAsync(ChannelId channelId, RecordingSessionId sessionId, CancellationToken token = default);
    ValueTask<string> AllowAutomaticAsync(ChannelId channelId, Guid ruleId, CancellationToken token = default);
}

/// <summary>Runtime intent only. Reservations precede recorder effects; entries never evict.</summary>
internal sealed class GenesisAutomaticRecordingSuppression
{
    internal const int Capacity = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<ChannelId, Entry> _entries = [];
    private readonly HashSet<Task> _observers = [];

    internal sealed class Entry(ChannelId channelId, RecordingSessionId sessionId)
    {
        public ChannelId ChannelId { get; } = channelId;
        public RecordingSessionId SessionId { get; } = sessionId;
        public RecordingAutomaticStopAdmission? Receipt { get; set; }
        public GenesisAutomaticRecordingSuppressionState State { get; set; }
        public string StatusText { get; set; } = "停止処理中です。";
        public Task? Observer { get; set; }
    }

    internal Entry? Reserve(ChannelId channelId, RecordingSessionId sessionId)
    {
        lock (_gate)
        {
            _observers.RemoveWhere(t => t.IsCompletedSuccessfully);
            if (_entries.ContainsKey(channelId) || _entries.Count >= Capacity || _observers.Count >= Capacity) return null;
            var entry = new Entry(channelId, sessionId);
            _entries.Add(channelId, entry);
            return entry;
        }
    }

    internal bool IsPending(ChannelId channelId)
    {
        lock (_gate) return _entries.TryGetValue(channelId, out var entry) &&
            entry.State != GenesisAutomaticRecordingSuppressionState.Committed;
    }

    internal HashSet<ChannelId> PendingChannelsSnapshot()
    {
        lock (_gate) return _entries.Values.Where(e => e.State != GenesisAutomaticRecordingSuppressionState.Committed)
            .Select(e => e.ChannelId).ToHashSet();
    }

    internal bool IsStoppedGeneration(ChannelId channelId, RecordingSessionId sessionId)
    {
        lock (_gate) return _entries.TryGetValue(channelId, out var entry) && entry.SessionId == sessionId &&
            entry.State == GenesisAutomaticRecordingSuppressionState.Committed;
    }

    internal void Rollback(Entry entry)
    {
        lock (_gate)
        {
            if (_entries.GetValueOrDefault(entry.ChannelId) == entry && entry.Receipt is null)
                _entries.Remove(entry.ChannelId);
        }
    }

    internal void Bind(Entry entry, RecordingAutomaticStopAdmission receipt, Func<Task> observe)
    {
        lock (_gate)
        {
            entry.Receipt = receipt;
            entry.State = GenesisAutomaticRecordingSuppressionState.AdmittedPending;
            entry.Observer = Task.Run(observe, CancellationToken.None);
            _observers.RemoveWhere(t => t.IsCompletedSuccessfully);
            _observers.Add(entry.Observer);
        }
    }

    internal void Commit(Entry entry, RecordingAutomationStopResultKind kind)
    {
        lock (_gate)
        {
            if (_entries.GetValueOrDefault(entry.ChannelId) != entry) return;
            entry.State = GenesisAutomaticRecordingSuppressionState.Committed;
            entry.StatusText = kind switch
            {
                RecordingAutomationStopResultKind.NoDecodableVideoStart =>
                    "録画ファイルは作成されませんでした。自動録画の再開は抑止されています。",
                RecordingAutomationStopResultKind.Faulted =>
                    "録画は停止しましたが、録画ファイルの保存に失敗しました。自動録画の再開は抑止されています。",
                _ => "自動録画を停止し、このルールによる再開を抑止しました。",
            };
        }
    }

    internal IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> Snapshot()
    {
        lock (_gate) return Array.AsReadOnly(_entries.Values.Where(e => e.Receipt is not null)
            .Select(e => new GenesisAutomaticRecordingSuppressionRow(e.ChannelId,
                e.Receipt!.ActiveRuleId, e.State,
                e.Receipt.ResolutionFault is not null && e.State != GenesisAutomaticRecordingSuppressionState.Committed
                    ? "停止結果を確認中です。" : e.StatusText)).ToArray());
    }

    internal bool Allow(ChannelId channelId, Guid ruleId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(channelId, out var entry) ||
                entry.State != GenesisAutomaticRecordingSuppressionState.Committed ||
                entry.Receipt?.ActiveRuleId != ruleId) return false;
            _entries.Remove(channelId);
            return true;
        }
    }

    internal bool ContainsCommitted(ChannelId channelId, Guid ruleId) => Snapshot().Any(e =>
        e.ChannelId == channelId && e.RuleId == ruleId && e.State == GenesisAutomaticRecordingSuppressionState.Committed);

    internal void ClearUnobservedRules(RecordingRuleSet rules)
    {
        var enabled = rules.Rules.Where(r => r.IsEnabled).Select(r => r.Id).ToHashSet();
        foreach (var entry in Snapshot())
            if (!enabled.Contains(entry.RuleId)) Allow(entry.ChannelId, entry.RuleId);
    }

    internal Task[] Observers()
    {
        lock (_gate) return _observers.ToArray();
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _observers.Clear();
        }
    }
}
