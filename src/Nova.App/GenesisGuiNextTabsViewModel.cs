using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisGuiNextTab
{
    Channels,
    New,
    Favorites,
    Playing,
    Recording,
    Publishing = 6,
    Logs = 8,
    RelayTree = 10,
}

internal sealed record GenesisGuiNextYellowPageFilterOption(
    string Id,
    string DisplayName);

/// <summary>
/// Truthful application-facing projection for one playback session in the
/// GUI Next Playing tab. Fields that the current playback contract cannot
/// prove are deliberately absent.
/// </summary>
internal sealed record GenesisGuiNextPlayingEntry(
    PlaybackSessionState State,
    string? Source,
    string? ErrorMessage,
    string? ChannelName,
    string? ChannelId);

internal sealed record GenesisGuiNextActiveRecording(
    ChannelId ChannelId,
    RecordingSessionId SessionId,
    RecordingSessionOrigin Origin,
    string ChannelName,
    string StatusText,
    Guid? AutomaticRuleId,
    string? ErrorMessage)
{
    public string OriginLabel => Origin switch
    {
        RecordingSessionOrigin.Manual => "手動録画",
        RecordingSessionOrigin.AutomaticRule => "自動録画",
        _ => "録画",
    };
}

/// <summary>
/// Framework-neutral state for the primary GUI Next pages. It reuses the
/// shared channel objects supplied by the presence/catalog projection, keeps
/// favorites as stable identities, and retains display-only YP notices outside
/// playable channel state.
/// </summary>
internal sealed partial class GenesisGuiNextTabsViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly GenesisFavoriteChannelCollection _favorites;
    private readonly GenesisRecordingRuleRepository _recordingRuleRepository;
    private readonly object _recordingRulesLifecycleGate = new();
    private ReadOnlyCollection<GenesisChannelItem> _channels =
        Array.AsReadOnly(Array.Empty<GenesisChannelItem>());
    private ReadOnlyCollection<GenesisChannelItem> _newChannels =
        Array.AsReadOnly(Array.Empty<GenesisChannelItem>());
    private ReadOnlyCollection<GenesisYellowPageNoticeView> _notices =
        Array.AsReadOnly(Array.Empty<GenesisYellowPageNoticeView>());
    private ReadOnlyCollection<GenesisFavoriteChannelProjectionEntry> _favoriteChannels =
        Array.AsReadOnly(Array.Empty<GenesisFavoriteChannelProjectionEntry>());
    private IReadOnlyList<GenesisChannelItem> _visibleChannels =
        Array.AsReadOnly(Array.Empty<GenesisChannelItem>());
    private IReadOnlyList<GenesisChannelItem> _visibleNewChannels =
        Array.AsReadOnly(Array.Empty<GenesisChannelItem>());
    private IReadOnlyList<GenesisYellowPageNoticeView> _visibleNotices =
        Array.AsReadOnly(Array.Empty<GenesisYellowPageNoticeView>());
    private IReadOnlyList<GenesisFavoriteChannelProjectionEntry> _visibleFavoriteChannels =
        Array.AsReadOnly(Array.Empty<GenesisFavoriteChannelProjectionEntry>());
    private ReadOnlyCollection<GenesisGuiNextPlayingEntry> _playing =
        Array.AsReadOnly(Array.Empty<GenesisGuiNextPlayingEntry>());
    private ReadOnlyCollection<GenesisGuiNextActiveRecording> _activeRecordings =
        Array.AsReadOnly(Array.Empty<GenesisGuiNextActiveRecording>());
    private bool _activeRecordingsAvailable;
    private ReadOnlyCollection<GenesisGuiNextYellowPageFilterOption> _yellowPageFilters =
        Array.AsReadOnly(Array.Empty<GenesisGuiNextYellowPageFilterOption>());
    private GenesisChannelPresenceSnapshot? _presence;
    private GenesisGuiNextTab _selectedTab;
    private GenesisGuiNextRecordingPageMode _recordingPageMode =
        GenesisGuiNextRecordingPageMode.ActiveRecordings;
    private IReadOnlyList<GenesisGuiNextRecordingRuleRow> _recordingRuleRows =
        Array.AsReadOnly(Array.Empty<GenesisGuiNextRecordingRuleRow>());
    private GenesisRecordingRuleRepositorySnapshot? _recordingRulesSnapshot;
    private CancellationTokenSource? _recordingRulesLoadCancellation;
    private TaskCompletionSource _recordingRulesLoadsDrained = CreateCompletedDrainSource();
    private int _activeRecordingRulesLoads;
    private long _recordingRulesLoadRevision;
    private bool _isRecordingRulesLoading;
    private bool _disposed;
    private string _recordingRuleMutationMessage = string.Empty;
    private string _searchText = string.Empty;
    private string? _selectedYellowPageId;

    public GenesisGuiNextTabsViewModel(
        GenesisFavoriteChannelCollection favorites)
        : this(favorites, new GenesisRecordingRuleRepository(path: null))
    {
    }

    public GenesisGuiNextTabsViewModel(
        GenesisFavoriteChannelCollection favorites,
        GenesisRecordingRuleRepository recordingRuleRepository,
        IGenesisRecordingLibraryController? recordingLibraryController = null,
        IGenesisRecordingHistoryController? recordingHistoryController = null)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(recordingRuleRepository);
        _favorites = favorites;
        _recordingRuleRepository = recordingRuleRepository;
        RuleMutations = new(recordingRuleRepository, PublishRecordingRulesSnapshot);
        RuleMutations.AvailabilityChanged += OnRuleMutationAvailabilityChanged;
        RuleEditor = new(RuleMutations);
        _selectedTab = GenesisGuiNextTab.Channels;
        RebuildFavorites();
        RebuildVisibleChannelViews();
        BindRecordingLibraryController(recordingLibraryController);
        BindRecordingHistoryController(recordingHistoryController);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GenesisRecordingRuleEditor RuleEditor { get; }
    public GenesisRecordingRuleMutationController RuleMutations { get; }
    public bool RecordingRuleMutationAvailable => RuleMutations.IsAvailable;

    public bool CanQuickMutateRecordingRule(Guid? id) => CanCreateRecordingRule &&
        id is { } selected && RecordingRulesSnapshot!.Rules!.Rules.Any(rule => rule.Id == selected);

    public bool CanMoveRecordingRule(Guid? id, GenesisRecordingRuleQuickAction action) =>
        CanQuickMutateRecordingRule(id) &&
        GenesisRecordingRuleMutationController.GetMoveTarget(RecordingRulesSnapshot!.Rules!, id!.Value, action) is not null;

    public string RecordingRuleToggleLabel(Guid? id) =>
        RecordingRulesSnapshot?.Rules?.Rules.FirstOrDefault(rule => rule.Id == id)?.IsEnabled == true
            ? "無効化" : "有効化";

    public async Task<GenesisRecordingRuleEditorResult> QuickMutateRecordingRuleAsync(Guid? id,
        GenesisRecordingRuleQuickAction action,
        Func<GenesisRecordingRuleConfirmation, CancellationToken, ValueTask<bool>> confirm)
    {
        if (!CanQuickMutateRecordingRule(id) ||
            (action is GenesisRecordingRuleQuickAction.MoveUp or GenesisRecordingRuleQuickAction.MoveDown &&
                !CanMoveRecordingRule(id, action))) return new(null, "現在は操作できません。");
        GenesisRecordingRuleRepositorySnapshot snapshot;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || _isRecordingRulesLoading || _recordingRulesSnapshot is null)
                return new(null, "現在は操作できません。");
            snapshot = RecordingRulesSnapshot!;
        }
        var result = await RuleMutations.ExecuteAsync(snapshot, id!.Value, action, confirm).ConfigureAwait(false);
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return result;
            _recordingRuleMutationMessage = result.Message;
        }
        OnPropertyChanged(nameof(RecordingRulesStatusText));
        return result;
    }

    private void OnRuleMutationAvailabilityChanged()
    {
        lock (_recordingRulesLifecycleGate) { if (_disposed) return; }
        OnPropertyChanged(nameof(RecordingRuleMutationAvailable));
    }

    public bool CanCreateRecordingRule => CanReloadRecordingRules && RuleMutations.IsAvailable &&
        SelectedTab == GenesisGuiNextTab.Recording &&
        RecordingPageMode == GenesisGuiNextRecordingPageMode.Rules &&
        !IsRecordingRulesLoading && RecordingRulesSnapshot is
        { IsAvailable: true, Rules: not null, Revision: not null };

    public bool CanEditRecordingRule(Guid? id) => CanCreateRecordingRule &&
        id is { } selected && RecordingRulesSnapshot!.Rules!.Rules.Any(
            rule => rule.Id == selected && GenesisRecordingRuleEditor.CanEdit(rule));

    public GenesisRecordingRuleDraft? CreateRecordingRule() => CanCreateRecordingRule
        ? RuleEditor.BeginCreate(RecordingRulesSnapshot) : null;

    public GenesisRecordingRuleDraft? EditRecordingRule(Guid? id) =>
        CanEditRecordingRule(id) ? RuleEditor.BeginEdit(RecordingRulesSnapshot, id!.Value) : null;

    private void PublishRecordingRulesSnapshot(GenesisRecordingRuleRepositorySnapshot snapshot, Guid? selectedId)
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return;
            _recordingRulesLoadCancellation?.Cancel();
            ++_recordingRulesLoadRevision;
            _recordingRulesSnapshot = snapshot;
            _recordingRuleMutationMessage = string.Empty;
            _recordingRuleRows = snapshot.IsAvailable && snapshot.Rules is { } rules
                ? GenesisGuiNextRecordingRuleProjection.CreateRows(rules)
                : Array.AsReadOnly(Array.Empty<GenesisGuiNextRecordingRuleRow>());
            _isRecordingRulesLoading = false;
            RecordingRuleSelectionAfterSave = selectedId;
        }
        OnPropertyChanged(nameof(RecordingRuleRows));
        OnPropertyChanged(nameof(RecordingRulesSnapshot));
        OnPropertyChanged(nameof(RecordingRulesStatus));
        OnPropertyChanged(nameof(RecordingRulesRevision));
        OnPropertyChanged(nameof(RecordingRulesFailureKind));
        OnPropertyChanged(nameof(IsRecordingRulesLoading));
        OnPropertyChanged(nameof(RecordingRulesStatusText));
    }

    // Consumed once by the existing native RuleId selection restoration boundary.
    public Guid? RecordingRuleSelectionAfterSave { get; private set; }
    public void ClearRecordingRuleSelectionAfterSave() => RecordingRuleSelectionAfterSave = null;

    public GenesisGuiNextTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value)
            {
                return;
            }

            _selectedTab = value;
            if (value != GenesisGuiNextTab.Publishing) MaskPublishingKey();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefreshRecordingLibrary));
            RecordingHistoryPageChanged();
        }
    }

    public GenesisGuiNextRecordingPageMode RecordingPageMode
    {
        get => _recordingPageMode;
        private set
        {
            if (_recordingPageMode == value)
            {
                return;
            }

            _recordingPageMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefreshRecordingLibrary));
            RecordingHistoryPageChanged();
        }
    }

    public IReadOnlyList<GenesisGuiNextRecordingRuleRow> RecordingRuleRows =>
        _recordingRuleRows;

    public GenesisRecordingRuleRepositorySnapshot? RecordingRulesSnapshot =>
        _recordingRulesSnapshot;

    public bool IsRecordingRulesLoading => _isRecordingRulesLoading;

    public bool CanReloadRecordingRules
    {
        get
        {
            lock (_recordingRulesLifecycleGate)
            {
                return !_disposed;
            }
        }
    }

    public GenesisRecordingRuleSnapshotStatus? RecordingRulesStatus =>
        _recordingRulesSnapshot?.Status;

    public GenesisRecordingRuleRevision? RecordingRulesRevision =>
        _recordingRulesSnapshot?.Revision;

    public GenesisRecordingRuleRepositoryFailureKind? RecordingRulesFailureKind =>
        _recordingRulesSnapshot?.FailureKind;

    public string RecordingRulesStatusText => _isRecordingRulesLoading
        ? "録画ルールを読み込み中…"
        : _recordingRuleMutationMessage.Length != 0 ? _recordingRuleMutationMessage
        : _recordingRulesSnapshot is { } snapshot
            ? GenesisGuiNextRecordingRuleProjection.StatusText(
                snapshot.Status,
                snapshot.FailureKind,
                _recordingRuleRows.Count)
            : "録画ルールを確認できません";

    public async Task SelectRecordingPageModeAsync(
        GenesisGuiNextRecordingPageMode mode)
    {
        lock (_recordingRulesLifecycleGate) { if (_disposed) return; }
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var modeChanged = RecordingPageMode != mode; RecordingPageMode = mode;
        if (mode == GenesisGuiNextRecordingPageMode.History)
            await (modeChanged ? _recordingHistoryDrained.Task : RefreshRecordingHistoryAsync()).ConfigureAwait(false);
        if (mode == GenesisGuiNextRecordingPageMode.Library &&
            RecordingLibrarySnapshot.State == GenesisRecordingLibraryObservationState.NotObservedYet)
        {
            await RefreshRecordingLibraryAsync().ConfigureAwait(false);
        }
        if (mode == GenesisGuiNextRecordingPageMode.Rules &&
            SelectedTab == GenesisGuiNextTab.Recording)
        {
            await ReloadRecordingRulesAsync().ConfigureAwait(false);
        }
    }

    public Task ReloadRecordingRulesAsync()
    {
        CancellationTokenSource cancellation;
        long loadRevision;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _recordingRulesLoadCancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            _recordingRulesLoadCancellation = cancellation;
            loadRevision = ++_recordingRulesLoadRevision;
            if (_activeRecordingRulesLoads == 0)
            {
                _recordingRulesLoadsDrained = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _activeRecordingRulesLoads++;
        }

        _recordingRuleMutationMessage = string.Empty;
        _isRecordingRulesLoading = true;
        _recordingRulesSnapshot = null;
        _recordingRuleRows = Array.AsReadOnly(
            Array.Empty<GenesisGuiNextRecordingRuleRow>());
        OnPropertyChanged(nameof(IsRecordingRulesLoading));
        OnPropertyChanged(nameof(RecordingRulesSnapshot));
        OnPropertyChanged(nameof(RecordingRulesStatus));
        OnPropertyChanged(nameof(RecordingRulesRevision));
        OnPropertyChanged(nameof(RecordingRulesFailureKind));
        OnPropertyChanged(nameof(RecordingRuleRows));
        OnPropertyChanged(nameof(RecordingRulesStatusText));
        return LoadRecordingRulesCoreAsync(loadRevision, cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        ClosePublishingView();
        Task drained;
        Task libraryDrained;
        CancellationTokenSource? libraryCancellation;
        CancellationTokenSource? cancellation;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed)
            {
                drained = _recordingRulesLoadsDrained.Task;
                cancellation = null;
            }
            else
            {
                _disposed = true;
                cancellation = _recordingRulesLoadCancellation;
                drained = _recordingRulesLoadsDrained.Task;
            }
            libraryCancellation = _recordingLibraryCancellation;
            libraryDrained = _recordingLibraryDrained.Task;
        }

        var historyDrained = CancelRecordingHistoryForDisposal();
        cancellation?.Cancel();
        try { libraryCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException)
        {
            // A controller's cancellation callback cannot prevent lifecycle drain.
        }
        await RuleEditor.DisposeAsync().ConfigureAwait(false);
        await RuleMutations.DisposeAsync().ConfigureAwait(false);
        RuleMutations.AvailabilityChanged -= OnRuleMutationAvailabilityChanged;
        await drained.ConfigureAwait(false);
        await libraryDrained.ConfigureAwait(false);
        await historyDrained.ConfigureAwait(false);
        _recordingHistoryCancellation.Dispose();
        cancellation?.Dispose();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_searchText, next, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = next;
            OnPropertyChanged();
            RebuildVisibleChannelViews();
        }
    }

    public string? SelectedYellowPageId
    {
        get => _selectedYellowPageId;
        set
        {
            var next = NormalizeYellowPageId(value);
            if (next is not null && !ContainsYellowPageFilter(next))
            {
                next = null;
            }

            if (string.Equals(
                _selectedYellowPageId,
                next,
                StringComparison.Ordinal))
            {
                return;
            }

            _selectedYellowPageId = next;
            OnPropertyChanged();
            RebuildVisibleChannelViews();
        }
    }

    public IReadOnlyList<GenesisChannelItem> Channels => _channels;

    public IReadOnlyList<GenesisChannelItem> NewChannels => _newChannels;

    public IReadOnlyList<GenesisYellowPageNoticeView> Notices => _notices;

    public IReadOnlyList<GenesisFavoriteChannelProjectionEntry> FavoriteChannels =>
        _favoriteChannels;

    public IReadOnlyList<GenesisChannelItem> VisibleChannels => _visibleChannels;

    public IReadOnlyList<GenesisChannelItem> VisibleNewChannels => _visibleNewChannels;

    public IReadOnlyList<GenesisYellowPageNoticeView> VisibleNotices => _visibleNotices;

    public IReadOnlyList<GenesisFavoriteChannelProjectionEntry> VisibleFavoriteChannels =>
        _visibleFavoriteChannels;

    public IReadOnlyList<GenesisGuiNextPlayingEntry> Playing => _playing;

    public IReadOnlyList<GenesisGuiNextActiveRecording> ActiveRecordings => _activeRecordings;

    public IReadOnlyList<GenesisGuiNextActiveRecording> ActiveManualRecordings =>
        Array.AsReadOnly(_activeRecordings
            .Where(static recording => recording.Origin == RecordingSessionOrigin.Manual)
            .ToArray());

    public bool ActiveRecordingsAvailable => _activeRecordingsAvailable;

    public IReadOnlyList<GenesisGuiNextYellowPageFilterOption> YellowPageFilters =>
        _yellowPageFilters;

    public int ChannelCount => _channels.Count;

    public int NewChannelCount => _newChannels.Count;

    public int NoticeCount => _notices.Count;

    public int FavoriteChannelCount => _favoriteChannels.Count;

    public int VisibleChannelCount => _visibleChannels.Count;

    public int VisibleNewChannelCount => _visibleNewChannels.Count;

    public int VisibleNoticeCount => _visibleNotices.Count;

    public int VisibleFavoriteChannelCount => _visibleFavoriteChannels.Count;

    public int PlayingCount => _playing.Count;

    public int ActiveRecordingCount => _activeRecordings.Count;

    public int ActiveManualRecordingCount => ActiveManualRecordings.Count;

    private async Task LoadRecordingRulesCoreAsync(
        long loadRevision,
        CancellationTokenSource cancellation)
    {
        try
        {
            GenesisRecordingRuleRepositorySnapshot snapshot;
            try
            {
                snapshot = await _recordingRuleRepository
                    .GetSnapshotAsync(cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                snapshot = new(
                    GenesisRecordingRuleSnapshotStatus.Cancelled,
                    Rules: null,
                    Revision: null,
                    GenesisRecordingRuleRepositoryFailureKind.Cancelled);
            }
            catch
            {
                snapshot = new(
                    GenesisRecordingRuleSnapshotStatus.Unavailable,
                    Rules: null,
                    Revision: null,
                    GenesisRecordingRuleRepositoryFailureKind.Io);
            }

            lock (_recordingRulesLifecycleGate)
            {
                if (_disposed || loadRevision != _recordingRulesLoadRevision)
                {
                    return;
                }

                _recordingRulesSnapshot = snapshot;
                _recordingRuleRows = snapshot.IsAvailable && snapshot.Rules is { } rules
                    ? GenesisGuiNextRecordingRuleProjection.CreateRows(rules)
                    : Array.AsReadOnly(
                        Array.Empty<GenesisGuiNextRecordingRuleRow>());
                _isRecordingRulesLoading = false;
            }

            OnPropertyChanged(nameof(IsRecordingRulesLoading));
            OnPropertyChanged(nameof(RecordingRulesSnapshot));
            OnPropertyChanged(nameof(RecordingRulesStatus));
            OnPropertyChanged(nameof(RecordingRulesRevision));
            OnPropertyChanged(nameof(RecordingRulesFailureKind));
            OnPropertyChanged(nameof(RecordingRuleRows));
            OnPropertyChanged(nameof(RecordingRulesStatusText));
        }
        finally
        {
            cancellation.Dispose();
            TaskCompletionSource? drained = null;
            lock (_recordingRulesLifecycleGate)
            {
                if (ReferenceEquals(_recordingRulesLoadCancellation, cancellation))
                {
                    _recordingRulesLoadCancellation = null;
                }

                _activeRecordingRulesLoads--;
                if (_activeRecordingRulesLoads == 0)
                {
                    drained = _recordingRulesLoadsDrained;
                }
            }

            drained?.TrySetResult();
        }
    }

    private static TaskCompletionSource CreateCompletedDrainSource()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    public void ApplyChannelViews(
        GenesisChannelViewProjectionSnapshot views,
        GenesisChannelPresenceSnapshot? presence = null)
    {
        ArgumentNullException.ThrowIfNull(views);
        _channels = Array.AsReadOnly(views.Channels.ToArray());
        _newChannels = Array.AsReadOnly(views.NewChannels.ToArray());
        _notices = Array.AsReadOnly(views.Notices.ToArray());
        _presence = presence;
        RefreshFavoriteMetadata();
        RebuildYellowPageFilters();
        RebuildFavorites();

        OnPropertyChanged(nameof(Channels));
        OnPropertyChanged(nameof(ChannelCount));
        OnPropertyChanged(nameof(NewChannels));
        OnPropertyChanged(nameof(NewChannelCount));
        OnPropertyChanged(nameof(Notices));
        OnPropertyChanged(nameof(NoticeCount));
        RebuildVisibleChannelViews();
    }

    public GenesisFavoriteChannelMutationResult AddFavorite(
        GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var result = _favorites.AddOrUpdate(channel);
        if (result != GenesisFavoriteChannelMutationResult.Unchanged)
        {
            RebuildFavorites();
            RebuildVisibleChannelViews();
        }

        return result;
    }

    public bool RemoveFavorite(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var removed = _favorites.Remove(channel);
        if (removed)
        {
            RebuildFavorites();
            RebuildVisibleChannelViews();
        }

        return removed;
    }

    public bool IsFavorite(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return _favorites.Contains(channel);
    }

    public void UpdatePlayback(PlaybackSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var next = snapshot.State is not (PlaybackSessionState.Starting or
            PlaybackSessionState.Playing or
            PlaybackSessionState.Stopping)
                ? Array.AsReadOnly(Array.Empty<GenesisGuiNextPlayingEntry>())
                : Array.AsReadOnly(new[]
            {
                new GenesisGuiNextPlayingEntry(
                    snapshot.State,
                    snapshot.Source,
                    snapshot.ErrorMessage,
                    snapshot.ChannelName,
                    snapshot.ChannelId),
            });

        if (_playing.SequenceEqual(next))
        {
            return;
        }

        _playing = next;
        OnPropertyChanged(nameof(Playing));
        OnPropertyChanged(nameof(PlayingCount));
    }

    public void UpdateActiveRecordings(
        IReadOnlyList<GenesisGuiNextActiveRecording> recordings,
        bool isAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        var next = Array.AsReadOnly(recordings.ToArray());
        if (_activeRecordings.SequenceEqual(next) &&
            _activeRecordingsAvailable == isAvailable)
        {
            return;
        }

        _activeRecordings = next;
        _activeRecordingsAvailable = isAvailable;
        OnPropertyChanged(nameof(ActiveRecordings));
        OnPropertyChanged(nameof(ActiveRecordingCount));
        OnPropertyChanged(nameof(ActiveRecordingsAvailable));
        OnPropertyChanged(nameof(ActiveManualRecordings));
        OnPropertyChanged(nameof(ActiveManualRecordingCount));
    }

    internal IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> SuppressionRows { get; private set; } = [];

    internal void UpdateSuppression(IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> rows)
    {
        if (SuppressionRows.SequenceEqual(rows)) return;
        SuppressionRows = Array.AsReadOnly(rows.ToArray());
        OnPropertyChanged(nameof(SuppressionRows));
        OnPropertyChanged(nameof(ActiveRecordings));
    }

    public void UpdateActiveManualRecordings(
        IReadOnlyList<GenesisGuiNextActiveRecording> recordings) =>
        UpdateActiveRecordings(recordings);

    private void RefreshFavoriteMetadata()
    {
        IEnumerable<GenesisChannelItem> currentChannels = _channels;
        if (_presence is not null)
        {
            currentChannels = _presence.Entries
                .Where(static entry => entry.IsCurrentlyObserved)
                .Select(static entry => entry.Channel);
        }

        // Catalog-driven metadata is auxiliary to the successful refresh. Update the
        // in-memory last-known copy now, but leave disk persistence coalesced to the
        // next explicit favorite mutation or the application's final shutdown save.
        _favorites.RefreshKnownMetadata(currentChannels);
    }

    private void RebuildFavorites()
    {
        _favoriteChannels = _presence is null
            ? _favorites.Project(_channels)
            : _favorites.Project(_presence);
        OnPropertyChanged(nameof(FavoriteChannels));
        OnPropertyChanged(nameof(FavoriteChannelCount));
    }

    private void RebuildYellowPageFilters()
    {
        _yellowPageFilters = BuildYellowPageFilters(_presence);
        OnPropertyChanged(nameof(YellowPageFilters));

        if (_selectedYellowPageId is not null &&
            !ContainsYellowPageFilter(_selectedYellowPageId))
        {
            _selectedYellowPageId = null;
            OnPropertyChanged(nameof(SelectedYellowPageId));
        }
    }

    private void RebuildVisibleChannelViews()
    {
        var stableKeys = BuildSelectedYellowPageStableKeys();
        var channels = FilterChannelsByYellowPage(_channels, stableKeys);
        var newChannels = FilterChannelsByYellowPage(_newChannels, stableKeys);
        var notices = FilterNoticesByYellowPage(_notices, _selectedYellowPageId);
        var favorites = FilterFavoritesByYellowPage(_favoriteChannels, stableKeys);

        _visibleChannels = GenesisChannelSearchFilter.Apply(channels, _searchText);
        _visibleNewChannels = GenesisChannelSearchFilter.Apply(newChannels, _searchText);
        _visibleNotices = GenesisChannelSearchFilter.ApplyNotices(notices, _searchText);
        _visibleFavoriteChannels = GenesisChannelSearchFilter.ApplyFavorites(
            favorites,
            _searchText);

        OnPropertyChanged(nameof(VisibleChannels));
        OnPropertyChanged(nameof(VisibleChannelCount));
        OnPropertyChanged(nameof(VisibleNewChannels));
        OnPropertyChanged(nameof(VisibleNewChannelCount));
        OnPropertyChanged(nameof(VisibleNotices));
        OnPropertyChanged(nameof(VisibleNoticeCount));
        OnPropertyChanged(nameof(VisibleFavoriteChannels));
        OnPropertyChanged(nameof(VisibleFavoriteChannelCount));
    }

    private HashSet<string>? BuildSelectedYellowPageStableKeys()
    {
        if (_selectedYellowPageId is null)
        {
            return null;
        }

        var stableKeys = new HashSet<string>(StringComparer.Ordinal);
        if (_presence is null)
        {
            return stableKeys;
        }

        foreach (var entry in _presence.Entries)
        {
            if (entry.KnownYellowPageIds.Contains(
                    _selectedYellowPageId,
                    StringComparer.Ordinal))
            {
                stableKeys.Add(entry.StableKey);
            }
        }

        return stableKeys;
    }

    private bool ContainsYellowPageFilter(string yellowPageId)
    {
        foreach (var option in _yellowPageFilters)
        {
            if (string.Equals(option.Id, yellowPageId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ReadOnlyCollection<GenesisGuiNextYellowPageFilterOption> BuildYellowPageFilters(
        GenesisChannelPresenceSnapshot? presence)
    {
        if (presence is null || presence.YellowPages.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<GenesisGuiNextYellowPageFilterOption>());
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<GenesisGuiNextYellowPageFilterOption>();
        foreach (var status in presence.YellowPages)
        {
            if (seen.Add(status.Definition.Id))
            {
                options.Add(new(
                    status.Definition.Id,
                    status.Definition.Name));
            }
        }

        return Array.AsReadOnly(options.ToArray());
    }

    private static ReadOnlyCollection<GenesisChannelItem> FilterChannelsByYellowPage(
        IReadOnlyList<GenesisChannelItem> channels,
        HashSet<string>? stableKeys)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (stableKeys is null)
        {
            return Array.AsReadOnly(channels.ToArray());
        }

        return Array.AsReadOnly(
            channels
                .Where(channel => stableKeys.Contains(
                    GenesisChannelIdentity.GetStableKey(channel)))
                .ToArray());
    }

    private static ReadOnlyCollection<GenesisYellowPageNoticeView> FilterNoticesByYellowPage(
        IReadOnlyList<GenesisYellowPageNoticeView> notices,
        string? yellowPageId)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (yellowPageId is null)
        {
            return Array.AsReadOnly(notices.ToArray());
        }

        return Array.AsReadOnly(
            notices
                .Where(notice => string.Equals(
                    notice.YellowPageId,
                    yellowPageId,
                    StringComparison.Ordinal))
                .ToArray());
    }

    private static ReadOnlyCollection<GenesisFavoriteChannelProjectionEntry> FilterFavoritesByYellowPage(
        IReadOnlyList<GenesisFavoriteChannelProjectionEntry> favorites,
        HashSet<string>? stableKeys)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        if (stableKeys is null)
        {
            return Array.AsReadOnly(favorites.ToArray());
        }

        return Array.AsReadOnly(
            favorites
                .Where(favorite =>
                    stableKeys.Contains(favorite.Favorite.StableKey) ||
                    (favorite.CurrentChannel is { } current &&
                        stableKeys.Contains(GenesisChannelIdentity.GetStableKey(current))))
                .ToArray());
    }

    private static string? NormalizeYellowPageId(string? yellowPageId)
    {
        if (string.IsNullOrWhiteSpace(yellowPageId))
        {
            return null;
        }

        return yellowPageId.Trim();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
