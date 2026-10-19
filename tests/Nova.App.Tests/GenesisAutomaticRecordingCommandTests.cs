using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    [Fact]
    public async Task AutomaticRowDispatchPendingSuppressedAndAllowAreOriginSafe()
    {
        var sink = new UserStopSink(); var r = RecordRule();
        await using var f = new Fixture(new([r]), sink: sink);
        await using var manual = new GenesisManualRecordingSessionController(f.OwnerProvider, f.Runtime);
        var bridge = new RecordingTestBridge(manual, f.Controller);
        var vm = new GenesisMainViewModel(bridge);
        Assert.Equal(0, sink.Starts); Assert.Equal(0, f.OwnerProvider.AttachCount);
        await f.Controller.ObserveAsync([Channel()]); vm.ReplaceChannels([Channel()]); await vm.RefreshActiveRecordingsAsync();
        var active = Assert.Single(vm.ActiveRecordings); Assert.True(vm.GetActiveRecordingPresentation(active).CanStop);
        var native = new GenesisGuiNextNativeTabRow(Channel(), null, active);
        Assert.True(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(vm, native));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(vm, native));
        Assert.Equal(1, sink.AdmissionCalls); Assert.Equal(0, sink.LegacyStops);
        Assert.True(vm.GetActiveRecordingPresentation(active).IsTransitioning);
        Assert.Contains("停止処理中", Assert.Single(vm.ActiveRecordings).StatusText);
        var pending = Assert.Single(vm.SuppressionRows); Assert.False(vm.CanAllowAutomatic(pending));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(vm, new(Channel(), null, Suppression: pending)));
        var committed = WaitForCommitted(f.Controller); sink.Finish(RecordingSinkStopResultKind.Faulted); await committed;
        Assert.Empty(vm.ActiveRecordings);
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection()); tabs.SelectedTab = GenesisGuiNextTab.Recording;
        tabs.UpdateSuppression(vm.SuppressionRows);
        native = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Null(native.ActiveRecording); Assert.True(vm.CanAllowAutomatic(native.Suppression!));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(vm, native));
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(vm, GenesisGuiNextTab.Channels, native).IsPlayEnabled);
        Assert.Equal(GenesisGuiNextFavoriteToggleResult.Unavailable, GenesisGuiNextFavoriteToggle.Apply(tabs, native));
        Assert.Contains("保存に失敗", native.Suppression!.StatusText);
        Assert.True(await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(vm, native));
        Assert.Equal("自動録画の抑止を解除しました。次回のチャンネル更新から通常の判定に戻ります。", vm.AutomaticRecordingCommandMessage);
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(vm, native));
        Assert.Empty(vm.SuppressionRows); Assert.Equal(1, sink.Starts); Assert.Equal(1, f.OwnerProvider.AttachCount);
        Assert.Equal(PlaybackSessionState.Stopped, bridge.Snapshot.State);
        await f.Controller.DisposeAsync(); Assert.False(vm.CanAllowAutomatic(native.Suppression));
        Assert.False(vm.GetActiveRecordingPresentation(active).CanStop);
    }

    [Fact]
    public async Task OnePassKeepsSuppressionSnapshotForAllCandidatesWhileAllowWaits()
    {
        var sink = new UserStopSink(); var r = RecordRule(); await using var f = new Fixture(new([r]), sink: sink);
        await f.Controller.ObserveAsync([Channel()]); var s = f.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(f.Controller); await f.Controller.StopAutomaticAsync(ChannelId(), s.SessionId);
        sink.Finish(RecordingSinkStopResultKind.Stopped); await committed;
        await using var manual = new GenesisManualRecordingSessionController(f.OwnerProvider, f.Runtime);
        var vm = new GenesisMainViewModel(new RecordingTestBridge(manual, f.Controller));
        var suppressed = Assert.Single(vm.SuppressionRows);
        var id = Guid.NewGuid().ToString("N"); var other = Channel() with { ChannelId = id, Source = $"pcp://tracker.example:7144/channel/{id}" };
        sink.BlockStart = true;
        var pass = f.Controller.ObserveAsync([other, Channel()]).AsTask(); await sink.StartEntered.Task;
        var allow = vm.AllowAutomaticRecordingAsync(suppressed, CancellationToken.None).AsTask(); Assert.False(allow.IsCompleted);
        Assert.False(vm.CanAllowAutomatic(suppressed));
        await vm.AllowAutomaticRecordingAsync(suppressed, CancellationToken.None);
        sink.ReleaseStart.SetResult();
        Assert.Equal(1, (await pass).StartedCount); await allow;
        Assert.Equal(2, sink.Starts); Assert.Empty(f.Controller.SuppressionRows);
        Assert.Equal(1, (await f.Controller.ObserveAsync([other, Channel()])).StartedCount);
        Assert.Equal(3, sink.Starts);
    }

    private sealed class RecordingTestBridge(IGenesisManualRecordingSessionController manual, IGenesisAutomaticRecordingUserCommands automatic) :
        IPlaybackSessionController, IGenesisManualRecordingSessionController, IGenesisAutomaticRecordingUserCommands
    {
        private readonly FakePlaybackSessionController _playback = new();
        public PlaybackSessionSnapshot Snapshot => _playback.Snapshot;
        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(string source, CancellationToken token = default) => _playback.PlayAsync(source, token);
        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(CancellationToken token = default) => _playback.StopAsync(token);
        public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel) => manual.GetSnapshot(channel);
        public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channel) => manual.GetSnapshot(channel);
        public bool IsKnownManualSession(ChannelId c, RecordingSessionId s) => manual.IsKnownManualSession(c, s);
        public ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(CancellationToken token = default) => manual.GetActiveRecordingSessionsAsync(token);
        public ValueTask<GenesisManualRecordingSessionSnapshot> GetActiveManualRecordingsAsync(CancellationToken token = default) => manual.GetActiveManualRecordingsAsync(token);
        public ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(GenesisChannelItem c, CancellationToken token = default) => manual.StartAsync(c, token);
        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(GenesisChannelItem c, CancellationToken token = default) => manual.StopAsync(c, token);
        public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(ChannelId c, RecordingSessionId s, CancellationToken token = default) => manual.StopAsync(c, s, token);
        public event EventHandler? SuppressionChanged { add => automatic.SuppressionChanged += value; remove => automatic.SuppressionChanged -= value; }
        public IReadOnlyList<GenesisAutomaticRecordingSuppressionRow> SuppressionRows => automatic.SuppressionRows;
        public bool CanStopAutomatic(ChannelId c, RecordingSessionId s) => automatic.CanStopAutomatic(c, s);
        public bool IsStoppedAutomaticGeneration(ChannelId c, RecordingSessionId s) => automatic.IsStoppedAutomaticGeneration(c, s);
        public bool CanAllowAutomatic(ChannelId c, Guid r) => automatic.CanAllowAutomatic(c, r);
        public ValueTask<string> StopAutomaticAsync(ChannelId c, RecordingSessionId s, CancellationToken token = default) => automatic.StopAutomaticAsync(c, s, token);
        public ValueTask<string> AllowAutomaticAsync(ChannelId c, Guid r, CancellationToken token = default) => automatic.AllowAutomaticAsync(c, r, token);
    }
}
