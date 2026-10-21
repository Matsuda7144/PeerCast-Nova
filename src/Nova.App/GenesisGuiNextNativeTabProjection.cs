using System.Collections.ObjectModel;

namespace PeerCast.Nova.App;

/// <summary>
/// One native list row for a GUI Next primary tab. Channel rows use
/// DisplayChannel for presentation metadata and InteractiveChannel only when the
/// current application state proves the row is a live playable catalog channel.
/// Rule rows carry their own immutable projection and are never interactive.
/// </summary>
internal sealed record GenesisGuiNextNativeTabRow(
    GenesisChannelItem DisplayChannel,
    GenesisChannelItem? InteractiveChannel,
    GenesisGuiNextActiveRecording? ActiveRecording = null,
    GenesisGuiNextRecordingRuleRow? RecordingRule = null,
    GenesisAutomaticRecordingSuppressionRow? Suppression = null,
    GenesisGuiNextRecordingLibraryRow? RecordingLibrary = null,
    GenesisGuiNextRecordingHistoryRow? RecordingHistory = null,
    string? StableSelectionKey = null,
    GenesisGuiNextLogRow? Log = null)
{
    public bool IsInteractive => InteractiveChannel is not null && RecordingRule is null && Suppression is null && RecordingLibrary is null && RecordingHistory is null && Log is null;

    public GenesisGuiNextActiveRecording? ActiveManualRecording =>
        ActiveRecording?.Origin == PeerCast.Nova.Core.Recording.RecordingSessionOrigin.Manual
            ? ActiveRecording
            : null;
}

/// <summary>
/// Framework-neutral adapter from the heterogeneous GUI Next tab projections to
/// the existing Genesis list. Channel rows keep offline favorites and playback
/// snapshots non-interactive; rule rows carry a separate typed projection so
/// rule data is never rendered through channel-specific columns.
/// </summary>
internal static class GenesisGuiNextNativeTabProjection
{
    internal static string GetRecordingEmptyStateText(
        bool snapshotAvailable,
        int activeRecordingCount) =>
        !snapshotAvailable
            ? "録画状態を確認できません"
            : activeRecordingCount > 0
                ? "録画中のチャンネルを選択してください"
                : "現在録画中のチャンネルはありません";

    public static IReadOnlyList<GenesisGuiNextNativeTabRow> Create(
        GenesisGuiNextTabsViewModel tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        return Create(tabs, tabs.SelectedTab);
    }

    internal static IReadOnlyList<GenesisGuiNextNativeTabRow> Create(
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextTab selectedTab)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        return selectedTab switch
        {
            GenesisGuiNextTab.Channels => ProjectChannels(tabs),
            GenesisGuiNextTab.New => CreateLiveChannels(tabs.VisibleNewChannels),
            GenesisGuiNextTab.Favorites => ProjectFavorites(tabs.VisibleFavoriteChannels),
            GenesisGuiNextTab.Playing => ProjectPlaying(tabs.Playing),
            GenesisGuiNextTab.Logs => Array.AsReadOnly(tabs.LogRows.Select(static row =>
                new GenesisGuiNextNativeTabRow(new(string.Empty, string.Empty), InteractiveChannel: null, Log: row)).ToArray()),
            GenesisGuiNextTab.Recording => tabs.RecordingPageMode switch
            {
                GenesisGuiNextRecordingPageMode.ActiveRecordings =>
                    Array.AsReadOnly(ProjectActiveRecordings(tabs.ActiveRecordings).Concat(
                        ProjectSuppressed(tabs)).ToArray()),
                GenesisGuiNextRecordingPageMode.Rules =>
                    ProjectRecordingRules(tabs.RecordingRuleRows),
                GenesisGuiNextRecordingPageMode.History =>
                    Array.AsReadOnly(tabs.RecordingHistoryRows.Select(static row =>
                        new GenesisGuiNextNativeTabRow(new(string.Empty, string.Empty),
                            InteractiveChannel: null, RecordingHistory: row)).ToArray()),
                GenesisGuiNextRecordingPageMode.Library =>
                    Array.AsReadOnly(tabs.RecordingLibraryRows.Select(static row =>
                        new GenesisGuiNextNativeTabRow(new(string.Empty, string.Empty),
                            InteractiveChannel: null, RecordingLibrary: row)).ToArray()),
                _ => Array.AsReadOnly(Array.Empty<GenesisGuiNextNativeTabRow>()),
            },
            _ => Array.AsReadOnly(Array.Empty<GenesisGuiNextNativeTabRow>()),
        };
    }

    private static IEnumerable<GenesisGuiNextNativeTabRow> ProjectSuppressed(GenesisGuiNextTabsViewModel tabs)
    {
        var currentNames = new Dictionary<PeerCast.Nova.Core.Channels.ChannelId, string>();
        foreach (var channel in tabs.Channels)
            if (GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) && channelId is not null)
                currentNames.TryAdd(channelId, channel.Name);

        return tabs.SuppressionRows.Where(e => e.State == GenesisAutomaticRecordingSuppressionState.Committed)
            .Select(e => new GenesisGuiNextNativeTabRow(
                new GenesisChannelItem(currentNames.GetValueOrDefault(e.ChannelId) ??
                    "一覧にないチャンネル · " + e.ChannelId.Value.ToString("N").ToUpperInvariant(),
                    string.Empty, Description: "自動録画を抑止中 · 停止時の対象ルール: " + e.RuleId.ToString("D") + " · " + e.StatusText,
                    Type: "自動録画を抑止中", ChannelId: e.ChannelId.Value.ToString("N").ToUpperInvariant()),
                InteractiveChannel: null, Suppression: e));
    }

    internal static ReadOnlyCollection<GenesisGuiNextNativeTabRow> CreateLiveChannels(
        IReadOnlyList<GenesisChannelItem> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        return Array.AsReadOnly(
            channels
                .Select(static channel =>
                    new GenesisGuiNextNativeTabRow(channel, channel))
                .ToArray());
    }

    internal static ReadOnlyCollection<GenesisGuiNextNativeTabRow> CreateNoticeRows(
        IReadOnlyList<GenesisYellowPageNoticeView> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        return Array.AsReadOnly(
            notices
                .Select(static notice =>
                {
                    var display = new GenesisChannelItem(
                        notice.Notice.Name,
                        string.Empty,
                        Description: notice.Notice.Description,
                        Type: notice.Notice.Type)
                    {
                        YellowPage = notice.YellowPageName,
                    };
                    return new GenesisGuiNextNativeTabRow(
                        display,
                        InteractiveChannel: null,
                        StableSelectionKey: notice.StableKey);
                })
                .ToArray());
    }

    private static ReadOnlyCollection<GenesisGuiNextNativeTabRow> ProjectChannels(
        GenesisGuiNextTabsViewModel tabs)
    {
        var rows = new List<GenesisGuiNextNativeTabRow>(
            tabs.VisibleChannels.Count + tabs.VisibleNotices.Count);
        rows.AddRange(CreateLiveChannels(tabs.VisibleChannels));
        rows.AddRange(CreateNoticeRows(tabs.VisibleNotices));
        return Array.AsReadOnly(rows.ToArray());
    }

    private static ReadOnlyCollection<GenesisGuiNextNativeTabRow> ProjectFavorites(
        IReadOnlyList<GenesisFavoriteChannelProjectionEntry> favorites) =>
        Array.AsReadOnly(
            favorites
                .Select(static favorite =>
                {
                    if (favorite.CurrentChannel is { } currentChannel)
                    {
                        return new GenesisGuiNextNativeTabRow(
                            currentChannel,
                            currentChannel);
                    }

                    var saved = favorite.Favorite;
                    return new GenesisGuiNextNativeTabRow(
                        new GenesisChannelItem(
                            saved.Name,
                            saved.LastKnownSource,
                            Description: saved.Description,
                            Type: saved.Type,
                            ChannelId: saved.ChannelId)
                        {
                            YellowPage = saved.LastKnownYellowPage,
                        },
                        InteractiveChannel: null);
                })
                .ToArray());

    private static ReadOnlyCollection<GenesisGuiNextNativeTabRow> ProjectPlaying(
        IReadOnlyList<GenesisGuiNextPlayingEntry> playing) =>
        Array.AsReadOnly(
            playing
                .Select(static entry =>
                {
                    var source = entry.Source ?? string.Empty;
                    var name = string.IsNullOrWhiteSpace(entry.ChannelName)
                        ? source
                        : entry.ChannelName;
                    var details = string.IsNullOrWhiteSpace(source)
                        ? entry.State.ToString()
                        : $"{entry.State} · {source}";
                    if (!string.IsNullOrWhiteSpace(entry.ErrorMessage))
                    {
                        details = $"{details} — {entry.ErrorMessage}";
                    }

                    return new GenesisGuiNextNativeTabRow(
                        new GenesisChannelItem(
                            name,
                            source,
                            Description: details,
                            ChannelId: entry.ChannelId),
                        InteractiveChannel: null);
                })
                .ToArray());

    private static ReadOnlyCollection<GenesisGuiNextNativeTabRow>
        ProjectActiveRecordings(
            IReadOnlyList<GenesisGuiNextActiveRecording> recordings) =>
        Array.AsReadOnly(
            recordings
                .Select(static recording =>
                {
                    var description = string.IsNullOrWhiteSpace(recording.ErrorMessage)
                        ? $"状態: {recording.StatusText}"
                        : $"状態: {recording.StatusText}  {recording.ErrorMessage}";
                    return new GenesisGuiNextNativeTabRow(
                        new GenesisChannelItem(
                            recording.ChannelName,
                            Source: string.Empty,
                            Description: description,
                            Type: recording.OriginLabel,
                            ChannelId: recording.ChannelId.Value.ToString("N")
                                .ToUpperInvariant()),
                        InteractiveChannel: null,
                        ActiveRecording: recording);
                })
                .ToArray());

    private static ReadOnlyCollection<GenesisGuiNextNativeTabRow>
        ProjectRecordingRules(
            IReadOnlyList<GenesisGuiNextRecordingRuleRow> rules) =>
        Array.AsReadOnly(
            rules
                .Select(static rule => new GenesisGuiNextNativeTabRow(
                    DisplayChannel: new GenesisChannelItem(string.Empty, string.Empty),
                    InteractiveChannel: null,
                    RecordingRule: rule))
                .ToArray());
}
