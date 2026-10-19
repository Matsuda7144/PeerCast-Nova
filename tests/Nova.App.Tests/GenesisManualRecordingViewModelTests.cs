using System.Collections.Concurrent;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisManualRecordingViewModelTests
{
    private const string FirstId = "531DC8DFC7FB42928AC2C0A626517A87";
    private const string SecondId = "102132435465768798A9BABBDCDDEF00";

    [Fact]
    public void RecordingEligibilityRequiresAValidSelectedChannel()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);

        Assert.False(viewModel.GetManualRecordingPresentation((GenesisChannelItem?)null).CanRecord);
        Assert.False(viewModel.GetManualRecordingPresentation(
            new GenesisChannelItem("bad", "https://example.test/live", ChannelId: FirstId))
            .CanRecord);
        Assert.False(viewModel.GetManualRecordingPresentation(
            new GenesisChannelItem(
                "missing id",
                Source(FirstId),
                ChannelId: null))
            .CanRecord);
        Assert.False(viewModel.GetManualRecordingPresentation(
            new GenesisChannelItem(
                "mismatch",
                Source(FirstId),
                ChannelId: SecondId))
            .CanRecord);
        Assert.True(viewModel.GetManualRecordingPresentation(Channel(FirstId)).CanRecord);
    }

    [Fact]
    public async Task CoreStartedIsProjectedAfterStartingAndRefreshKeepsStateByChannelId()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channelA = Channel(FirstId);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StartHandler = async (channel, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            controller.SetState(channel.ChannelId!, GenesisRecordingCoreStateKind.Manual);
            return Outcome(
                GenesisManualRecordingCommandKind.Started,
                GenesisRecordingCoreStateKind.Manual);
        };
        viewModel.ReplaceChannels([channelA, Channel(SecondId)]);

        var starting = viewModel.StartManualRecordingAsync(channelA).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            "録画開始中",
            viewModel.GetManualRecordingPresentation(channelA).StatusText);
        release.TrySetResult();
        await starting;

        Assert.Equal("録画中", viewModel.GetManualRecordingPresentation(channelA).StatusText);
        var refreshedA = Channel(FirstId, name: "refreshed A");
        var channelB = Channel(SecondId);
        viewModel.ReplaceChannels([refreshedA, channelB]);
        Assert.Equal(GenesisRecordingCoreStateKind.Manual,
            controller.GetSnapshot(refreshedA).State);
        Assert.True(viewModel.GetManualRecordingPresentation(refreshedA).CanStop);
        Assert.True(viewModel.GetManualRecordingPresentation(channelB).CanRecord);

        await viewModel.StopManualRecordingAsync(refreshedA);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle,
            controller.GetSnapshot(refreshedA).State);
        Assert.Equal(GenesisRecordingCoreStateKind.Idle,
            controller.GetSnapshot(channelB).State);
        Assert.Empty(viewModel.ActiveManualRecordings);
        Assert.True(viewModel.GetManualRecordingPresentation(channelA).CanRecord);
    }

    [Fact]
    public async Task DifferentChannelsCanRecordConcurrentlyAndDuplicateStartIsSuppressed()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var bothEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StartHandler = async (channel, cancellationToken) =>
        {
            if (Interlocked.Increment(ref controller.StartCount) == 2)
            {
                bothEntered.TrySetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            controller.SetState(channel.ChannelId!, GenesisRecordingCoreStateKind.Manual);
            return Outcome(
                GenesisManualRecordingCommandKind.Started,
                GenesisRecordingCoreStateKind.Manual);
        };

        var first = viewModel.StartManualRecordingAsync(Channel(FirstId)).AsTask();
        await WaitUntilAsync(() => Volatile.Read(ref controller.StartCount) == 1);
        var duplicate = viewModel.StartManualRecordingAsync(Channel(FirstId)).AsTask();
        var second = viewModel.StartManualRecordingAsync(Channel(SecondId)).AsTask();
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, controller.StartCount);
        Assert.Equal("録画開始中",
            viewModel.GetManualRecordingPresentation(Channel(FirstId)).StatusText);
        Assert.Equal("録画開始中",
            viewModel.GetManualRecordingPresentation(Channel(SecondId)).StatusText);

        release.TrySetResult();
        await Task.WhenAll(first, duplicate, second);
        Assert.Equal(2, controller.StartCount);
        Assert.Equal(2, viewModel.ActiveManualRecordings.Count);
        Assert.Equal(2, viewModel.ActiveManualRecordings
            .Select(static recording => recording.ChannelId)
            .Distinct()
            .Count());
    }

    [Fact]
    public async Task ActiveManualRecordingRemainsKeyedWhenSelectionAndCatalogRowsChange()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channelA = Channel(FirstId, name: "Channel A");
        var channelB = Channel(SecondId, name: "Channel B");
        viewModel.ReplaceChannels([channelA, channelB]);
        viewModel.SelectedChannel = channelA;

        await viewModel.StartManualRecordingAsync(channelA);

        var active = Assert.Single(viewModel.ActiveManualRecordings);
        Assert.Equal(Guid.ParseExact(FirstId, "N"), active.ChannelId.Value);
        Assert.Equal("Channel A", active.ChannelName);
        Assert.True(viewModel.GetManualRecordingPresentation(active).CanStop);

        viewModel.SelectedChannel = channelB;
        viewModel.ReplaceChannels([Channel(SecondId, name: "Refreshed B")]);
        await viewModel.RefreshActiveManualRecordingsAsync();

        active = Assert.Single(viewModel.ActiveManualRecordings);
        Assert.Equal(Guid.ParseExact(FirstId, "N"), active.ChannelId.Value);
        Assert.Equal("Channel A", active.ChannelName);

        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdateActiveManualRecordings(viewModel.ActiveManualRecordings);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(active.ChannelId.Value.ToString("N").ToUpperInvariant(),
            row.DisplayChannel.ChannelId);
        Assert.False(row.IsInteractive);

        await viewModel.PlayAsync();
        await viewModel.StopManualRecordingAsync(active.ChannelId, active.SessionId);

        Assert.Empty(viewModel.ActiveManualRecordings);
        Assert.Equal(active.ChannelId, controller.LastStopChannelId);
        Assert.Equal(active.SessionId, controller.LastStopSessionId);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
    }

    [Fact]
    public async Task UnavailableCoreStateIsNotPresentedAsConfirmedActiveRecording()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        await viewModel.StartManualRecordingAsync(channel);
        var active = Assert.Single(viewModel.ActiveManualRecordings);

        controller.StateAvailable = false;
        await viewModel.RefreshActiveManualRecordingsAsync();

        active = Assert.Single(viewModel.ActiveManualRecordings);
        Assert.Equal("状態を確認できません", active.StatusText);
        Assert.False(viewModel.GetManualRecordingPresentation(active).CanStop);
        Assert.True(viewModel.GetManualRecordingPresentation(active).IsFailure);

        controller.StateAvailable = true;
        await viewModel.RefreshActiveManualRecordingsAsync();

        active = Assert.Single(viewModel.ActiveManualRecordings);
        Assert.Equal("録画中", active.StatusText);
        Assert.True(viewModel.GetManualRecordingPresentation(active).CanStop);
    }

    [Fact]
    public async Task UnavailableRetainedManualRowCannotStop()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        viewModel.ReplaceChannels([channel]);
        await viewModel.StartManualRecordingAsync(channel);
        var active = Assert.Single(viewModel.ActiveRecordings);

        controller.StateAvailable = false;
        await viewModel.RefreshActiveRecordingsAsync();

        var retained = Assert.Single(viewModel.ActiveRecordings);
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdateActiveRecordings(
            viewModel.ActiveRecordings,
            viewModel.ActiveRecordingSnapshotAvailable);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Equal(active.SessionId, retained.SessionId);
        Assert.False(viewModel.ActiveRecordingSnapshotAvailable);
        Assert.Equal("状態を確認できません", retained.StatusText);
        Assert.Equal(
            "録画状態を確認できません",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                tabs.ActiveRecordingsAvailable,
                tabs.ActiveRecordingCount));
        Assert.False(viewModel.GetActiveRecordingPresentation(retained).CanStop);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            row));
        Assert.Equal(0, controller.StopCount);

        controller.StateAvailable = true;
        await viewModel.RefreshActiveRecordingsAsync();
        tabs.UpdateActiveRecordings(
            viewModel.ActiveRecordings,
            viewModel.ActiveRecordingSnapshotAvailable);

        Assert.Equal(
            "録画中のチャンネルを選択してください",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                tabs.ActiveRecordingsAvailable,
                tabs.ActiveRecordingCount));
        Assert.True(viewModel.GetActiveRecordingPresentation(
            Assert.Single(viewModel.ActiveRecordings)).CanStop);
    }

    [Fact]
    public async Task UnavailableRetainedAutomaticRowRemainsReadOnly()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var sessionId = new RecordingSessionId(Guid.NewGuid());
        controller.SetSession(
            FirstId,
            GenesisRecordingCoreStateKind.Automatic,
            sessionId,
            Guid.NewGuid());
        await viewModel.RefreshActiveRecordingsAsync();
        var active = Assert.Single(viewModel.ActiveRecordings);

        controller.StateAvailable = false;
        await viewModel.RefreshActiveRecordingsAsync();

        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdateActiveRecordings(
            viewModel.ActiveRecordings,
            viewModel.ActiveRecordingSnapshotAvailable);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));

        Assert.Equal(sessionId, Assert.Single(viewModel.ActiveRecordings).SessionId);
        Assert.Equal(RecordingSessionOrigin.AutomaticRule, active.Origin);
        Assert.False(viewModel.GetActiveRecordingPresentation(active).CanStop);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            row));
        Assert.Equal(0, controller.StopCount);
    }

    [Fact]
    public async Task UnavailableMixedRowsRemainVisibleAndCannotDispatchMutation()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var manualChannel = Channel(FirstId, "Manual channel");
        var automaticChannel = Channel(SecondId, "Automatic channel");
        viewModel.ReplaceChannels([manualChannel, automaticChannel]);
        await viewModel.StartManualRecordingAsync(manualChannel);
        controller.SetSession(
            SecondId,
            GenesisRecordingCoreStateKind.Automatic,
            new RecordingSessionId(Guid.NewGuid()),
            Guid.NewGuid());
        await viewModel.RefreshActiveRecordingsAsync();
        Assert.Equal(2, viewModel.ActiveRecordings.Count);

        controller.StateAvailable = false;
        await viewModel.RefreshActiveRecordingsAsync();

        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdateActiveRecordings(
            viewModel.ActiveRecordings,
            viewModel.ActiveRecordingSnapshotAvailable);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        var rows = GenesisGuiNextNativeTabProjection.Create(tabs);
        var manualRow = Assert.Single(
            rows,
            static row => row.ActiveRecording?.Origin == RecordingSessionOrigin.Manual);
        var automaticRow = Assert.Single(
            rows,
            static row => row.ActiveRecording?.Origin == RecordingSessionOrigin.AutomaticRule);

        Assert.Equal(2, rows.Count);
        Assert.Equal("録画状態を確認できません", GenesisGuiNextNativeTabProjection
            .GetRecordingEmptyStateText(tabs.ActiveRecordingsAvailable, tabs.ActiveRecordingCount));
        Assert.False(viewModel.GetActiveRecordingPresentation(
            manualRow.ActiveRecording!).CanStop);
        Assert.False(viewModel.GetActiveRecordingPresentation(
            automaticRow.ActiveRecording!).CanStop);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            manualRow));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            automaticRow));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(
            viewModel,
            GenesisGuiNextTab.Recording,
            manualRow));
        Assert.Equal(1, controller.StartCount);
        Assert.Equal(0, controller.StopCount);
    }

    [Fact]
    public async Task PlaybackAndRecordingCommandsRemainIndependent()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;

        await viewModel.PlayAsync();

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.True(viewModel.GetManualRecordingPresentation(channel).CanRecord);
        await viewModel.StartManualRecordingAsync(channel);
        Assert.Equal("録画中", viewModel.GetManualRecordingPresentation(channel).StatusText);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);

        await viewModel.StopManualRecordingAsync(channel);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);

        await viewModel.StartManualRecordingAsync(channel);
        await viewModel.StopAsync();
        Assert.Equal(GenesisRecordingCoreStateKind.Manual,
            controller.GetSnapshot(channel).State);
        await viewModel.StopManualRecordingAsync(channel);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.True(viewModel.GetManualRecordingPresentation(channel).CanRecord);
    }

    [Fact]
    public async Task FailedRecordingUsesBoundedMessageAndDoesNotLeakExceptionText()
    {
        var controller = new RecordingAwarePlaybackController
        {
            StartHandler = static (_, _) =>
                ValueTask.FromResult(Outcome(
                    GenesisManualRecordingCommandKind.Failed,
                    GenesisRecordingCoreStateKind.Idle)),
        };
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);

        await viewModel.StartManualRecordingAsync(channel);

        var presentation = viewModel.GetManualRecordingPresentation(channel);
        Assert.Equal("録画失敗", presentation.StatusText);
        Assert.Equal("録画を開始できませんでした。", presentation.ErrorMessage);
        Assert.DoesNotContain("Exception", presentation.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopFailureUsesBoundedMessageAndKeepsRecordingStateTruthful()
    {
        var controller = new RecordingAwarePlaybackController
        {
            StopHandler = static (_, _) =>
                ValueTask.FromResult(Outcome(
                    GenesisManualRecordingCommandKind.Faulted,
                    GenesisRecordingCoreStateKind.Manual)),
        };
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        controller.SetState(FirstId, GenesisRecordingCoreStateKind.Manual);

        await viewModel.StopManualRecordingAsync(channel);

        var presentation = viewModel.GetManualRecordingPresentation(channel);
        Assert.True(presentation.CanStop);
        Assert.Equal("録画中", presentation.StatusText);
        Assert.Equal("録画を停止できませんでした。", presentation.ErrorMessage);
        Assert.DoesNotContain("Exception", presentation.ErrorMessage, StringComparison.Ordinal);
        Assert.Single(viewModel.ActiveManualRecordings);
    }

    [Fact]
    public async Task NoDecodableVideoStartShowsNeutralNoFileNotice()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        await viewModel.StartManualRecordingAsync(channel);
        controller.StopHandler = (stoppingChannel, _) =>
        {
            Interlocked.Increment(ref controller.StopCount);
            controller.SetState(
                stoppingChannel.ChannelId!,
                GenesisRecordingCoreStateKind.Idle);
            return ValueTask.FromResult(Outcome(
                GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                GenesisRecordingCoreStateKind.Idle));
        };

        await viewModel.StopManualRecordingAsync(channel);

        var presentation = viewModel.GetManualRecordingPresentation(channel);
        Assert.Equal("録画ファイルなし（映像開始点未到達）", presentation.StatusText);
        Assert.Null(presentation.ErrorMessage);
        Assert.False(presentation.IsFailure);
        Assert.True(presentation.CanRecord);
        Assert.Empty(viewModel.ActiveManualRecordings);
        Assert.Equal(1, controller.StopCount);
    }

    [Fact]
    public async Task DoubleStopIsSuppressedPerChannel()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        controller.SetState(FirstId, GenesisRecordingCoreStateKind.Manual);
        await viewModel.RefreshActiveManualRecordingsAsync();
        var active = Assert.Single(viewModel.ActiveManualRecordings);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StopHandler = async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref controller.StopCount);
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            controller.SetState(FirstId, GenesisRecordingCoreStateKind.Idle);
            return Outcome(
                GenesisManualRecordingCommandKind.Stopped,
                GenesisRecordingCoreStateKind.Idle);
        };

        var first = viewModel.StopManualRecordingAsync(
            active.ChannelId,
            active.SessionId).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = viewModel.StopManualRecordingAsync(channel).AsTask();
        Assert.Equal("録画停止中", viewModel.GetManualRecordingPresentation(channel).StatusText);
        Assert.Equal(1, controller.StopCount);
        release.TrySetResult();
        await Task.WhenAll(first, duplicate);
        Assert.Equal(1, controller.StopCount);
        Assert.True(viewModel.GetManualRecordingPresentation(channel).CanRecord);
    }

    [Fact]
    public void RecordingPresentationIsDisabledForPlayingTabAndAutomaticSessionCannotBeStopped()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        controller.SetState(FirstId, GenesisRecordingCoreStateKind.Automatic);
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        var channels = GenesisGuiNextRecordingCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Channels,
            row);
        var playing = GenesisGuiNextRecordingCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Playing,
            row);

        Assert.False(channels.IsRecordEnabled);
        Assert.False(channels.IsStopRecordingEnabled);
        Assert.False(playing.IsRecordEnabled);
        Assert.False(playing.IsStopRecordingEnabled);
        Assert.Equal("自動録画中", viewModel.GetManualRecordingPresentation(channel).StatusText);
    }

    [Fact]
    public async Task DetailPaneDispatchUsesIndependentRecordAndRecordingStopCommands()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId);
        var row = new GenesisGuiNextNativeTabRow(channel, channel);

        var started = await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(
            viewModel,
            GenesisGuiNextTab.Channels,
            row);
        Assert.True(started);
        Assert.Equal(1, controller.StartCount);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);

        var stopped = await GenesisGuiNextRecordingCommandDispatch.StopSelectedAsync(
            viewModel,
            GenesisGuiNextTab.Channels,
            row);
        Assert.True(stopped);
        Assert.True(viewModel.GetManualRecordingPresentation(channel).CanRecord);
        Assert.False(viewModel.GetManualRecordingPresentation(channel).CanStop);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(
            viewModel,
            GenesisGuiNextTab.Playing,
            row));

        var nonInteractive = new GenesisGuiNextNativeTabRow(channel, InteractiveChannel: null);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(
            viewModel,
            GenesisGuiNextTab.Channels,
            nonInteractive));
    }

    [Fact]
    public async Task ManualAndAutomaticSessionsProjectTogetherAndOnlyManualCanStop()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var manualChannel = Channel(FirstId, "Manual channel");
        var automaticChannel = Channel(SecondId, "Automatic channel");
        viewModel.ReplaceChannels([manualChannel, automaticChannel]);
        await viewModel.StartManualRecordingAsync(manualChannel);

        var automaticSessionId = new RecordingSessionId(Guid.NewGuid());
        var automaticRuleId = Guid.NewGuid();
        controller.SetSession(
            SecondId,
            GenesisRecordingCoreStateKind.Automatic,
            automaticSessionId,
            automaticRuleId);
        await viewModel.RefreshActiveRecordingsAsync();

        Assert.Equal(2, viewModel.ActiveRecordings.Count);
        var manual = Assert.Single(
            viewModel.ActiveRecordings,
            static row => row.Origin == RecordingSessionOrigin.Manual);
        var automatic = Assert.Single(
            viewModel.ActiveRecordings,
            static row => row.Origin == RecordingSessionOrigin.AutomaticRule);
        Assert.Equal("手動録画", manual.OriginLabel);
        Assert.Equal("自動録画", automatic.OriginLabel);
        Assert.Equal(automaticSessionId, automatic.SessionId);
        Assert.Equal(automaticRuleId, automatic.AutomaticRuleId);
        Assert.Equal("Automatic channel", automatic.ChannelName);
        Assert.True(viewModel.GetActiveRecordingPresentation(manual).CanStop);
        Assert.False(viewModel.GetActiveRecordingPresentation(automatic).CanStop);

        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.UpdateActiveRecordings(
            viewModel.ActiveRecordings,
            viewModel.ActiveRecordingSnapshotAvailable);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        var rows = GenesisGuiNextNativeTabProjection.Create(tabs);
        var automaticNativeRow = Assert.Single(
            rows,
            static row => row.ActiveRecording?.Origin == RecordingSessionOrigin.AutomaticRule);
        var manualNativeRow = Assert.Single(
            rows,
            static row => row.ActiveRecording?.Origin == RecordingSessionOrigin.Manual);

        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            automaticNativeRow));
        Assert.Equal(0, controller.StopCount);
        Assert.True(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            manualNativeRow));
        Assert.Equal(1, controller.StopCount);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            manualNativeRow));
        Assert.Equal(1, controller.StopCount);
        Assert.Single(viewModel.ActiveRecordings);
        Assert.Equal(automaticSessionId, Assert.Single(viewModel.ActiveRecordings).SessionId);
        Assert.Equal(
            "録画中のチャンネルを選択してください",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                viewModel.ActiveRecordingSnapshotAvailable,
                viewModel.ActiveRecordings.Count));

        controller.SetState(SecondId, GenesisRecordingCoreStateKind.Idle);
        await viewModel.RefreshActiveRecordingsAsync();
        Assert.Empty(viewModel.ActiveRecordings);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            viewModel,
            automaticNativeRow));
        Assert.Equal(
            "現在録画中のチャンネルはありません",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                viewModel.ActiveRecordingSnapshotAvailable,
                viewModel.ActiveRecordings.Count));
    }

    [Fact]
    public async Task AutomaticRecordingSurvivesCatalogRemovalWithChannelIdFallback()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(SecondId, "Catalog name");
        viewModel.ReplaceChannels([channel]);
        var sessionId = new RecordingSessionId(Guid.NewGuid());
        var ruleId = Guid.NewGuid();
        controller.SetSession(
            SecondId,
            GenesisRecordingCoreStateKind.Automatic,
            sessionId,
            ruleId);

        await viewModel.RefreshActiveRecordingsAsync();
        var active = Assert.Single(viewModel.ActiveRecordings);
        Assert.Equal("Catalog name", active.ChannelName);

        viewModel.ReplaceChannels([]);
        await viewModel.RefreshActiveRecordingsAsync();

        active = Assert.Single(viewModel.ActiveRecordings);
        Assert.Equal(sessionId, active.SessionId);
        Assert.Equal($"Channel {SecondId[..8]}", active.ChannelName);
        Assert.Equal(ruleId, active.AutomaticRuleId);
    }

    [Fact]
    public async Task FailedCoreSnapshotDoesNotProjectFalseEmptyState()
    {
        var controller = new RecordingAwarePlaybackController
        {
            StateAvailable = false,
        };
        var viewModel = new GenesisMainViewModel(controller);

        await viewModel.RefreshActiveRecordingsAsync();

        Assert.False(viewModel.ActiveRecordingSnapshotAvailable);
        Assert.Empty(viewModel.ActiveRecordings);
        Assert.Equal(
            "録画状態を確認できません",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                viewModel.ActiveRecordingSnapshotAvailable,
                viewModel.ActiveRecordings.Count));

        controller.StateAvailable = true;
        await viewModel.RefreshActiveRecordingsAsync();
        Assert.True(viewModel.ActiveRecordingSnapshotAvailable);
        Assert.Equal(
            "現在録画中のチャンネルはありません",
            GenesisGuiNextNativeTabProjection.GetRecordingEmptyStateText(
                viewModel.ActiveRecordingSnapshotAvailable,
                viewModel.ActiveRecordings.Count));
    }

    [Fact]
    public async Task ReplacementGenerationGetsNewIdentityAndDoesNotReuseOldManualName()
    {
        var controller = new RecordingAwarePlaybackController();
        var viewModel = new GenesisMainViewModel(controller);
        var channel = Channel(FirstId, "Old generation name");
        viewModel.ReplaceChannels([channel]);
        await viewModel.StartManualRecordingAsync(channel);
        var first = Assert.Single(viewModel.ActiveRecordings);

        viewModel.ReplaceChannels([]);
        var replacementSessionId = new RecordingSessionId(Guid.NewGuid());
        controller.SetSession(
            FirstId,
            GenesisRecordingCoreStateKind.Manual,
            replacementSessionId);
        await viewModel.RefreshActiveRecordingsAsync();

        var replacement = Assert.Single(viewModel.ActiveRecordings);
        Assert.NotEqual(first.SessionId, replacement.SessionId);
        Assert.Equal(replacementSessionId, replacement.SessionId);
        Assert.Equal($"Channel {FirstId[..8]}", replacement.ChannelName);
        Assert.True(viewModel.GetActiveRecordingPresentation(replacement).CanStop);
        await viewModel.StopManualRecordingAsync(first.ChannelId, first.SessionId);
        Assert.Equal(0, controller.StopCount);
    }

    private static GenesisChannelItem Channel(string id, string? name = null) =>
        new(name ?? "station", Source(id), ChannelId: id);

    private static string Source(string id) =>
        $"pcp://tracker.example:7144/channel/{id}";

    private static GenesisManualRecordingCommandOutcome Outcome(
        GenesisManualRecordingCommandKind kind,
        GenesisRecordingCoreStateKind state) =>
        new(kind, new(true, state));

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingAwarePlaybackController :
        IPlaybackSessionController,
        IGenesisManualRecordingSessionController
    {
        private readonly ConcurrentDictionary<string, GenesisRecordingCoreStateKind> _states =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RecordingSessionId> _sessionIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _channelNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Guid> _automaticRuleIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RecordingSessionId>
            _channelNameSessionIds = new(StringComparer.OrdinalIgnoreCase);
        private PlaybackSessionSnapshot _snapshot = PlaybackSessionSnapshot.Stopped;

        public int StartCount;

        public Func<GenesisChannelItem, CancellationToken,
            ValueTask<GenesisManualRecordingCommandOutcome>>? StartHandler
        { get; set; }

        public Func<GenesisChannelItem, CancellationToken,
            ValueTask<GenesisManualRecordingCommandOutcome>>? StopHandler
        { get; set; }

        public int StopCount;

        public bool StateAvailable { get; set; } = true;

        public ChannelId? LastStopChannelId { get; private set; }

        public RecordingSessionId? LastStopSessionId { get; private set; }

        public PlaybackSessionSnapshot Snapshot => _snapshot;

        public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel)
        {
            if (!StateAvailable)
            {
                return GenesisRecordingCoreSnapshot.Unavailable;
            }

            var valid = GenesisRecordingCandidateAdapter.TryCreate(channel, out _);
            if (!GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var id) || id is null)
            {
                return GenesisRecordingCoreSnapshot.Unavailable;
            }

            var state = _states.GetValueOrDefault(
                id.ToString(),
                GenesisRecordingCoreStateKind.Idle);
            return new(
                valid,
                state,
                _sessionIds.GetValueOrDefault(id.ToString()));
        }

        public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId)
        {
            if (!StateAvailable)
            {
                return GenesisRecordingCoreSnapshot.Unavailable;
            }

            var state = _states.GetValueOrDefault(
                channelId.ToString(),
                GenesisRecordingCoreStateKind.Idle);
            return new(
                false,
                state,
                _sessionIds.GetValueOrDefault(channelId.ToString()));
        }

        public bool IsKnownManualSession(ChannelId channelId, RecordingSessionId sessionId) =>
            GetSnapshot(channelId).State == GenesisRecordingCoreStateKind.Manual &&
            GetSnapshot(channelId).SessionId == sessionId;

        public ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StateAvailable)
            {
                return ValueTask.FromResult(GenesisActiveRecordingSessionSnapshot.Unavailable);
            }

            var sessions = _states
                .Where(static pair => pair.Value is GenesisRecordingCoreStateKind.Manual or
                    GenesisRecordingCoreStateKind.Automatic)
                .Select(pair => new GenesisActiveRecordingSession(
                    new ChannelId(Guid.ParseExact(pair.Key, "N")),
                    _sessionIds.GetValueOrDefault(pair.Key) ??
                        new RecordingSessionId(Guid.ParseExact(pair.Key, "N")),
                    pair.Value == GenesisRecordingCoreStateKind.Manual
                        ? RecordingSessionOrigin.Manual
                        : RecordingSessionOrigin.AutomaticRule,
                    pair.Value == GenesisRecordingCoreStateKind.Automatic
                        ? _automaticRuleIds.GetValueOrDefault(pair.Key)
                        : null,
                    pair.Value == GenesisRecordingCoreStateKind.Manual
                        ? _channelNameSessionIds.GetValueOrDefault(pair.Key) ==
                            _sessionIds.GetValueOrDefault(pair.Key)
                                ? _channelNames.GetValueOrDefault(pair.Key)
                                : null
                        : null))
                .OrderBy(static session => session.ChannelId.Value)
                .ThenBy(static session => session.SessionId.Value)
                .ToArray();
            return ValueTask.FromResult(new GenesisActiveRecordingSessionSnapshot(
                true,
                Array.AsReadOnly(sessions)));
        }

        public ValueTask<GenesisManualRecordingSessionSnapshot> GetActiveManualRecordingsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessions = _states
                .Where(static pair => pair.Value == GenesisRecordingCoreStateKind.Manual)
                .Select(pair => new GenesisManualRecordingSessionIdentity(
                    new ChannelId(Guid.ParseExact(pair.Key, "N")),
                    _sessionIds.GetValueOrDefault(pair.Key) ??
                        new RecordingSessionId(Guid.ParseExact(pair.Key, "N")),
                    _channelNames.GetValueOrDefault(pair.Key) ?? "station"))
                .ToArray();
            return ValueTask.FromResult(new GenesisManualRecordingSessionSnapshot(
                StateAvailable,
                Array.AsReadOnly(sessions)));
        }

        public ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken = default)
        {
            _channelNames[channel.ChannelId!] = channel.Name;
            if (StartHandler is { } handler)
            {
                return TrackManualStartNameAsync(handler(channel, cancellationToken), channel);
            }

            Interlocked.Increment(ref StartCount);
            SetState(channel.ChannelId!, GenesisRecordingCoreStateKind.Manual);
            _channelNameSessionIds[channel.ChannelId!] = _sessionIds[channel.ChannelId!];
            return ValueTask.FromResult(Outcome(
                GenesisManualRecordingCommandKind.Started,
                GenesisRecordingCoreStateKind.Manual));
        }

        private async ValueTask<GenesisManualRecordingCommandOutcome> TrackManualStartNameAsync(
            ValueTask<GenesisManualRecordingCommandOutcome> start,
            GenesisChannelItem channel)
        {
            var outcome = await start.ConfigureAwait(false);
            if ((outcome.Kind is GenesisManualRecordingCommandKind.Started or
                GenesisManualRecordingCommandKind.AlreadyRunning) &&
                GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) &&
                channelId is not null &&
                GetSnapshot(channelId).SessionId is { } sessionId)
            {
                _channelNameSessionIds[channel.ChannelId!] = sessionId;
            }

            return outcome;
        }

        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken = default)
        {
            if (GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) &&
                channelId is not null &&
                GetSnapshot(channelId).SessionId is { } sessionId)
            {
                return StopAsync(channelId, sessionId, cancellationToken);
            }

            return ValueTask.FromResult(Outcome(
                GenesisManualRecordingCommandKind.NoActiveSession,
                GenesisRecordingCoreStateKind.Idle));
        }

        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
            ChannelId channelId,
            RecordingSessionId expectedSessionId,
            CancellationToken cancellationToken = default)
        {
            if (GetSnapshot(channelId).SessionId != expectedSessionId)
            {
                return ValueTask.FromResult(Outcome(
                    GenesisManualRecordingCommandKind.Rejected,
                    GetSnapshot(channelId).State));
            }

            LastStopChannelId = channelId;
            LastStopSessionId = expectedSessionId;
            var channel = Channel(channelId.ToString());
            if (StopHandler is { } handler)
            {
                return handler(channel, cancellationToken);
            }

            Interlocked.Increment(ref StopCount);
            _ = cancellationToken;
            SetState(channelId.ToString(), GenesisRecordingCoreStateKind.Idle);
            return ValueTask.FromResult(Outcome(
                GenesisManualRecordingCommandKind.Stopped,
                GenesisRecordingCoreStateKind.Idle));
        }

        public void SetState(string id, GenesisRecordingCoreStateKind state)
        {
            SetSession(
                id,
                state,
                state is GenesisRecordingCoreStateKind.Manual or
                    GenesisRecordingCoreStateKind.Automatic
                    ? new RecordingSessionId(Guid.ParseExact(id, "N"))
                    : null,
                state == GenesisRecordingCoreStateKind.Automatic ? Guid.NewGuid() : null);
        }

        public void SetSession(
            string id,
            GenesisRecordingCoreStateKind state,
            RecordingSessionId? sessionId,
            Guid? automaticRuleId = null)
        {
            _states[id] = state;
            if (state is GenesisRecordingCoreStateKind.Manual or
                GenesisRecordingCoreStateKind.Automatic)
            {
                if (sessionId is not null)
                {
                    _sessionIds[id] = sessionId;
                }
                else
                {
                    _ = _sessionIds.TryRemove(id, out _);
                }

                if (automaticRuleId is { } ruleId)
                {
                    _automaticRuleIds[id] = ruleId;
                }
                else
                {
                    _ = _automaticRuleIds.TryRemove(id, out _);
                }
            }
            else
            {
                _ = _sessionIds.TryRemove(id, out _);
                _ = _automaticRuleIds.TryRemove(id, out _);
            }
        }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _snapshot = new(PlaybackSessionState.Playing, source, null);
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                _snapshot));
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _snapshot = PlaybackSessionSnapshot.Stopped;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                _snapshot));
        }
    }
}
