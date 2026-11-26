namespace PeerCast.Nova.App;

internal sealed record GenesisYellowPageConfigurationReadSnapshot(
    long Revision,
    IReadOnlyList<GenesisYellowPageDefinition> EnabledDefinitions);

/// <summary>
/// Thread-safe application-facing Yellow Page configuration. The native settings
/// surface may edit this state while catalog refresh work reads stable snapshots.
/// Persistence remains behind an application-owned callback so configuration
/// mutations do not depend on a concrete store implementation.
/// </summary>
public sealed class GenesisYellowPageConfiguration
{
    private readonly object _sync = new();
    private readonly List<GenesisYellowPageDefinition> _definitions;
    private Func<IReadOnlyList<GenesisYellowPageDefinition>, bool>? _persistSnapshot;
    private long _revision;

    public GenesisYellowPageConfiguration(IEnumerable<GenesisYellowPageDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = [];
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (FindIndex(definition.Id) >= 0)
            {
                throw new ArgumentException(
                    "Yellow Page ids must be unique.",
                    nameof(definitions));
            }

            _definitions.Add(definition);
        }
    }

    public static GenesisYellowPageConfiguration CreateDefault() =>
        new([GenesisYellowPageDefaults.Sp]);

    public IReadOnlyList<GenesisYellowPageDefinition> Definitions
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(_definitions.ToArray());
            }
        }
    }

    public IReadOnlyList<GenesisYellowPageDefinition> EnabledDefinitions
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(
                    _definitions.Where(static definition => definition.IsEnabled).ToArray());
            }
        }
    }

    public bool Add(GenesisYellowPageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_sync)
        {
            if (FindIndex(definition.Id) >= 0)
            {
                return false;
            }

            _definitions.Add(definition);
            MarkChanged();
            return true;
        }
    }

    public bool Replace(GenesisYellowPageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_sync)
        {
            var index = FindIndex(definition.Id);
            if (index < 0)
            {
                return false;
            }

            if (Equals(_definitions[index], definition))
            {
                return true;
            }

            _definitions[index] = definition;
            MarkChanged();
            return true;
        }
    }

    public bool Remove(string id)
    {
        var normalizedId = NormalizeId(id);
        lock (_sync)
        {
            var index = FindIndex(normalizedId);
            if (index < 0)
            {
                return false;
            }

            _definitions.RemoveAt(index);
            MarkChanged();
            return true;
        }
    }

    public bool SetEnabled(string id, bool isEnabled)
    {
        var normalizedId = NormalizeId(id);
        lock (_sync)
        {
            var index = FindIndex(normalizedId);
            if (index < 0)
            {
                return false;
            }

            var current = _definitions[index];
            if (current.IsEnabled == isEnabled)
            {
                return true;
            }

            _definitions[index] = current with { IsEnabled = isEnabled };
            MarkChanged();
            return true;
        }
    }

    internal GenesisYellowPageConfigurationReadSnapshot CaptureEnabledReadSnapshot()
    {
        lock (_sync)
        {
            return new(
                _revision,
                Array.AsReadOnly(
                    _definitions.Where(static definition => definition.IsEnabled).ToArray()));
        }
    }

    internal bool IsRevisionCurrent(long revision)
    {
        lock (_sync)
        {
            return _revision == revision;
        }
    }

    internal void BindPersistence(
        Func<IReadOnlyList<GenesisYellowPageDefinition>, bool> persistSnapshot)
    {
        ArgumentNullException.ThrowIfNull(persistSnapshot);
        lock (_sync)
        {
            _persistSnapshot = persistSnapshot;
        }
    }

    internal bool TryPersistCurrentSnapshot()
    {
        Func<IReadOnlyList<GenesisYellowPageDefinition>, bool>? persistSnapshot;
        IReadOnlyList<GenesisYellowPageDefinition> snapshot;
        lock (_sync)
        {
            persistSnapshot = _persistSnapshot;
            snapshot = Array.AsReadOnly(_definitions.ToArray());
        }

        if (persistSnapshot is null)
        {
            return true;
        }

        try
        {
            return persistSnapshot(snapshot);
        }
        catch
        {
            return false;
        }
    }

    internal void RestoreSnapshot(IReadOnlyList<GenesisYellowPageDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var validated = new GenesisYellowPageConfiguration(definitions).Definitions;
        lock (_sync)
        {
            if (_definitions.SequenceEqual(validated))
            {
                return;
            }

            _definitions.Clear();
            _definitions.AddRange(validated);
            MarkChanged();
        }
    }

    private void MarkChanged() =>
        _revision = unchecked(_revision + 1);

    private int FindIndex(string id)
    {
        for (var index = 0; index < _definitions.Count; index++)
        {
            if (string.Equals(_definitions[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var normalized = id.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Yellow Page id is required.", nameof(id));
        }

        return normalized;
    }
}
