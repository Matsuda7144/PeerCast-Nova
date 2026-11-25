namespace PeerCast.Nova.App;

internal enum GenesisChannelPresenceState
{
    Active,
    Ended,
}

internal enum GenesisChannelPresenceTransitionKind
{
    BecameActive,
    BecameEnded,
}

internal sealed record GenesisChannelPresenceEntry(
    string StableKey,
    GenesisChannelItem Channel,
    GenesisChannelPresenceState State,
    bool IsCurrentlyObserved,
    bool IsNew,
    int ConsecutiveMissingSuccessfulRefreshes,
    IReadOnlyList<string> KnownYellowPageIds);

internal sealed record GenesisChannelPresenceTransition(
    GenesisChannelPresenceTransitionKind Kind,
    GenesisChannelPresenceEntry Entry);

internal sealed class GenesisChannelPresenceSnapshot
{
    public GenesisChannelPresenceSnapshot(
        IEnumerable<GenesisChannelPresenceEntry> entries,
        IEnumerable<GenesisChannelPresenceTransition> transitions)
        : this(
            entries,
            transitions,
            Array.Empty<GenesisYellowPageCatalogReadStatus>())
    {
    }

    public GenesisChannelPresenceSnapshot(
        IEnumerable<GenesisChannelPresenceEntry> entries,
        IEnumerable<GenesisChannelPresenceTransition> transitions,
        IEnumerable<GenesisYellowPageCatalogReadStatus> yellowPages)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(yellowPages);

        Entries = Array.AsReadOnly(entries.ToArray());
        Active = Array.AsReadOnly(
            Entries.Where(static entry =>
                entry.State == GenesisChannelPresenceState.Active)
                .ToArray());
        New = Array.AsReadOnly(
            Active.Where(static entry => entry.IsNew).ToArray());
        Transitions = Array.AsReadOnly(transitions.ToArray());
        YellowPages = Array.AsReadOnly(yellowPages.ToArray());
    }

    public IReadOnlyList<GenesisChannelPresenceEntry> Entries { get; }

    public IReadOnlyList<GenesisChannelPresenceEntry> Active { get; }

    public IReadOnlyList<GenesisChannelPresenceEntry> New { get; }

    public IReadOnlyList<GenesisChannelPresenceTransition> Transitions { get; }

    public IReadOnlyList<GenesisYellowPageCatalogReadStatus> YellowPages { get; }
}

/// <summary>
/// Tracks stable channel identities across successful multi-YP refresh
/// generations. A transient absence remains active until three consecutive
/// refreshes provide enough successful YP evidence that the channel is absent.
/// </summary>
internal sealed class GenesisChannelPresenceTracker
{
    internal const int MissingSuccessfulRefreshesBeforeEnded = 3;

    private readonly Dictionary<string, PresenceRecord> _records =
        new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private bool _hasBaseline;

    public GenesisChannelPresenceSnapshot ApplySuccessfulRefresh(
        GenesisAggregatedChannelCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var transitions = new List<GenesisChannelPresenceTransition>();
        var observedKeys = new HashSet<string>(StringComparer.Ordinal);
        var yellowPageStates =
            new Dictionary<string, GenesisYellowPageCatalogReadState>(
                StringComparer.Ordinal);

        var hasSuccessfulYellowPage = false;
        foreach (var status in snapshot.YellowPages)
        {
            yellowPageStates[status.Definition.Id] = status.State;
            if (status.State == GenesisYellowPageCatalogReadState.Succeeded)
            {
                hasSuccessfulYellowPage = true;
            }
        }

        // 起動直後の初回 YP 取得はベースラインとして記録し、既存全チャンネルの
        // 新着扱いおよび通知・効果音連打を抑止する。
        var isInitialBaseline = !_hasBaseline;

        foreach (var entry in snapshot.Entries)
        {
            var stableKey = GenesisChannelIdentity.GetStableKey(entry.Channel);
            if (!observedKeys.Add(stableKey))
            {
                continue;
            }

            if (!_records.TryGetValue(stableKey, out var record))
            {
                record = new PresenceRecord(entry.Channel, isNew: !isInitialBaseline);
                record.AddKnownYellowPages(entry.YellowPageIds);
                _records.Add(stableKey, record);
                _order.Add(stableKey);
                if (!isInitialBaseline)
                {
                    transitions.Add(new(
                        GenesisChannelPresenceTransitionKind.BecameActive,
                        record.ToEntry(stableKey)));
                }

                continue;
            }

            var wasEnded = record.State == GenesisChannelPresenceState.Ended;
            record.Channel = entry.Channel;
            record.State = GenesisChannelPresenceState.Active;
            record.IsCurrentlyObserved = true;
            record.IsNew = wasEnded;
            record.ConsecutiveMissingSuccessfulRefreshes = 0;
            record.AddKnownYellowPages(entry.YellowPageIds);

            if (wasEnded)
            {
                transitions.Add(new(
                    GenesisChannelPresenceTransitionKind.BecameActive,
                    record.ToEntry(stableKey)));
            }
        }

        foreach (var stableKey in _order)
        {
            if (observedKeys.Contains(stableKey))
            {
                continue;
            }

            var record = _records[stableKey];
            record.IsCurrentlyObserved = false;
            if (record.State == GenesisChannelPresenceState.Ended ||
                !HasAbsenceEvidence(record, yellowPageStates))
            {
                continue;
            }

            // New is consumed only by a refresh that actually provides evidence
            // about this channel. A failed supplying YP must not make a new row
            // disappear before a later observation or proven absence.
            record.IsNew = false;
            record.ConsecutiveMissingSuccessfulRefreshes++;
            if (record.ConsecutiveMissingSuccessfulRefreshes <
                MissingSuccessfulRefreshesBeforeEnded)
            {
                continue;
            }

            record.State = GenesisChannelPresenceState.Ended;
            transitions.Add(new(
                GenesisChannelPresenceTransitionKind.BecameEnded,
                record.ToEntry(stableKey)));
        }

        if (isInitialBaseline && (hasSuccessfulYellowPage || snapshot.Entries.Count > 0))
        {
            _hasBaseline = true;
        }

        return BuildSnapshot(transitions, snapshot.YellowPages);
    }

    private GenesisChannelPresenceSnapshot BuildSnapshot(
        List<GenesisChannelPresenceTransition> transitions,
        IReadOnlyList<GenesisYellowPageCatalogReadStatus> yellowPages)
    {
        var entries = new GenesisChannelPresenceEntry[_order.Count];
        for (var index = 0; index < _order.Count; index++)
        {
            var stableKey = _order[index];
            entries[index] = _records[stableKey].ToEntry(stableKey);
        }

        return new GenesisChannelPresenceSnapshot(entries, transitions, yellowPages);
    }

    private static bool HasAbsenceEvidence(
        PresenceRecord record,
        Dictionary<string, GenesisYellowPageCatalogReadState> yellowPageStates)
    {
        var hasSuccessfulYellowPage = false;
        foreach (var state in yellowPageStates.Values)
        {
            if (state == GenesisYellowPageCatalogReadState.Succeeded)
            {
                hasSuccessfulYellowPage = true;
                break;
            }
        }

        if (!hasSuccessfulYellowPage)
        {
            return false;
        }

        foreach (var yellowPageId in record.KnownYellowPageIds)
        {
            if (yellowPageStates.TryGetValue(yellowPageId, out var state) &&
                state == GenesisYellowPageCatalogReadState.Failed)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class PresenceRecord
    {
        private readonly HashSet<string> _knownYellowPageIds =
            new(StringComparer.Ordinal);
        private readonly List<string> _knownYellowPageOrder = [];

        public PresenceRecord(GenesisChannelItem channel, bool isNew)
        {
            Channel = channel;
            State = GenesisChannelPresenceState.Active;
            IsCurrentlyObserved = true;
            IsNew = isNew;
        }

        public GenesisChannelItem Channel { get; set; }

        public GenesisChannelPresenceState State { get; set; }

        public bool IsCurrentlyObserved { get; set; }

        public bool IsNew { get; set; }

        public int ConsecutiveMissingSuccessfulRefreshes { get; set; }

        public List<string> KnownYellowPageIds => _knownYellowPageOrder;

        public void AddKnownYellowPages(IReadOnlyList<string> yellowPageIds)
        {
            foreach (var yellowPageId in yellowPageIds)
            {
                if (_knownYellowPageIds.Add(yellowPageId))
                {
                    _knownYellowPageOrder.Add(yellowPageId);
                }
            }
        }

        public GenesisChannelPresenceEntry ToEntry(string stableKey) =>
            new(
                stableKey,
                Channel,
                State,
                IsCurrentlyObserved,
                IsNew,
                ConsecutiveMissingSuccessfulRefreshes,
                Array.AsReadOnly(_knownYellowPageOrder.ToArray()));
    }
}
