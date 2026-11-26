using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

/// <summary>
/// A process-lifetime snapshot, not a durable index or filesystem library.
/// Entries are ordered by terminal observation, not media chronology.
/// </summary>
internal sealed record GenesisRecordingHistorySnapshot(
    long Revision,
    IReadOnlyList<RecordingSessionTerminalFact> Entries,
    long EvictedEntryCount,
    long PublicationFailureCount,
    GenesisRecordingHistoryRetentionBoundary? EvictedThrough = null)
{
    public bool IsTruncated => EvictedEntryCount != 0;
    public bool HasGap => IsTruncated || PublicationFailureCount != 0;
}

internal sealed record GenesisRecordingHistoryRetentionBoundary(DateTimeOffset TerminalObservedAtUtc, Guid SessionId);

/// <summary>
/// Application-owned terminal history. Self-contained facts need no active
/// correlation registry and cannot remove a newer active recording generation.
/// This owner performs no I/O, native UI, recording commands, or async queuing.
/// </summary>
internal sealed class GenesisRecordingHistoryReadModel : IRecordingSessionTerminalObserver
{
    internal const int MaximumTerminalEntryCount = 4096;

    private readonly object _gate = new();
    private readonly Dictionary<RecordingSessionId, RecordingSessionTerminalFact> _entries = [];
    private GenesisRecordingHistoryRetentionBoundary? _evictedThrough;
    private long _revision;
    private long _evictedEntryCount;
    private long _publicationFailureCount;

    // The application subscriber only posts a capacity-one wake signal. No
    // serialization or disk I/O is permitted on the terminal publication path.
    internal event Action? Changed;

    internal void Hydrate(GenesisRecordingHistorySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_revision != 0 || _entries.Count != 0) throw new InvalidOperationException("History already initialized.");
            foreach (var fact in snapshot.Entries) _entries.Add(fact.Session.SessionId, fact);
            _evictedEntryCount = snapshot.EvictedEntryCount;
            _publicationFailureCount = snapshot.PublicationFailureCount;
            _evictedThrough = snapshot.EvictedThrough;
            _revision = 1; // Fresh process revision; not a persisted media fact.
        }
    }

    public void ObserveTerminal(RecordingSessionTerminalFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        lock (_gate)
        {
            if (_entries.TryGetValue(fact.Session.SessionId, out var existing))
            {
                if (existing != fact)
                {
                    // An immutable terminal fact cannot silently rewrite the
                    // past. A conflicting replay is a separate history gap.
                    RecordPublicationFailure();
                }

                return;
            }

            if (_evictedThrough is { } cutoff && Compare(fact, cutoff) <= 0)
            {
                // The retention window is already visibly truncated. Old
                // replays cannot reinsert evicted rows or advance revision.
                return;
            }

            _entries.Add(fact.Session.SessionId, fact);
            if (_entries.Count > MaximumTerminalEntryCount)
            {
                var oldest = _entries.Values.MinBy(
                    static entry => entry,
                    TerminalObservationComparer.Instance)!;
                _entries.Remove(oldest.Session.SessionId);
                _evictedThrough = new(oldest.TerminalObservedAtUtc, oldest.Session.SessionId.Value);
                _evictedEntryCount++;
            }

            _revision++;
            Changed?.Invoke();
        }
    }

    public void RecordPublicationFailure()
    {
        lock (_gate)
        {
            _publicationFailureCount++;
            _revision++;
            Changed?.Invoke();
        }
    }

    public GenesisRecordingHistorySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var entries = _entries.Values
                .OrderDescending(TerminalObservationComparer.Instance)
                .ToArray();
            return new(
                _revision,
                Array.AsReadOnly(entries),
                _evictedEntryCount,
                _publicationFailureCount,
                _evictedThrough);
        }
    }

    public RecordingSessionTerminalFact? Find(RecordingSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate)
        {
            return _entries.GetValueOrDefault(sessionId);
        }
    }

    private static int Compare(RecordingSessionTerminalFact left, RecordingSessionTerminalFact right)
    {
        var time = left.TerminalObservedAtUtc.CompareTo(right.TerminalObservedAtUtc);
        return time != 0
            ? time
            : left.Session.SessionId.Value.CompareTo(right.Session.SessionId.Value);
    }

    private static int Compare(RecordingSessionTerminalFact left, GenesisRecordingHistoryRetentionBoundary right)
    {
        var time = left.TerminalObservedAtUtc.CompareTo(right.TerminalObservedAtUtc);
        return time != 0 ? time : left.Session.SessionId.Value.CompareTo(right.SessionId);
    }

    private sealed class TerminalObservationComparer : IComparer<RecordingSessionTerminalFact>
    {
        public static TerminalObservationComparer Instance { get; } = new();

        public int Compare(RecordingSessionTerminalFact? x, RecordingSessionTerminalFact? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            return GenesisRecordingHistoryReadModel.Compare(x, y);
        }
    }
}
