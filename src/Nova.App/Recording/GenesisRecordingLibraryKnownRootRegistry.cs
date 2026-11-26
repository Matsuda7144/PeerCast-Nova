namespace PeerCast.Nova.App;

[Flags]
internal enum GenesisRecordingRootProvenance { Default = 1, StartupRecordingDirectory = 2, Explicit = 4 }
internal enum GenesisRecordingRootRegistrationResult { Added, AlreadyKnown, Updated, Invalid, CapacityReached }
internal sealed record GenesisRecordingLibraryKnownRoot(string Path, GenesisRecordingRootProvenance Provenance);
internal sealed record GenesisRecordingLibraryKnownRootSnapshot(
    long Revision, IReadOnlyList<GenesisRecordingLibraryKnownRoot> Roots)
{
    public int Count => Roots.Count;
    public int Capacity { get; } = GenesisRecordingLibraryKnownRootRegistry.MaximumRootCount;
}

internal sealed record GenesisRecordingMetadataSnapshot(
    GenesisRecordingHistorySnapshot History, GenesisRecordingLibraryKnownRootSnapshot KnownRoots)
{
    // Process-local composite revision. Neither component can decrease during
    // one writer's lifetime; hydration establishes the clean starting point.
    public long Revision => History.Revision + KnownRoots.Revision;
}

/// <summary>Additive coverage paths only. Registration never accesses the filesystem.</summary>
internal sealed class GenesisRecordingLibraryKnownRootRegistry
{
    internal const int MaximumRootCount = 64;
    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly object _gate = new();
    private readonly Dictionary<string, GenesisRecordingLibraryKnownRoot> _roots = new(PathComparer);
    private long _revision;
    // Like History, the sole subscriber only posts a capacity-one wake.
    internal event Action? Changed;

    public GenesisRecordingRootRegistrationResult RegisterExplicit(string? path) =>
        Register(path, GenesisRecordingRootProvenance.Explicit);

    internal GenesisRecordingRootRegistrationResult Register(string? path, GenesisRecordingRootProvenance provenance)
    {
        if (!TryNormalize(path, out var normalized) || !IsValidProvenance(provenance))
            return GenesisRecordingRootRegistrationResult.Invalid;
        lock (_gate)
        {
            if (_roots.TryGetValue(normalized!, out var existing))
            {
                var merged = existing.Provenance | provenance;
                if (merged == existing.Provenance) return GenesisRecordingRootRegistrationResult.AlreadyKnown;
                _roots[existing.Path] = existing with { Provenance = merged };
                _revision++;
                Changed?.Invoke();
                return GenesisRecordingRootRegistrationResult.Updated;
            }
            if (_roots.Count == MaximumRootCount) return GenesisRecordingRootRegistrationResult.CapacityReached;
            _roots.Add(normalized!, new(normalized!, provenance));
            _revision++;
            Changed?.Invoke();
            return GenesisRecordingRootRegistrationResult.Added;
        }
    }

    public GenesisRecordingLibraryKnownRootSnapshot GetSnapshot()
    {
        lock (_gate) return new(_revision, Array.AsReadOnly(_roots.Values.OrderBy(x => x.Path, PathComparer).ToArray()));
    }

    internal void Hydrate(GenesisRecordingLibraryKnownRootSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_revision != 0 || _roots.Count != 0) throw new InvalidOperationException("Roots already initialized.");
            foreach (var root in snapshot.Roots) _roots.Add(root.Path, root);
            _revision = _roots.Count == 0 ? 0 : 1;
        }
    }

    internal static bool IsValidProvenance(GenesisRecordingRootProvenance value) => value != 0 &&
        (value & ~(GenesisRecordingRootProvenance.Default | GenesisRecordingRootProvenance.StartupRecordingDirectory |
            GenesisRecordingRootProvenance.Explicit)) == 0;

    internal static bool TryNormalize(string? path, out string? normalized)
    {
        if (!GenesisRecordingOutputDirectorySettings.TryNormalize(path, out normalized)) return false;
        // Keep drive/filesystem roots intact while removing any number of
        // redundant ending separators. The settings policy itself is unchanged.
        while (true)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(normalized!);
            if (trimmed == normalized) return true;
            normalized = trimmed;
        }
    }
}
