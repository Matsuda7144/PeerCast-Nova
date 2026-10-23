namespace PeerCast.Nova.App;

/// <summary>
/// Framework-neutral interaction boundary for the Genesis main window. Native
/// controls translate user gestures into these operations; playback remains
/// behind the application-facing session controller owned by the view model.
/// </summary>
internal sealed class GenesisMainWindowInteraction
{
    private readonly GenesisMainViewModel _viewModel;
    private readonly GenesisChannelCatalogSession? _channelCatalogSession;

    public GenesisMainWindowInteraction(GenesisMainViewModel viewModel)
        : this(
            viewModel,
            GenesisChannelRefreshInteractionScope.Find(viewModel))
    {
    }

    internal GenesisMainWindowInteraction(
        GenesisMainViewModel viewModel,
        GenesisChannelCatalogSession? channelCatalogSession)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _channelCatalogSession = channelCatalogSession;
    }

    public GenesisMainViewModel ViewModel => _viewModel;

    internal bool IsRecordingHistoryView => _channelCatalogSession?.GuiNextTabs?.IsRecordingHistoryPage == true;

    internal bool IsPublishingView => _channelCatalogSession?.GuiNextTabs?.SelectedTab == GenesisGuiNextTab.Publishing;

    internal bool IsYellowPageCatalogView
    {
        get
        {
            var tabs = _channelCatalogSession?.GuiNextTabs;
            return _channelCatalogSession is not null &&
                tabs?.SelectedTab is not (GenesisGuiNextTab.Logs or GenesisGuiNextTab.Publishing) &&
                tabs?.IsRecordingHistoryPage != true &&
                !IsRecordingRulesView(tabs);
        }
    }

    public bool CanRefreshCurrentView
    {
        get
        {
            var tabs = _channelCatalogSession?.GuiNextTabs;
            if (tabs?.IsRecordingHistoryPage == true) return tabs.CanRefreshRecordingHistory;
            if (IsRecordingRulesView(tabs))
            {
                return tabs!.CanReloadRecordingRules;
            }

            return _channelCatalogSession is not null;
        }
    }

    public async ValueTask PlaySourceAsync(
        string? source,
        CancellationToken cancellationToken = default)
    {
        if (IsRecordingHistoryView || IsPublishingView || !_viewModel.CanPlay)
        {
            return;
        }

        _viewModel.SourceText = source?.Trim() ?? string.Empty;
        await _viewModel.PlayAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
        (IsRecordingHistoryView || IsPublishingView) ? ValueTask.CompletedTask : _viewModel.StopAsync(cancellationToken);

    public async ValueTask RefreshChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_channelCatalogSession is not null)
        {
            _ = await _channelCatalogSession.RefreshAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask RefreshCurrentViewAsync(
        CancellationToken cancellationToken = default)
    {
        await RefreshCurrentViewCoreAsync(
                refreshRequestId: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    internal ValueTask RefreshCurrentViewAsync(Guid refreshRequestId) =>
        RefreshCurrentViewCoreAsync(refreshRequestId, CancellationToken.None);

    internal void RecordRefreshSuppressed(Guid refreshRequestId, string reason)
    {
        if (!IsYellowPageCatalogView || _channelCatalogSession?.DiagnosticTrace is not { } trace)
        {
            return;
        }

        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace,
            refreshRequestId,
            GenesisYellowPageRefreshTrigger.Manual);
        trace.TryRecord(
            "RefreshRequested",
            context,
            stage: "UiCommand",
            outcome: "Requested");
        trace.TryRecord(
            "RefreshSuppressed",
            context,
            stage: "UiCommand",
            outcome: "Suppressed",
            suppressionReason: reason);
    }

    private async ValueTask RefreshCurrentViewCoreAsync(
        Guid? refreshRequestId,
        CancellationToken cancellationToken)
    {
        var tabs = _channelCatalogSession?.GuiNextTabs;
        if (tabs?.SelectedTab == GenesisGuiNextTab.Publishing)
        {
            if (!cancellationToken.IsCancellationRequested) tabs.RefreshPublishingStatus();
            return;
        }
        if (tabs?.SelectedTab == GenesisGuiNextTab.Logs)
        {
            if (!cancellationToken.IsCancellationRequested)
                tabs.UpdateLogDiagnostics(_channelCatalogSession!.Diagnostics, _viewModel.IsChannelCatalogRefreshing);
            return;
        }
        if (tabs?.IsRecordingHistoryPage == true)
        {
            await tabs.RefreshRecordingHistoryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (IsRecordingRulesView(tabs))
        {
            if (tabs!.CanReloadRecordingRules)
            {
                await tabs.ReloadRecordingRulesAsync().ConfigureAwait(false);
            }

            return;
        }

        if (refreshRequestId is { } requestId && _channelCatalogSession is not null)
        {
            _ = await _channelCatalogSession.RefreshAsync(requestId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await RefreshChannelsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRecordingRulesView(GenesisGuiNextTabsViewModel? tabs) =>
        tabs is
        {
            SelectedTab: GenesisGuiNextTab.Recording,
            RecordingPageMode: GenesisGuiNextRecordingPageMode.Rules,
        };

    public bool ShowVideoOnly()
    {
        if (!_viewModel.IsPlaybackWindowOpen)
        {
            return false;
        }

        _viewModel.ShowVideoOnly();
        return true;
    }

    public bool ShowFullscreen()
    {
        if (!_viewModel.IsPlaybackWindowOpen)
        {
            return false;
        }

        _viewModel.ShowFullscreen();
        return true;
    }

    public bool RestorePlaybackWindow()
    {
        if (!_viewModel.IsPlaybackWindowOpen)
        {
            return false;
        }

        _viewModel.RestorePlaybackWindow();
        return true;
    }

    public bool SelectChannel(int index)
    {
        if (index < 0 || index >= _viewModel.Channels.Count)
        {
            _viewModel.SelectedChannel = null;
            return false;
        }

        _viewModel.SelectedChannel = _viewModel.Channels[index];
        return true;
    }

    public bool SelectChannel(GenesisChannelItem channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var source = channel.Source;
        var channels = _viewModel.Channels;
        for (var index = 0; index < channels.Count; index++)
        {
            if (string.Equals(
                    channels[index].Source,
                    source,
                    StringComparison.Ordinal))
            {
                return SelectChannel(index);
            }
        }

        _viewModel.SelectedChannel = null;
        return false;
    }

    public bool SortChannelColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= GenesisChannelListPresentation.Columns.Count)
        {
            return false;
        }

        _viewModel.SortChannels(
            GenesisChannelListPresentation.Columns[columnIndex].Column);
        return true;
    }

    public async ValueTask<bool> ActivateChannelAsync(
        int index,
        CancellationToken cancellationToken = default)
    {
        if (IsRecordingHistoryView || IsPublishingView || !_viewModel.CanPlay)
        {
            return false;
        }

        var channels = _viewModel.Channels;
        if (index < 0 || index >= channels.Count)
        {
            _ = SelectChannel(index);
            return false;
        }

        return await ActivateChannelAsync(channels[index], cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ActivateChannelAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (IsRecordingHistoryView || IsPublishingView || !_viewModel.CanPlay)
        {
            return false;
        }

        var source = channel.Source;
        if (!SelectChannel(channel))
        {
            return false;
        }

        await PlaySourceAsync(source, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> ActivateSelectedChannelAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsRecordingHistoryView || IsPublishingView || !_viewModel.CanPlay)
        {
            return false;
        }

        var selected = _viewModel.SelectedChannel;
        if (selected is null)
        {
            return false;
        }

        await PlaySourceAsync(selected.Source, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
