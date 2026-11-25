using System.Collections.ObjectModel;

namespace PeerCast.Nova.App;

internal enum GenesisFavoriteChannelMutationResult
{
    Added,
    Updated,
    Unchanged,
}

/// <summary>
/// Persistent favorite identity plus bounded last-known display metadata. The
/// last-known source is retained for restoration and diagnostics only; callers
/// must use a live projection before treating a favorite as playable.
/// </summary>
internal sealed record GenesisFavoriteChannel(
    string StableKey,
    string Name,
    string LastKnownSource,
    string? Description,
    string? Type,
    string? ChannelId)
{
    public string? LastKnownYellowPage { get; init; }

    public bool Matches(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (string.Equals(
            StableKey,
            GenesisChannelIdentity.GetStableKey(channel),
            StringComparison.Ordinal))
        {
            return true;
        }

        return MatchesName(channel.Name);
    }

    public bool MatchesName(string? channelName)
    {
        var normalizedFavoriteName = NormalizeMatchName(Name);
        var normalizedChannelName = NormalizeMatchName(channelName);
        return normalizedFavoriteName is not null &&
            normalizedChannelName is not null &&
            string.Equals(
                normalizedFavoriteName,
                normalizedChannelName,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static string? NormalizeMatchName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static GenesisFavoriteChannel FromChannel(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new(
            GenesisChannelIdentity.GetStableKey(channel),
            channel.Name,
            channel.Source,
            channel.Description,
            channel.Type,
            channel.ChannelId)
        {
            LastKnownYellowPage = channel.YellowPage,
        };
    }
}

internal interface IGenesisFavoriteChannelStore
{
    ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        IReadOnlyList<GenesisFavoriteChannel> favorites,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thread-safe application-owned favorite set. Order is stable, adding an
/// existing identity refreshes its last-known metadata in place, and snapshots
/// are immutable from subsequent edits.
/// </summary>
internal sealed class GenesisFavoriteChannelCollection
{
    private readonly object _sync = new();
    private readonly List<GenesisFavoriteChannel> _favorites = [];
    private Func<IReadOnlyList<GenesisFavoriteChannel>, bool>? _persistExplicitMutation;

    public GenesisFavoriteChannelCollection()
    {
    }

    public GenesisFavoriteChannelCollection(
        IEnumerable<GenesisFavoriteChannel> favorites)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        foreach (var favorite in favorites)
        {
            ArgumentNullException.ThrowIfNull(favorite);
            if (FindIndex(favorite.StableKey) >= 0)
            {
                throw new ArgumentException(
                    "Favorite channel identities must be unique.",
                    nameof(favorites));
            }

            _favorites.Add(favorite);
        }
    }

    public ReadOnlyCollection<GenesisFavoriteChannel> Snapshot
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(_favorites.ToArray());
            }
        }
    }

    internal void BindPersistence(
        Func<IReadOnlyList<GenesisFavoriteChannel>, bool> persistExplicitMutation)
    {
        ArgumentNullException.ThrowIfNull(persistExplicitMutation);
        lock (_sync)
        {
            _persistExplicitMutation = persistExplicitMutation;
        }
    }

    public GenesisFavoriteChannelMutationResult AddOrUpdate(
        GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var favorite = GenesisFavoriteChannel.FromChannel(channel);
        lock (_sync)
        {
            var index = FindMatchIndex(channel);
            if (index < 0)
            {
                var next = new List<GenesisFavoriteChannel>(_favorites)
                {
                    favorite,
                };
                if (!TryPersistExplicitMutation(next))
                {
                    return GenesisFavoriteChannelMutationResult.Unchanged;
                }

                _favorites.Add(favorite);
                return GenesisFavoriteChannelMutationResult.Added;
            }

            if (Equals(_favorites[index], favorite))
            {
                return GenesisFavoriteChannelMutationResult.Unchanged;
            }

            var updated = new List<GenesisFavoriteChannel>(_favorites);
            updated[index] = favorite;
            if (!TryPersistExplicitMutation(updated))
            {
                return GenesisFavoriteChannelMutationResult.Unchanged;
            }

            _favorites[index] = favorite;
            return GenesisFavoriteChannelMutationResult.Updated;
        }
    }

    public bool Remove(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (_sync)
        {
            var index = FindMatchIndex(channel);
            if (index < 0)
            {
                return false;
            }

            var next = new List<GenesisFavoriteChannel>(_favorites);
            next.RemoveAt(index);
            if (!TryPersistExplicitMutation(next))
            {
                return false;
            }

            _favorites.RemoveAt(index);
            return true;
        }
    }

    public bool Remove(string stableKey)
    {
        ArgumentNullException.ThrowIfNull(stableKey);
        lock (_sync)
        {
            var index = FindIndex(stableKey);
            if (index < 0)
            {
                return false;
            }

            var next = new List<GenesisFavoriteChannel>(_favorites);
            next.RemoveAt(index);
            if (!TryPersistExplicitMutation(next))
            {
                return false;
            }

            _favorites.RemoveAt(index);
            return true;
        }
    }

    public bool Contains(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (_sync)
        {
            return FindMatchIndex(channel) >= 0;
        }
    }

    public bool RefreshKnownMetadata(IEnumerable<GenesisChannelItem> currentChannels)
    {
        ArgumentNullException.ThrowIfNull(currentChannels);
        var currentByIdentity = new Dictionary<string, GenesisChannelItem>(
            StringComparer.Ordinal);
        var currentByName = new Dictionary<string, GenesisChannelItem>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var channel in currentChannels)
        {
            ArgumentNullException.ThrowIfNull(channel);
            currentByIdentity.TryAdd(
                GenesisChannelIdentity.GetStableKey(channel),
                channel);
            if (GenesisFavoriteChannel.NormalizeMatchName(channel.Name) is { } normalizedName)
            {
                currentByName.TryAdd(normalizedName, channel);
            }
        }

        var changed = false;
        lock (_sync)
        {
            for (var index = 0; index < _favorites.Count; index++)
            {
                var current = _favorites[index];
                if (!TryFindCurrentChannel(
                        current,
                        currentByIdentity,
                        currentByName,
                        out var channel))
                {
                    continue;
                }

                var refreshed = GenesisFavoriteChannel.FromChannel(channel);
                if (Equals(current, refreshed))
                {
                    continue;
                }

                var duplicateIndex = FindIndex(refreshed.StableKey);
                if (duplicateIndex >= 0 && duplicateIndex != index)
                {
                    continue;
                }

                _favorites[index] = refreshed;
                changed = true;
            }
        }

        return changed;
    }

    public ReadOnlyCollection<GenesisFavoriteChannelProjectionEntry> Project(
        GenesisChannelPresenceSnapshot presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        return GenesisFavoriteChannelProjection.Create(Snapshot, presence);
    }

    public ReadOnlyCollection<GenesisFavoriteChannelProjectionEntry> Project(
        IEnumerable<GenesisChannelItem> currentChannels)
    {
        ArgumentNullException.ThrowIfNull(currentChannels);
        return GenesisFavoriteChannelProjection.Create(Snapshot, currentChannels);
    }

    private bool TryPersistExplicitMutation(
        IReadOnlyList<GenesisFavoriteChannel> favorites)
    {
        if (_persistExplicitMutation is null)
        {
            return true;
        }

        try
        {
            return _persistExplicitMutation(Array.AsReadOnly(favorites.ToArray()));
        }
        catch
        {
            return false;
        }
    }

    private int FindIndex(string stableKey)
    {
        for (var index = 0; index < _favorites.Count; index++)
        {
            if (string.Equals(
                _favorites[index].StableKey,
                stableKey,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private int FindMatchIndex(GenesisChannelItem channel)
    {
        var stableKeyIndex = FindIndex(GenesisChannelIdentity.GetStableKey(channel));
        if (stableKeyIndex >= 0)
        {
            return stableKeyIndex;
        }

        for (var index = 0; index < _favorites.Count; index++)
        {
            if (_favorites[index].MatchesName(channel.Name))
            {
                return index;
            }
        }

        return -1;
    }

    internal static bool TryFindCurrentChannel(
        GenesisFavoriteChannel favorite,
        Dictionary<string, GenesisChannelItem> currentByIdentity,
        Dictionary<string, GenesisChannelItem> currentByName,
        out GenesisChannelItem channel)
    {
        if (currentByIdentity.TryGetValue(favorite.StableKey, out channel!))
        {
            return true;
        }

        if (GenesisFavoriteChannel.NormalizeMatchName(favorite.Name) is { } normalizedName &&
            currentByName.TryGetValue(normalizedName, out channel!))
        {
            return true;
        }

        channel = null!;
        return false;
    }
}

internal sealed record GenesisFavoriteChannelProjectionEntry(
    GenesisFavoriteChannel Favorite,
    GenesisChannelItem? CurrentChannel)
{
    public bool IsOnline => CurrentChannel is not null;

    public string DisplayName => CurrentChannel?.Name ?? Favorite.Name;

    public string? PlayableSource => CurrentChannel?.Source;
}

internal static class GenesisFavoriteChannelProjection
{
    public static ReadOnlyCollection<GenesisFavoriteChannelProjectionEntry> Create(
        IReadOnlyList<GenesisFavoriteChannel> favorites,
        GenesisChannelPresenceSnapshot presence)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(presence);
        return Create(
            favorites,
            presence.Active
                .Where(static entry => entry.IsCurrentlyObserved)
                .Select(static entry => entry.Channel));
    }

    public static ReadOnlyCollection<GenesisFavoriteChannelProjectionEntry> Create(
        IReadOnlyList<GenesisFavoriteChannel> favorites,
        IEnumerable<GenesisChannelItem> currentChannels)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(currentChannels);

        var currentByIdentity = new Dictionary<string, GenesisChannelItem>(
            StringComparer.Ordinal);
        var currentByName = new Dictionary<string, GenesisChannelItem>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var channel in currentChannels)
        {
            ArgumentNullException.ThrowIfNull(channel);
            currentByIdentity.TryAdd(
                GenesisChannelIdentity.GetStableKey(channel),
                channel);
            if (GenesisFavoriteChannel.NormalizeMatchName(channel.Name) is { } normalizedName)
            {
                currentByName.TryAdd(normalizedName, channel);
            }
        }

        var projection = new GenesisFavoriteChannelProjectionEntry[favorites.Count];
        for (var index = 0; index < favorites.Count; index++)
        {
            var favorite = favorites[index];
            ArgumentNullException.ThrowIfNull(favorite);
            _ = GenesisFavoriteChannelCollection.TryFindCurrentChannel(
                favorite,
                currentByIdentity,
                currentByName,
                out var currentChannel);
            projection[index] = new(favorite, currentChannel);
        }

        return Array.AsReadOnly(projection);
    }
}
