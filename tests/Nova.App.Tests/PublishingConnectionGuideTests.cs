using System.Collections.Concurrent;
using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Windows;
using Owner = PeerCast.Nova.App.Tests.NovaObsPublishingFinishTests.Owner;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingConnectionGuideTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private const string Key = "non-default-秘密-key";
    private const string Server = "rtmp://127.0.0.1:18645/custom-app";

    private sealed class Source : INovaObsPublishingStatusSource
    {
        internal ObsPublishStatusSnapshot Current = Raw();
        internal Func<ObsPublishStatusSnapshot>? Read;
        public ObsPublishStatusSnapshot Snapshot => Read is { } read ? read() : Current;
    }

    private static ObsPublishStatusSnapshot Raw() => new(new ChannelId(Guid.NewGuid()),
        new ChannelSessionId(Guid.NewGuid()), 1, ChannelSessionState.Created, false, false, false,
        Server, Key, null, null, null, null);

    private static NovaObsPublishingReadOnlySnapshot Status(ObsPublishStatusSnapshot raw, bool notStarted = true) =>
        new(notStarted ? NovaObsPublishingAvailability.NotStarted : NovaObsPublishingAvailability.Observed,
            raw.ChannelId.Value, raw.SessionId.Value, "safe state", string.Empty, NovaObsPublishingTone.Normal, null, null, null)
        {
            Generation = 1,
            IsSourceNotStarted = notStarted,
            LifecycleState = notStarted ? NovaObsPublishingLifecycleState.NotStarted : NovaObsPublishingLifecycleState.Running,
            LifecycleFailure = NovaObsPublishingLifecycleFailure.None,
        };

    [Fact]
    public void FixedPortIsPlannedAndSensitiveDtoAndNormalStatusDoNotDisclose()
    {
        var source = new Source();
        var reader = new NovaObsPublishingConnectionReader(source);
        var normal = Status(source.Current);
        var info = Assert.IsType<NovaObsPublishingConnectionInfo>(reader.Read(1, normal));
        var view = NovaObsPublishingConnectionPresentation.From(normal, info);
        Assert.Equal(Server, view.ServerText);
        Assert.True(view.CanCopyServer); Assert.True(view.CanUseKey);
        Assert.Contains("未待受", view.StateText);
        Assert.Equal(Key, info.Key);
        Assert.DoesNotContain(Key, info.ToString());
        Assert.DoesNotContain(Key, JsonSerializer.Serialize(info));
        Assert.DoesNotContain(Server, view.ToString());
        Assert.DoesNotContain(Key, JsonSerializer.Serialize(normal));
        Assert.DoesNotContain(Server, JsonSerializer.Serialize(normal));
        var ordinary = new NovaObsPublishingReadOnlyStatus(source).Read();
        Assert.DoesNotContain(Key, JsonSerializer.Serialize(ordinary));
        Assert.DoesNotContain(Server, ordinary.ToString());
    }

    [Fact]
    public void PortZeroIsUnknownUntilActualListenerSnapshot()
    {
        var source = new Source { Current = Raw() with { RtmpServer = null } };
        var reader = new NovaObsPublishingConnectionReader(source);
        var status = Status(source.Current);
        var info = Assert.IsType<NovaObsPublishingConnectionInfo>(reader.Read(1, status));
        var view = NovaObsPublishingConnectionPresentation.From(status, info);
        Assert.Equal("待受開始後に確定", view.ServerText); Assert.False(view.CanCopyServer);
        Assert.True(view.CanUseKey);
        source.Current = source.Current with { Revision = 2, State = ChannelSessionState.Negotiating };
        view = NovaObsPublishingConnectionPresentation.From(Status(source.Current, false), reader.Read(1, Status(source.Current, false)));
        Assert.Equal("待受準備中", view.StateText); Assert.False(view.CanCopyServer); Assert.False(view.CanUseKey);
        source.Current = source.Current with { Revision = 3, ListenerReady = true, RtmpServer = Server };
        view = NovaObsPublishingConnectionPresentation.From(Status(source.Current, false), reader.Read(1, Status(source.Current, false)));
        Assert.Equal(Server, view.ServerText); Assert.True(view.CanCopyServer);
        Assert.Contains("待受準備完了", view.StateText);
    }

    [Fact]
    public async Task RealManagedLoopbackHostUsesItsActualBoundPortWithoutGuiOrObs()
    {
        await using var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0, Application = "different", StreamName = Key } });
        var owner = NovaObsPublishingRuntime.ForOwner(host);
        await using var composition = NovaGuiNextPublishingComposition.Create(new SingleFactory(owner));
        var first = Assert.IsType<NovaObsPublishingConnectionInfo>(composition.ReadConnectionInfo(composition.Read()));
        Assert.Null(first.Server); Assert.False(first.CanCopyServer);
        await composition.RequestStartAsync().WaitAsync(Limit);
        var current = Assert.IsType<NovaObsPublishingConnectionInfo>(composition.ReadConnectionInfo(composition.Read()));
        Assert.Equal($"rtmp://127.0.0.1:{host.IngestEndPoint.Port}/different", current.Server);
        Assert.Equal(Key, current.Key); Assert.True(current.CanCopyServer);
        // Dispose is owned by composition; the host's extra await-using is idempotent.
    }

    private sealed class SingleFactory(INovaObsPublishingRuntime owner) : INovaObsPublishingRuntimeFactory
    {
        internal int Creates;
        public INovaObsPublishingRuntime Create() { Creates++; return owner; }
    }

    [Theory]
    [InlineData(false, false, false, "待受準備中")]
    [InlineData(true, false, false, "待受準備完了（このPCのみ）")]
    [InlineData(true, true, false, "入力接続あり・メディア待ち")]
    [InlineData(true, true, true, "メディア初期化受信済み")]
    public void TcpAndHeadDoNotClaimProtocolOrPlaybackSuccess(bool listening, bool connected, bool head, string expected)
    {
        var source = new Source
        {
            Current = Raw() with
            {
                ListenerReady = listening,
                PublisherConnected = connected,
                HeadAvailable = head,
                State = head ? ChannelSessionState.Receiving : ChannelSessionState.Negotiating
            }
        };
        var status = Status(source.Current, false);
        var view = NovaObsPublishingConnectionPresentation.From(status, new NovaObsPublishingConnectionReader(source).Read(1, status));
        Assert.Equal(expected, view.StateText);
        Assert.DoesNotContain("再生成功", view.StateText); Assert.DoesNotContain("配信成功", view.StateText);
    }

    [Theory]
    [InlineData((int)NovaObsPublishingCompletionKind.ObsNormalEos, true, "正常終了（OBS停止）")]
    [InlineData((int)NovaObsPublishingCompletionKind.LocalCompleted, true, "終了（Novaローカル）")]
    [InlineData((int)NovaObsPublishingCompletionKind.Cancelled, true, "中断済み")]
    [InlineData((int)NovaObsPublishingCompletionKind.ObsNormalEos, false, "異常終了・結果不明")]
    [InlineData((int)NovaObsPublishingCompletionKind.Disconnected, true, "異常終了・結果不明")]
    [InlineData((int)NovaObsPublishingCompletionKind.IncompleteEos, true, "異常終了・結果不明")]
    [InlineData((int)NovaObsPublishingCompletionKind.TimedOut, true, "異常終了・結果不明")]
    [InlineData((int)NovaObsPublishingCompletionKind.DeliveryFailed, true, "異常終了・結果不明")]
    [InlineData((int)NovaObsPublishingCompletionKind.CleanupIncomplete, false, "異常終了・結果不明")]
    public void CompletionRequiresProofAndNeverLeavesCopyEnabled(int kind, bool cleaned, string expected)
    {
        var status = Status(Raw(), false) with
        {
            CompletionKind = (NovaObsPublishingCompletionKind)kind,
            CleanupCompleted = cleaned,
            Availability = NovaObsPublishingAvailability.Stopped,
            LifecycleState = NovaObsPublishingLifecycleState.Stopped
        };
        var view = NovaObsPublishingConnectionPresentation.From(status, null);
        Assert.Equal(expected, view.StateText);
        Assert.False(view.CanCopyServer); Assert.False(view.CanUseKey); Assert.Null(view.Identity);
    }

    [Fact]
    public void RevisionsIdentityAndTerminalCannotBeRepairedByLateReceiving()
    {
        var source = new Source(); var reader = new NovaObsPublishingConnectionReader(source);
        var status = Status(source.Current);
        var accepted = Assert.IsType<NovaObsPublishingConnectionInfo>(reader.Read(1, status));
        Assert.True(reader.IsCurrent(accepted.Identity));
        source.Current = source.Current with { Revision = 0 };
        Assert.Null(reader.Read(1, status)); Assert.True(reader.IsCurrent(accepted.Identity));
        source.Current = source.Current with { Revision = 2 };
        Assert.NotNull(reader.Read(1, status));
        source.Current = source.Current with { PublishName = "changed-without-revision" };
        Assert.Null(reader.Read(1, status));
        source.Current = source.Current with { Revision = 3, State = ChannelSessionState.Stopped };
        Assert.Null(reader.Read(1, status));
        source.Current = source.Current with { Revision = 4, State = ChannelSessionState.Receiving, HeadAvailable = true };
        Assert.Null(reader.Read(1, status));
        var replacement = new NovaObsPublishingConnectionReader(source);
        Assert.Null(replacement.Read(2, status));
        source.Current = source.Current with { SessionId = new ChannelSessionId(Guid.NewGuid()) };
        Assert.Null(replacement.Read(1, status));
    }

    [Fact]
    public void UnavailableReadDoesNotPermanentlyPoisonAnOtherwiseUnstartedOwner()
    {
        var source = new Source(); var reader = new NovaObsPublishingConnectionReader(source);
        var status = Status(source.Current);
        Assert.Null(reader.Read(1, status with { Availability = NovaObsPublishingAvailability.Unavailable }));
        Assert.NotNull(reader.Read(1, status));
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal readonly Owner Owner = new();
        internal readonly SingleFactory Factory;
        internal readonly NovaGuiNextPublishingComposition Composition;
        internal readonly GenesisGuiNextTabsViewModel Tabs = new(new GenesisFavoriteChannelCollection())
        { SelectedTab = GenesisGuiNextTab.Publishing };
        internal readonly ConcurrentQueue<Action> Queue = new();
        internal Harness()
        {
            Owner.Current = Owner.Current with { RtmpServer = Server, PublishName = Key };
            Factory = new(Owner);
            Composition = NovaGuiNextPublishingComposition.Create(Factory);
            Tabs.SetPublishingDispatcher(Queue.Enqueue);
            Tabs.BindPublishingStatus(Composition, Composition);
        }
        internal void Deliver() { while (Queue.TryDequeue(out var action)) action(); }
        internal async Task Refresh() { await Tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver(); }
        internal async Task Act(Task task) { await task.WaitAsync(Limit); Deliver(); }
        public async ValueTask DisposeAsync() { await Tabs.DisposeAsync(); Deliver(); await Composition.DisposeAsync(); }
    }

    [Fact]
    public async Task ExplicitRevealMaskAndCopyUseActualKeyAndHaveNoLifecycleSideEffects()
    {
        await using var h = new Harness(); await h.Refresh();
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
        Assert.Equal(0, h.Owner.Starts);
        await h.Act(h.Tabs.TogglePublishingKeyAsync());
        Assert.Equal(Key, h.Tabs.PublishingKeyDisplay);
        await h.Act(h.Tabs.TogglePublishingKeyAsync());
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
        string? copied = null;
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(true, (value, valid) =>
        { if (!valid()) return false; copied = value; return true; }));
        Assert.Equal(Key, copied); Assert.Equal("コピーしました", h.Tabs.PublishingConnectionFeedback);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(false, (value, valid) =>
        { if (!valid()) return false; copied = value; return true; }));
        Assert.Equal(Server, copied);
        Assert.Equal(1, h.Factory.Creates); Assert.Equal(0, h.Owner.Starts);
        Assert.Equal(0, h.Owner.Finishes); Assert.Equal(0, h.Owner.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyFailureAndExceptionFeedbackNeverContainPrivateValues(bool throws)
    {
        await using var h = new Harness(); await h.Refresh();
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(true, (_, _) =>
            throws ? throw new InvalidOperationException(Key) : false));
        Assert.Contains("コピーできません", h.Tabs.PublishingConnectionFeedback);
        Assert.DoesNotContain(Key, h.Tabs.PublishingConnectionFeedback);
        Assert.Equal(0, h.Owner.Starts); Assert.Equal(0, h.Owner.Disposals);
    }

    [Fact]
    public async Task RebindingClearsRevealImmediatelyAndRejectsQueuedOldCopyEvenWithSameGeneration()
    {
        await using var old = new Harness(); await using var next = new Harness();
        await old.Refresh(); await old.Act(old.Tabs.TogglePublishingKeyAsync());
        Assert.Equal(Key, old.Tabs.PublishingKeyDisplay);
        var writes = 0;
        await old.Tabs.CopyPublishingConnectionAsync(true, (_, valid) => { if (valid()) writes++; return valid(); }).WaitAsync(Limit);
        old.Tabs.BindPublishingStatus(next.Composition, next.Composition);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, old.Tabs.PublishingKeyDisplay);
        Assert.False(old.Tabs.PublishingConnection.CanUseKey);
        old.Deliver();
        Assert.Equal(0, writes);
        await old.Refresh();
        Assert.Equal(next.Owner.Current.SessionId.Value, old.Tabs.PublishingConnection.Identity?.SessionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WindowClosureTabChangeAndDisposeRejectQueuedDisclosure(int stop)
    {
        await using var h = new Harness(); await h.Refresh();
        var writes = 0;
        await h.Tabs.CopyPublishingConnectionAsync(true, (_, valid) => { if (valid()) writes++; return valid(); }).WaitAsync(Limit);
        if (stop == 0) h.Tabs.SetPublishingDispatcher(null);
        else if (stop == 1) h.Tabs.SelectedTab = GenesisGuiNextTab.Channels;
        else await h.Tabs.DisposeAsync();
        h.Deliver();
        Assert.Equal(0, writes);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
    }

    [Fact]
    public async Task ClipboardWriterRechecksAfterRebindingImmediatelyBeforeItsWrite()
    {
        await using var h = new Harness(); await using var next = new Harness(); await h.Refresh();
        var wrote = false;
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(true, (_, valid) =>
        {
            Assert.True(valid());
            h.Tabs.BindPublishingStatus(next.Composition, next.Composition);
            if (!valid()) return false;
            wrote = true; return true;
        }));
        Assert.False(wrote);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
    }

    [Fact]
    public async Task OwnerCompletionBetweenReadAndOsWriteRefusesCopyBeforeNextUiPoll()
    {
        await using var h = new Harness();
        await h.Composition.RequestStartAsync().WaitAsync(Limit); await h.Refresh();
        var wrote = false;
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(true, (_, valid) =>
        {
            Assert.True(valid());
            h.Owner.End(h.Owner.Local(ObsPublishLocalTerminationOutcome.Cancelled));
            if (!valid()) return false;
            wrote = true; return true;
        }));
        Assert.False(wrote);
        await h.Refresh();
        Assert.False(h.Tabs.PublishingConnection.CanUseKey);
    }

    [Fact]
    public async Task StartBetweenPlannedReadAndClipboardWriteInvalidatesThatDisclosure()
    {
        await using var h = new Harness(); await h.Refresh();
        var wrote = false;
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(false, (_, valid) =>
        {
            Assert.True(valid());
            h.Composition.RequestStartAsync().WaitAsync(Limit).GetAwaiter().GetResult();
            if (!valid()) return false;
            wrote = true; return true;
        }));
        Assert.False(wrote); Assert.Equal(1, h.Owner.Starts);
        await h.Refresh(); Assert.Contains("待受準備完了", h.Tabs.PublishingConnection.StateText);
    }

    [Fact]
    public async Task NavigatingAwayAndBackCannotResurrectQueuedReveal()
    {
        await using var h = new Harness(); await h.Refresh();
        await h.Tabs.TogglePublishingKeyAsync().WaitAsync(Limit); // Publication held in UI queue.
        h.Tabs.SelectedTab = GenesisGuiNextTab.Channels;
        h.Tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        h.Deliver();
        Assert.False(h.Tabs.PublishingKeyRevealed);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
    }

    [Fact]
    public async Task HigherRevisionRemasksExplicitReveal()
    {
        await using var h = new Harness(); await h.Refresh();
        await h.Act(h.Tabs.TogglePublishingKeyAsync());
        Assert.True(h.Tabs.PublishingKeyRevealed);
        h.Owner.Current = h.Owner.Current with { Revision = 2 };
        await h.Refresh();
        Assert.False(h.Tabs.PublishingKeyRevealed);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, h.Tabs.PublishingKeyDisplay);
    }

    [Fact]
    public async Task DelayedOldRevisionCannotInvalidateNewerAcceptedCopyIdentity()
    {
        var source = new Source(); var reader = new NovaObsPublishingConnectionReader(source);
        var status = Status(source.Current);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var reads = 0;
        source.Read = () =>
        {
            var captured = source.Current;
            if (Interlocked.Increment(ref reads) == 1) { entered.TrySetResult(); release.Wait(Limit); }
            return captured;
        };
        var old = Task.Run(() => reader.Read(1, status));
        try
        {
            await entered.Task.WaitAsync(Limit);
            source.Current = source.Current with { Revision = 4 };
            var latest = Assert.IsType<NovaObsPublishingConnectionInfo>(reader.Read(1, status));
            release.Set(); Assert.Null(await old.WaitAsync(Limit));
            Assert.True(reader.IsCurrent(latest.Identity));
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task BorrowedReadReturningAfterRebindCannotPublishOrRevealOldValues()
    {
        await using var old = new Harness(); await using var next = new Harness();
        await old.Refresh(); await old.Act(old.Tabs.TogglePublishingKeyAsync());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var reads = 0;
        old.Owner.ReadSnapshot = () =>
        {
            if (Interlocked.Increment(ref reads) == 2) { entered.TrySetResult(); release.Wait(Limit); }
            return old.Owner.Current;
        };
        var pending = old.Tabs.RefreshPublishingStatusAsync();
        try
        {
            await entered.Task.WaitAsync(Limit);
            old.Tabs.BindPublishingStatus(next.Composition, next.Composition);
            old.Deliver();
            Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, old.Tabs.PublishingKeyDisplay);
            release.Set(); await pending.WaitAsync(Limit); old.Deliver();
            Assert.Equal(next.Owner.Current.SessionId.Value, old.Tabs.PublishingConnection.Identity?.SessionId);
            Assert.False(old.Tabs.PublishingKeyRevealed);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task RevisionChangedSinceDisplayRejectsCopyAndRefreshesWithoutClipboardWrite()
    {
        await using var h = new Harness(); await h.Refresh();
        h.Owner.Current = h.Owner.Current with { Revision = 5 };
        var writes = 0;
        await h.Act(h.Tabs.CopyPublishingConnectionAsync(true, (_, _) => { writes++; return true; }));
        Assert.Equal(0, writes);
        await h.Refresh();
        Assert.Equal(5, h.Tabs.PublishingConnection.Identity?.SourceRevision);
        Assert.True(h.Tabs.PublishingConnection.CanUseKey);
    }

    private sealed class SequenceFactory(params Owner[] owners) : INovaObsPublishingRuntimeFactory
    {
        private int _index;
        public INovaObsPublishingRuntime Create() => owners[Interlocked.Increment(ref _index) - 1];
    }

    [Fact]
    public async Task RestartUsesFreshGenerationAndDoesNotCarryRevealedKey()
    {
        var first = new Owner(); var next = new Owner();
        first.Current = first.Current with { PublishName = Key, RtmpServer = Server };
        next.Current = next.Current with { PublishName = "next-key", RtmpServer = "rtmp://127.0.0.1:18700/next" };
        await using var composition = NovaGuiNextPublishingComposition.Create(new SequenceFactory(first, next));
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection()) { SelectedTab = GenesisGuiNextTab.Publishing };
        var queue = new ConcurrentQueue<Action>();
        void Deliver() { while (queue.TryDequeue(out var action)) action(); }
        tabs.SetPublishingDispatcher(queue.Enqueue); tabs.BindPublishingStatus(composition, composition);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver();
        await tabs.TogglePublishingKeyAsync().WaitAsync(Limit); Deliver();
        Assert.Equal(Key, tabs.PublishingKeyDisplay);
        await composition.RequestStartAsync().WaitAsync(Limit);
        await composition.RequestInterruptAsync().WaitAsync(Limit);
        await composition.RequestStartAsync().WaitAsync(Limit);
        await tabs.RefreshPublishingStatusAsync().WaitAsync(Limit); Deliver();
        Assert.Equal(2, tabs.PublishingConnection.Identity?.Generation);
        Assert.Equal(next.Current.SessionId.Value, tabs.PublishingConnection.Identity?.SessionId);
        Assert.Equal(NovaObsPublishingConnectionPresentation.Mask, tabs.PublishingKeyDisplay);
        Assert.Equal(1, first.Starts); Assert.Equal(1, first.Disposals); Assert.Equal(1, next.Starts);
    }

    [Fact]
    public async Task FailedBorrowedReadDoesNotExposeExceptionOrLeaveRevealVisible()
    {
        await using var h = new Harness(); await h.Refresh();
        await h.Act(h.Tabs.TogglePublishingKeyAsync());
        h.Owner.ReadSnapshot = () => throw new InvalidOperationException(Key);
        await h.Refresh();
        Assert.False(h.Tabs.PublishingConnection.CanUseKey);
        Assert.False(h.Tabs.PublishingKeyRevealed);
        Assert.DoesNotContain(Key, h.Tabs.PublishingConnection.ToString());
        Assert.DoesNotContain(Key, Assert.Single(h.Tabs.PublishingRows).DiagnosticText);
        h.Owner.ReadSnapshot = null;
    }

    [Fact]
    public async Task ReadFromPreviousWindowLifetimeCannotPublishConnectionIntoReattachedWindow()
    {
        await using var h = new Harness(); await h.Refresh();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var reads = 0;
        h.Owner.ReadSnapshot = () =>
        {
            var captured = h.Owner.Current;
            if (Interlocked.Increment(ref reads) == 2) { entered.TrySetResult(); release.Wait(Limit); }
            return captured;
        };
        var seen = new List<string>();
        h.Tabs.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GenesisGuiNextTabsViewModel.PublishingConnection))
                seen.Add(h.Tabs.PublishingConnection.ServerText);
        };
        var pending = h.Tabs.RefreshPublishingStatusAsync();
        try
        {
            await entered.Task.WaitAsync(Limit);
            h.Tabs.SetPublishingDispatcher(null);
            h.Owner.Current = h.Owner.Current with { Revision = 4, RtmpServer = "rtmp://127.0.0.1:18711/new" };
            h.Tabs.SetPublishingDispatcher(h.Queue.Enqueue);
            release.Set(); await pending.WaitAsync(Limit); h.Deliver();
            await h.Refresh();
            Assert.DoesNotContain(Server, seen);
            Assert.Equal("rtmp://127.0.0.1:18711/new", h.Tabs.PublishingConnection.ServerText);
            Assert.False(h.Tabs.PublishingKeyRevealed);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(800)]
    [InlineData(1200)]
    [InlineData(1600)]
    public void ExistingPaneLayoutKeepsLongValueFieldsAndButtonsSeparate(int width)
    {
        var layout = GenesisGuiNextPublishingGuideLayout.ForWidth(width);
        Assert.True(layout.ServerWidth > 0); Assert.True(layout.KeyWidth > 0);
        Assert.True(94 + layout.ServerWidth < layout.CopyLeft);
        Assert.True(94 + layout.KeyWidth < layout.RevealLeft);
        Assert.True(layout.RevealLeft + 64 < layout.CopyLeft);
        Assert.True(layout.CopyLeft + 72 <= width - 12);
    }

    [Theory]
    [InlineData("OBSから配信を受信中", "メディア初期化受信済み")]
    [InlineData("OBS接続中", "入力接続あり・メディア待ち")]
    [InlineData("OBS待機中", "待受準備完了（このPCのみ）")]
    public void OrdinaryPublishingRowUsesTheSameLimitedStateMeaning(string legacy, string expected)
    {
        var status = Status(Raw(), false) with { StateText = legacy };
        Assert.Equal(expected, GenesisGuiNextPublishingPresentation.Values(status)[0]);
    }
}
