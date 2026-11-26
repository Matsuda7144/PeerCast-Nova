using System.Security;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

/// <summary>
/// Explicit, serialized application observation. Root candidates use bounded
/// lexical top-K selection: enumeration order cannot choose retained files.
/// Enumeration streams to exhaustion; neither elapsed OS I/O nor number of
/// ignored directory entries is hard-bounded. Metadata probes are bounded.
/// </summary>
internal sealed class GenesisRecordingLibraryDiscovery
{
    internal const int MaximumCandidatesPerRoot = 10_000;
    internal const int MaximumSnapshotEntryCount = 4096;
    private static StringComparer Paths => GenesisRecordingLibraryKnownRootRegistry.PathComparer;
    private readonly Func<GenesisRecordingLibraryKnownRootSnapshot> _getRoots;
    private readonly Func<GenesisRecordingHistorySnapshot> _getHistory;
    private readonly IGenesisRecordingLibraryFileSystem _fileSystem;
    private readonly object _gate = new();
    private Task _refreshTail = Task.CompletedTask;
    private GenesisRecordingLibrarySnapshot _snapshot = new(0, GenesisRecordingLibraryObservationState.NotObservedYet,
        Array.Empty<GenesisRecordingLibraryEntry>(), Array.Empty<GenesisRecordingLibraryRootObservation>(), 0, false, 0, false);

    internal GenesisRecordingLibraryDiscovery(
        Func<GenesisRecordingLibraryKnownRootSnapshot> getRoots,
        Func<GenesisRecordingHistorySnapshot> getHistory,
        IGenesisRecordingLibraryFileSystem? fileSystem = null)
    {
        _getRoots = getRoots;
        _getHistory = getHistory;
        _fileSystem = fileSystem ?? new SystemGenesisRecordingLibraryFileSystem();
    }

    internal GenesisRecordingLibrarySnapshot GetSnapshot() { lock (_gate) return _snapshot; }

    internal Task<GenesisRecordingLibraryRefreshResult> RefreshAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            var refresh = RunSerializedAsync(_refreshTail, token);
            // A caller's unexpected failure cannot poison later refreshes.
            _refreshTail = refresh.ContinueWith(completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return refresh;
        }
    }

    private async Task<GenesisRecordingLibraryRefreshResult> RunSerializedAsync(Task previous, CancellationToken token)
    {
        // Queued cancellation is reported after the preceding scan unwinds;
        // it never permits a later scan to overlap that older operation.
        await previous.ConfigureAwait(false);
        return await Task.Run(() => RefreshCore(token), CancellationToken.None).ConfigureAwait(false);
    }

    private GenesisRecordingLibraryRefreshResult RefreshCore(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var next = Observe(_getRoots(), _getHistory(), token);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (Equivalent(_snapshot, next)) return new(GenesisRecordingLibraryRefreshStatus.Unchanged, _snapshot);
                _snapshot = next with { Revision = _snapshot.Revision + 1 };
                return new(GenesisRecordingLibraryRefreshStatus.Published, _snapshot);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(GenesisRecordingLibraryRefreshStatus.Cancelled, GetSnapshot());
        }
    }

    private GenesisRecordingLibrarySnapshot Observe(GenesisRecordingLibraryKnownRootSnapshot roots,
        GenesisRecordingHistorySnapshot history, CancellationToken token)
    {
        var byPath = new Dictionary<string, List<RecordingSessionTerminalFact>>(Paths);
        var excludedHistoryPaths = 0;
        foreach (var fact in history.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (fact.WriteResult is not { } writer) continue;
            if (!GenesisRecordingLibraryKnownRootRegistry.TryNormalize(writer.Path, out var path) || IsPartial(path!))
            {
                excludedHistoryPaths++;
                continue;
            }
            if (!byPath.TryGetValue(path!, out var facts)) byPath.Add(path!, facts = []);
            facts.Add(fact);
        }
        var joined = byPath.ToDictionary(x => x.Key,
            x => (IReadOnlyList<RecordingSessionTerminalFact>)Array.AsReadOnly(x.Value.OrderBy(f => f.Session.SessionId.Value).ToArray()), Paths);
        var retained = new SortedSet<GenesisRecordingLibraryEntry>(EntryPathComparer.Instance);
        var observations = new List<GenesisRecordingLibraryRootObservation>();
        var historySourceRoots = new Dictionary<string, string>(Paths);
        long locations = joined.Count;
        foreach (var root in roots.Roots.OrderBy(x => x.Path, Paths))
        {
            token.ThrowIfCancellationRequested();
            var candidates = new SortedSet<string>(Paths);
            long candidateCount = 0;
            var status = GenesisRecordingLibraryAvailability.Available;
            var failure = GenesisRecordingLibraryFailure.None;
            try
            {
                foreach (var entry in _fileSystem.EnumerateDirectEntries(root.Path, token))
                {
                    token.ThrowIfCancellationRequested();
                    if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                        !GenesisRecordingLibraryKnownRootRegistry.TryNormalize(entry.Path, out var path) ||
                        !Paths.Equals(Path.GetDirectoryName(path), root.Path) || !IsRootCandidate(path!)) continue;
                    candidateCount++;
                    AddPath(candidates, path!);
                    if (candidates.Count > MaximumCandidatesPerRoot) candidates.Remove(candidates.Max!);
                }
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                (status, failure) = MapFailure(exception);
            }
            var unavailable = 0;
            foreach (var path in candidates)
            {
                token.ThrowIfCancellationRequested();
                var probe = Probe(path, token);
                if (joined.ContainsKey(path)) historySourceRoots[path] = root.Path;
                if (probe.Availability == GenesisRecordingLibraryAvailability.Unavailable) unavailable++;
                // Vanishing/unreadable legacy entries are not fabricated as
                // historical Missing records. History is probed below anyway.
                if (probe.Availability != GenesisRecordingLibraryAvailability.Available) continue;
                if (!joined.ContainsKey(path)) locations++;
                Retain(retained, MakeEntry(path, probe, root.Path, joined.GetValueOrDefault(path)));
            }
            observations.Add(new(root.Path, root.Provenance, status, failure, candidateCount, candidates.Count, 0,
                candidateCount > MaximumCandidatesPerRoot, unavailable));
        }
        // Every retained writer-proven exact path is independently probed,
        // including paths outside roots and unsupported extensions. No parent
        // registration, recursion, token matching, or store mutation occurs.
        foreach (var (path, facts) in joined.OrderBy(x => x.Key, Paths))
        {
            token.ThrowIfCancellationRequested();
            Retain(retained, MakeEntry(path, Probe(path, token), historySourceRoots.GetValueOrDefault(path), facts));
        }
        var entries = retained.ToArray();
        var finalRoots = observations.Select(root => root with
        { IncludedEntryCount = entries.Count(entry => Paths.Equals(entry.SourceRoot, root.Path)) }).ToArray();
        return new(0, GenesisRecordingLibraryObservationState.Observed, Array.AsReadOnly(entries), Array.AsReadOnly(finalRoots),
            locations, locations > MaximumSnapshotEntryCount, excludedHistoryPaths, history.HasGap);
    }

    private ProbeResult Probe(string path, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var metadata = _fileSystem.ReadFileMetadata(path, token);
            if ((metadata.Attributes & FileAttributes.ReparsePoint) != 0)
                return new(GenesisRecordingLibraryAvailability.Unavailable, GenesisRecordingLibraryFailure.ReparsePoint, null);
            if ((metadata.Attributes & FileAttributes.Directory) != 0)
                return new(GenesisRecordingLibraryAvailability.Unavailable, GenesisRecordingLibraryFailure.NotRegularFile, null);
            return new(GenesisRecordingLibraryAvailability.Available, GenesisRecordingLibraryFailure.None, metadata);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            var (status, failure) = MapFailure(exception);
            return new(status, failure, null);
        }
    }

    private static GenesisRecordingLibraryEntry MakeEntry(string path, ProbeResult probe, string? root,
        IReadOnlyList<RecordingSessionTerminalFact>? history)
    {
        if (!GenesisRecordingLibraryFilenameParser.TryParse(Path.GetFileName(path), out var claims)) claims = null;
        return new(path, probe.Availability, probe.Failure, Container(path), probe.Metadata, root,
            history ?? Array.Empty<RecordingSessionTerminalFact>(), claims);
    }

    private static void AddPath(SortedSet<string> paths, string path)
    {
        // Stable representative for case-only duplicates, independent of input order.
        if (paths.TryGetValue(path, out var existing))
        {
            if (StringComparer.Ordinal.Compare(existing, path) <= 0) return;
            paths.Remove(existing);
        }
        paths.Add(path);
    }

    private static void Retain(SortedSet<GenesisRecordingLibraryEntry> retained, GenesisRecordingLibraryEntry entry)
    {
        retained.Remove(entry);
        retained.Add(entry);
        if (retained.Count > MaximumSnapshotEntryCount) retained.Remove(retained.Max!);
    }

    private static bool IsPartial(string path) => ExtensionEquals(path, ".partial");
    private static bool IsRootCandidate(string path) => ExtensionEquals(path, ".flv") || ExtensionEquals(path, ".bin");
    private static bool ExtensionEquals(string path, string extension) => Paths.Equals(Path.GetExtension(path), extension);
    private static GenesisRecordingLibraryContainer Container(string path) => ExtensionEquals(path, ".flv")
        ? GenesisRecordingLibraryContainer.FlvCandidate : ExtensionEquals(path, ".bin")
            ? GenesisRecordingLibraryContainer.UnknownContainerCandidate : GenesisRecordingLibraryContainer.UnsupportedExtension;
    private static bool IsFileSystemFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        SecurityException or ArgumentException or NotSupportedException;
    private static (GenesisRecordingLibraryAvailability, GenesisRecordingLibraryFailure) MapFailure(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => (GenesisRecordingLibraryAvailability.Missing, GenesisRecordingLibraryFailure.None),
        UnauthorizedAccessException or SecurityException => (GenesisRecordingLibraryAvailability.Unavailable, GenesisRecordingLibraryFailure.AccessDenied),
        ArgumentException or NotSupportedException => (GenesisRecordingLibraryAvailability.Unavailable, GenesisRecordingLibraryFailure.InvalidPath),
        _ => (GenesisRecordingLibraryAvailability.Unavailable, GenesisRecordingLibraryFailure.Io),
    };

    private static bool Equivalent(GenesisRecordingLibrarySnapshot a, GenesisRecordingLibrarySnapshot b) =>
        a.State == b.State && a.ObservedLocationCount == b.ObservedLocationCount && a.IsSnapshotTruncated == b.IsSnapshotTruncated &&
        a.ExcludedHistoryPathCount == b.ExcludedHistoryPathCount && a.HistoryHasGap == b.HistoryHasGap &&
        a.Roots.SequenceEqual(b.Roots) && a.Entries.Count == b.Entries.Count && a.Entries.Zip(b.Entries).All(pair =>
            pair.First with { HistoryFacts = pair.Second.HistoryFacts } == pair.Second &&
            pair.First.HistoryFacts.SequenceEqual(pair.Second.HistoryFacts));

    private sealed record ProbeResult(GenesisRecordingLibraryAvailability Availability,
        GenesisRecordingLibraryFailure Failure, GenesisRecordingLibraryFileMetadata? Metadata);
    private sealed class EntryPathComparer : IComparer<GenesisRecordingLibraryEntry>
    {
        internal static EntryPathComparer Instance { get; } = new();
        public int Compare(GenesisRecordingLibraryEntry? x, GenesisRecordingLibraryEntry? y) => Paths.Compare(x?.Path, y?.Path);
    }
}
