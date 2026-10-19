using System.Globalization;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRecordingHistoryTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 8, 1, 2, 3, TimeSpan.Zero);
    private static readonly Guid Session = Guid.Parse("12345678-1111-2222-3333-444444444444");
    private static readonly Guid Channel = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Rule = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly string PathValue = Path.GetFullPath(Path.Combine("synthetic-history", "観測 & channel.flv"));

    [Fact]
    public async Task FourthModePreservesNumericValuesOrderAndInertStartup()
    {
        var provider = new Queries();
        await using var tabs = Tabs(provider);
        Assert.Equal([0, 1, 2, 3], GenesisGuiNextRecordingPageModePresentation.Items.Select(x => (int)x.Mode));
        Assert.Equal(["録画中", "ライブラリ", "ルール", "履歴"], GenesisGuiNextRecordingPageModePresentation.Items.Select(x => x.Label));
        Assert.Equal(GenesisGuiNextRecordingPageMode.ActiveRecordings, tabs.RecordingPageMode);
        Assert.Equal(GenesisGuiNextTab.Channels, tabs.SelectedTab);
        Assert.Equal((0, 0, 0), provider.Counts);
        _ = GenesisGuiNextNativeTabProjection.Create(tabs);
        Assert.Equal((0, 0, 0), provider.Counts);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.History);
        Assert.Equal((1, 1, 1), provider.Counts);
        Assert.Empty(tabs.RecordingHistoryRows);
        Assert.Equal("保持している録画履歴はありません", tabs.RecordingHistoryStatus.Text);
        Assert.False(tabs.RecordingHistoryStatus.IsWarning);
        Assert.Equal(0, provider.Scans);
    }

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.Completed, RecordingFileWriteCompletionKind.EndOfStream, "完了（入力終了）")]
    [InlineData(RecordingSinkSessionLifecycleKind.Completed, RecordingFileWriteCompletionKind.Stopped, "完了（停止）")]
    [InlineData(RecordingSinkSessionLifecycleKind.NoDecodableVideoStart, null, "映像開始なし")]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted, null, "失敗")]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted, RecordingFileWriteCompletionKind.Stopped, "失敗")]
    public void TerminalResultsAndSevenColumnsUseImmutableWriterFacts(
        RecordingSinkSessionLifecycleKind kind, RecordingFileWriteCompletionKind? completion, string expected)
    {
        var fact = Fact(kind: kind, completion: completion);
        var history = Snapshot(fact);
        var row = GenesisGuiNextRecordingHistoryProjection.CreateRows(history, Library(fact)).Single();
        Assert.Equal(expected, row.Result);
        Assert.Same(fact, row.Fact);
        Assert.Equal(["結果", "チャンネル", "終端観測日時", "録画種別", "ファイル", "記録バイト数", "関連ファイル"],
            GenesisRecordingHistoryListPresentation.Columns.Select(x => x.Header));
        Assert.Equal(7, GenesisRecordingHistoryListPresentation.CreateRowValues(row).Length);
        Assert.Equal(completion is null ? "—" : "123 B", row.Bytes);
        Assert.Equal(completion is null ? "確定ファイル情報なし" : Path.GetFileName(PathValue), row.Filename);
        Assert.DoesNotContain("Cancelled", row.Result);
        if (kind == RecordingSinkSessionLifecycleKind.Faulted)
            Assert.Contains("ExceptionType: System.IO.IOException", row.Details);
    }

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.Active)]
    [InlineData((RecordingSinkSessionLifecycleKind)99)]
    public void InvalidTerminalKindsFailClosedAtExistingContract(RecordingSinkSessionLifecycleKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fact(kind: kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginRuleAndUnknownMetadataAreHistorical(bool automatic)
    {
        var fact = Fact(automatic: automatic, name: null, bytes: 0);
        var row = Row(fact);
        Assert.Equal("不明", row.Channel);
        Assert.Equal(automatic ? "自動" : "手動", row.Origin);
        Assert.Contains(automatic ? Rule.ToString("D") : "なし（手動）", row.Details);
        Assert.Contains("StreamType: 不明", row.Details);
        Assert.Contains("RequestedFileExtension（要求値・メディア未検証）: 不明", row.Details);
        Assert.Contains(Session.ToString("N"), row.Details);
        Assert.Contains(Channel.ToString("N"), row.Details);
        Assert.Equal("0 B", row.Bytes);
        Assert.DoesNotContain("duration", row.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void BytesAndObservationUtcDoNotDependOnMachineCulture(string culture)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var row = Row(Fact(bytes: 123456));
            Assert.Equal("123,456 B", row.Bytes);
            Assert.Equal("2026-10-08 01:02:03 UTC", row.Terminal);
            Assert.Contains("受入観測日時: 2026-10-08 02:02:03.0000000 UTC", row.Details);
            Assert.Contains("終端観測日時:", row.Details);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void OrderUsesReadModelGuidComparerAndRestoredSnapshot()
    {
        var model = new GenesisRecordingHistoryReadModel();
        var first = Fact(id: Guid.Parse("80000000-0000-0000-0000-000000000000"));
        var second = Fact(id: Guid.Parse("00000001-ffff-ffff-ffff-ffffffffffff"));
        model.ObserveTerminal(second); model.ObserveTerminal(first);
        var history = model.GetSnapshot();
        var expected = new[] { first, second }.OrderByDescending(x => x.TerminalObservedAtUtc)
            .ThenByDescending(x => x.Session.SessionId.Value).ToArray();
        Assert.Equal(expected, history.Entries);
        var restored = new GenesisRecordingHistoryReadModel(); restored.Hydrate(history);
        Assert.Equal(expected.Select(x => x.Session.SessionId.Value),
            GenesisGuiNextRecordingHistoryProjection.CreateRows(restored.GetSnapshot(), InitialLibrary()).Select(x => x.SessionId));
        Assert.Throws<InvalidDataException>(() =>
            GenesisGuiNextRecordingHistoryProjection.CreateRows(Snapshot(first, first), InitialLibrary()));
    }

    [Theory]
    [InlineData((int)GenesisRecordingLibraryAvailability.Available, "利用可能（Library観測）")]
    [InlineData((int)GenesisRecordingLibraryAvailability.Missing, "見つかりません（Library観測）")]
    [InlineData((int)GenesisRecordingLibraryAvailability.Unavailable, "確認できません（Library観測）")]
    public void AssociationRequiresPathAndActualSessionAndNeverChangesTerminal(
        int availabilityValue, string expected)
    {
        var availability = (GenesisRecordingLibraryAvailability)availabilityValue;
        var fact = Fact(kind: RecordingSinkSessionLifecycleKind.Faulted);
        var snapshot = Library(fact, availability);
        var row = GenesisGuiNextRecordingHistoryProjection.CreateRows(Snapshot(fact), snapshot).Single();
        Assert.Equal(expected, row.Association);
        Assert.Equal("失敗", row.Result);
        Assert.Equal("123 B", row.Bytes);
        Assert.Contains("Library観測サイズ: 999 B", row.Details);
        Assert.Contains("現在の観測サイズは録画時の記録と異なります", row.Details);
        Assert.Contains("cached観測", row.Details);
        var other = Fact(id: Guid.NewGuid());
        var stale = snapshot with { Entries = [snapshot.Entries[0] with { HistoryFacts = [other] }] };
        Assert.Equal("パス一致・履歴未反映", GenesisGuiNextRecordingHistoryProjection.CreateRows(Snapshot(fact), stale)[0].Association);
    }

    [Theory]
    [InlineData("notObserved", "未確認（Library未更新）")]
    [InlineData("outside", "関連未確認（snapshot対象外）")]
    [InlineData("differentPath", "関連未確認（snapshot対象外）")]
    [InlineData("filenameClaim", "パス一致・履歴未反映")]
    [InlineData("case", "platform")]
    public void UnobservedCoverageAndFilenameClaimsNeverBecomeMissing(string scenario, string expected)
    {
        var fact = Fact();
        var library = Library(fact);
        library = scenario switch
        {
            "notObserved" => InitialLibrary(),
            "outside" => library with { Entries = [], IsSnapshotTruncated = true, ExcludedHistoryPathCount = 1 },
            "differentPath" => library with { Entries = [library.Entries[0] with { Path = Path.Combine(Path.GetDirectoryName(PathValue)!, "other", Path.GetFileName(PathValue)) }] },
            "filenameClaim" => library with
            {
                Entries = [library.Entries[0] with
                { HistoryFacts = [], FilenameClaims = new(Time, "Not a historical name", Channel, Session) }]
            },
            "case" => library with { Entries = [library.Entries[0] with { Path = PathValue.ToUpperInvariant() }] },
            _ => throw new InvalidOperationException(),
        };
        if (expected == "platform") expected = OperatingSystem.IsWindows() ? "利用可能（Library観測）" : "関連未確認（snapshot対象外）";
        Assert.Equal(expected, GenesisGuiNextRecordingHistoryProjection.CreateRows(Snapshot(fact), library)[0].Association);
    }

    [Fact]
    public void SharedPathKeepsEverySessionWithoutChoosingWinner()
    {
        var first = Fact(); var second = Fact(id: Guid.NewGuid(), automatic: true);
        var library = Library(first) with { Entries = [Library(first).Entries[0] with { HistoryFacts = [first, second] }] };
        var rows = GenesisGuiNextRecordingHistoryProjection.CreateRows(Snapshot(first, second), library);
        Assert.Equal(2, rows.Count); Assert.NotEqual(rows[0].SessionId, rows[1].SessionId);
        Assert.All(rows, row => Assert.Contains("同パス参照の履歴: 2 件", row.Details));
    }

    [Theory]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.Invalid, (int)GenesisRecordingHistoryWriteStatus.None, "履歴読込: Invalid")]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.UnsupportedVersion, (int)GenesisRecordingHistoryWriteStatus.None, "UnsupportedVersion")]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.Unavailable, (int)GenesisRecordingHistoryWriteStatus.None, "Unavailable")]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.Available, (int)GenesisRecordingHistoryWriteStatus.Failed, "メタデータ保存: Failed")]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.Available, (int)GenesisRecordingHistoryWriteStatus.Conflict, "メタデータ保存: Conflict")]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.Available, (int)GenesisRecordingHistoryWriteStatus.Cancelled, "メタデータ保存: Cancelled")]
    public void LoadAndDurabilityWarningsNeverEraseLiveFacts(
        int loadValue, int writeValue, string expected)
    {
        var load = (GenesisRecordingHistoryLoadStatus)loadValue;
        var write = (GenesisRecordingHistoryWriteStatus)writeValue;
        var history = Snapshot(Fact()) with { EvictedEntryCount = 3, PublicationFailureCount = 2 };
        var durability = new GenesisRecordingHistoryDurabilitySnapshot(load, 1, 2, write, GenesisRecordingHistoryStoreFailure.Io, false);
        var status = GenesisGuiNextRecordingHistoryProjection.Status(history, durability, false);
        Assert.True(status.IsWarning); Assert.Contains(expected, status.Text);
        Assert.Contains("保持履歴 1 件（上限4096件）", status.Text);
        Assert.Contains("保持範囲外 3 件", status.WarningDetails); Assert.Contains("履歴の記録失敗 2 件", status.WarningDetails);
        Assert.Contains("保存待ち（History＋KnownRoots）", status.WarningDetails);
        Assert.DoesNotContain("総録画", status.Text);
        Assert.Single(GenesisGuiNextRecordingHistoryProjection.CreateRows(history, InitialLibrary()));
    }

    [Theory]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.Available)]
    [InlineData((int)GenesisRecordingHistoryLoadStatus.MissingFile)]
    public void AvailableOrMissingStoreWithNoFactsIsNormalEmpty(int loadValue)
    {
        var load = (GenesisRecordingHistoryLoadStatus)loadValue;
        var status = GenesisGuiNextRecordingHistoryProjection.Status(Snapshot(), Healthy() with { LoadStatus = load }, false);
        Assert.False(status.IsWarning); Assert.Equal("保持している録画履歴はありません", status.Text);
    }

    [Fact]
    public async Task DurabilityOnlyOrUnchangedQueriesPreserveRowsAndNativeRebuildIdentity()
    {
        var provider = new Queries { History = Snapshot(Fact()) };
        await using var tabs = Tabs(provider); await Enter(tabs);
        var rows = tabs.RecordingHistoryRows;
        var identity = GenesisGuiNextNativeRowRebuildIdentity.Create(rows, tabs.SelectedTab, tabs.RecordingPageMode, tabs.SuppressionRows);
        provider.Durability = Healthy() with { LatestRevision = 2, LastWriteStatus = GenesisRecordingHistoryWriteStatus.Failed };
        await tabs.RefreshRecordingHistoryAsync();
        Assert.Same(rows, tabs.RecordingHistoryRows);
        Assert.True(tabs.RecordingHistoryStatus.IsWarning);
        Assert.Contains("Failed", tabs.RecordingHistoryDetails(rows[0]));
        Assert.False(GenesisGuiNextNativeRowRebuildIdentity.Create(tabs.RecordingHistoryRows, tabs.SelectedTab,
            tabs.RecordingPageMode, tabs.SuppressionRows).RequiresRebuild(identity));
        await tabs.RefreshRecordingHistoryAsync();
        Assert.Same(rows, tabs.RecordingHistoryRows);
        provider.History = Snapshot(Fact(), Fact(id: Guid.NewGuid())) with { Revision = 2 };
        await tabs.RefreshRecordingHistoryAsync();
        Assert.Equal(2, tabs.RecordingHistoryRows.Count);
        Assert.NotSame(rows, tabs.RecordingHistoryRows);
        Assert.Equal(0, provider.Scans);
    }

    [Fact]
    public async Task QueryFailureIsUnavailableInitiallyAndRetainsPreviouslySuccessfulRows()
    {
        var provider = new Queries { Handler = () => throw new IOException("RAW SECRET") };
        await using var tabs = Tabs(provider); await Enter(tabs);
        Assert.Contains(GenesisGuiNextRecordingHistoryProjection.UnavailableText, tabs.RecordingHistoryStatus.Text);
        Assert.DoesNotContain("RAW SECRET", tabs.RecordingHistoryStatus.Text);
        Assert.DoesNotContain("保持している録画履歴はありません", tabs.RecordingHistoryStatus.Text);
        provider.Handler = null; provider.History = Snapshot(Fact());
        await tabs.RefreshRecordingHistoryAsync(); var rows = tabs.RecordingHistoryRows;
        provider.Handler = () => throw new IOException("RAW SECRET");
        await tabs.RefreshRecordingHistoryAsync(); Assert.Same(rows, tabs.RecordingHistoryRows);
        Assert.True(tabs.RecordingHistoryStatus.IsWarning);
    }

    [Fact]
    public async Task UnavailableCapabilityDoesNotQueryAndIsNotEmptySuccess()
    {
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        await Enter(tabs);
        Assert.False(tabs.CanRefreshRecordingHistory); Assert.True(tabs.RecordingHistoryStatus.IsWarning);
        Assert.Equal(GenesisGuiNextRecordingHistoryProjection.UnavailableText, tabs.RecordingHistoryStatus.Text);
    }

    [Fact]
    public async Task DuplicateProviderIdentityClearsRowsAndCannotSelectArbitraryWinner()
    {
        var fact = Fact(); var provider = new Queries { History = Snapshot(fact) };
        await using var tabs = Tabs(provider); await Enter(tabs);
        provider.History = Snapshot(fact, Fact(name: "conflict"));
        await tabs.RefreshRecordingHistoryAsync();
        Assert.Empty(tabs.RecordingHistoryRows); Assert.True(tabs.RecordingHistoryStatus.IsWarning);
        Assert.DoesNotContain("conflict", tabs.RecordingHistoryDetails(null));
    }

    [Fact]
    public async Task QueriesCoalesceToOnePendingWakeAndNeverOverlap()
    {
        var started = Gate(); var release = Gate(); var provider = new Queries();
        provider.Handler = () =>
        {
            if (provider.HistoryCalls == 1) { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
            return provider.History;
        };
        await using var tabs = Tabs(provider);
        var first = Enter(tabs); await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var wakes = Enumerable.Range(0, 100).Select(_ => tabs.RefreshRecordingHistoryAsync()).ToArray();
        Assert.All(wakes, task => Assert.Same(wakes[0], task));
        Assert.Equal(1, provider.HistoryCalls);
        release.TrySetResult(); await first.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(wakes).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((2, 2, 2), provider.Counts); Assert.Equal(1, provider.MaxConcurrent);
    }

    [Fact]
    public async Task ModeChangeAndReentryDiscardOldGenerationAndPublishOnlyCurrentResult()
    {
        var started = Gate(); var release = Gate(); var provider = new Queries();
        var oldFact = Fact(name: "OLD"); var newFact = Fact(name: "NEW", id: Guid.NewGuid());
        provider.Handler = () =>
        {
            if (provider.HistoryCalls == 1) { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Snapshot(oldFact); }
            return Snapshot(newFact);
        };
        await using var tabs = Tabs(provider);
        var first = Enter(tabs); await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.Empty(tabs.RecordingHistoryRows);
        var reenter = tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.History);
        release.TrySetResult();
        await Task.WhenAll(first, reenter).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("NEW", Assert.Single(tabs.RecordingHistoryRows).Channel);
        Assert.Equal(2, provider.HistoryCalls); Assert.Equal(1, provider.DurabilityCalls);
    }

    [Fact]
    public async Task LeavingHistoryDiscardsLateResultAndKeepsOtherModeSchema()
    {
        var started = Gate(); var release = Gate(); var provider = new Queries();
        provider.Handler = () => { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Snapshot(Fact()); };
        await using var tabs = Tabs(provider); var first = Enter(tabs);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        release.TrySetResult(); await first;
        Assert.Empty(tabs.RecordingHistoryRows);
        Assert.Same(GenesisRecordingLibraryListPresentation.Columns,
            GenesisGuiNextRecordingListPresentation.Columns(tabs.SelectedTab, tabs.RecordingPageMode));
        Assert.Equal(0, provider.DurabilityCalls);
    }

    [Fact]
    public async Task DisposeDrainsBlockedQueryAndPublishesNoLateNotificationsOrOwnerDisposal()
    {
        var started = Gate(); var release = Gate(); var provider = new Queries();
        provider.Handler = () => { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Snapshot(Fact()); };
        var tabs = Tabs(provider); var first = Enter(tabs);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposal = tabs.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        var notifications = 0; tabs.PropertyChanged += (_, _) => notifications++;
        release.TrySetResult(); await Task.WhenAll(first, disposal).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(tabs.RecordingHistoryRows); Assert.Equal(0, notifications);
        Assert.Equal(0, provider.DurabilityCalls); Assert.Equal(0, provider.OwnerDisposals);
        await tabs.RefreshRecordingHistoryAsync(); Assert.Equal(1, provider.HistoryCalls);
        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task CallerCancellationDiscardsQueryAndCancelledRefreshDoesNotRun()
    {
        var provider = new Queries();
        await using var tabs = Tabs(provider); await Enter(tabs);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await tabs.RefreshRecordingHistoryAsync(cancellation.Token);
        Assert.Equal(1, provider.HistoryCalls);
        var started = Gate(); var release = Gate();
        provider.Handler = () => { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Snapshot(Fact()); };
        using var inFlightCancellation = new CancellationTokenSource();
        var task = tabs.RefreshRecordingHistoryAsync(inFlightCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); inFlightCancellation.Cancel();
        release.TrySetResult(); await task; Assert.Empty(tabs.RecordingHistoryRows);
    }

    [Theory]
    [InlineData((int)GenesisGuiNextRecordingPageMode.Library)]
    [InlineData((int)GenesisGuiNextRecordingPageMode.Rules)]
    [InlineData((int)GenesisGuiNextRecordingPageMode.ActiveRecordings)]
    public void HistorySessionAnchorSurvivesOtherModesWithoutChangingTheirAnchor(int modeValue)
    {
        var mode = (GenesisGuiNextRecordingPageMode)modeValue;
        var list = (nint)(0x71448000 + (int)mode);
        var first = Native(Fact()); var second = Native(Fact(id: Guid.NewGuid()));
        var other = mode switch
        {
            GenesisGuiNextRecordingPageMode.Library => new GenesisGuiNextNativeTabRow(new("", ""), null,
                RecordingLibrary: GenesisGuiNextRecordingLibraryProjection.CreateRow(Library(Fact()).Entries[0])),
            GenesisGuiNextRecordingPageMode.Rules => new GenesisGuiNextNativeTabRow(new("", ""), null,
                RecordingRule: new(Rule, 0, "Existing rule", false, RecordingRuleAction.Record, 0, "criteria")),
            _ => new GenesisGuiNextNativeTabRow(new("", ""), null,
                ActiveRecording: new(new(Channel), new(Session), RecordingSessionOrigin.Manual, "Active", "録画中", null, null)),
        };
        try
        {
            GenesisChannelListRenderedChannels.SetRows(list, [first, second], (_, _) => true, historyMode: true);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(list, 1);
            GenesisChannelListRenderedChannels.SetRows(list, [other], (_, _) => true, historyMode: false);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(list, 0);
            var restored = -1;
            GenesisChannelListRenderedChannels.SetRows(list, [second, first], (_, row) => { restored = row; return true; }, true);
            Assert.Equal(0, restored);
            GenesisChannelListRenderedChannels.SetRows(list, [other], (_, row) => { restored = row; return true; }, false);
            Assert.Equal(0, restored);
            // Removed history identity clears, no adjacent/index fallback.
            GenesisChannelListRenderedChannels.SetRows(list, [first], (_, _) => throw new InvalidOperationException(), true);
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(list, [first]));
        }
        finally { GenesisChannelListRenderedChannels.Remove(list); }
    }

    [Fact]
    public void AmbiguousNativeHistoryAnchorClearsInsteadOfRestoringFirstMatch()
    {
        var list = (nint)0x71448008; var row = Native(Fact());
        try
        {
            GenesisChannelListRenderedChannels.SetRows(list, [row], (_, _) => true, true);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(list, 0);
            GenesisChannelListRenderedChannels.SetRows(list, [row, row], (_, _) => throw new InvalidOperationException(), true);
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(list, [row]));
        }
        finally { GenesisChannelListRenderedChannels.Remove(list); }
    }

    [Fact]
    public async Task HistoryDispatchDeniesAllActionsEvenWithGlobalCanStopAndStaleChannel()
    {
        var controller = new FakePlaybackSessionController();
        var vm = new GenesisMainViewModel(controller);
        await vm.PlayAsync(); // empty request cannot start
        var interactionWithoutHistory = new GenesisMainWindowInteraction(vm);
        await interactionWithoutHistory.PlaySourceAsync("pcp://fake/current");
        Assert.True(vm.CanStop);
        var provider = new Queries { History = Snapshot(Fact()) };
        await using var tabs = Tabs(provider); var catalog = new PoisonCatalog();
        await using var session = GenesisChannelCatalogSession.Create(catalog, vm,
            new GenesisGuiNextChannelPresencePublisher(tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance));
        var interaction = new GenesisMainWindowInteraction(vm, session);
        await Enter(tabs);
        var row = GenesisGuiNextNativeTabProjection.Create(tabs).Single();
        var stale = new GenesisChannelItem("Stale", "pcp://fake/stale");
        vm.ReplaceChannels([stale]); vm.SelectedChannel = stale;
        Assert.Null(row.InteractiveChannel); Assert.False(row.IsInteractive);
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(vm, tabs.SelectedTab, row, tabs.RecordingPageMode).IsStopEnabled);
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(vm, tabs.SelectedTab, null, tabs.RecordingPageMode).IsStopEnabled);
        await interaction.StopAsync(); // Escape and hidden SourceEnter/legacy Stop share this boundary
        await GenesisGuiNextPlaybackCommandDispatch.StopAsync(vm, interaction);
        Assert.Equal(PlaybackSessionState.Playing, controller.Snapshot.State);
        Assert.False(await interaction.ActivateSelectedChannelAsync());
        Assert.False(await interaction.ActivateChannelAsync(stale));
        await interaction.PlaySourceAsync("pcp://fake/replacement");
        Assert.Equal("pcp://fake/current", controller.Snapshot.Source);
        Assert.False(await GenesisGuiNextPlaybackCommandDispatch.ActivateSelectedChannelAsync(vm, interaction, tabs.SelectedTab, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(vm, tabs.SelectedTab, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopSelectedAsync(vm, tabs.SelectedTab, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(vm, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(vm, row));
        Assert.False(GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(tabs, row).IsEnabled);
        Assert.Equal(GenesisGuiNextFavoriteToggleResult.Unavailable, GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.Null(tabs.CreateRecordingRule()); Assert.Null(tabs.EditRecordingRule(Rule));
        Assert.False(tabs.CanQuickMutateRecordingRule(Rule));
        Assert.False(GenesisChannelDirectContextMenuDispatch.TryOpen(row, _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()));
        Assert.Null(GenesisChannelContextMenuKeyboard.ResolveRenderedTarget((nint)1, (_, _) => 0, (_, _) => row));
        Assert.Null(GenesisChannelDirectContextMenuRowResolver.Resolve(0, new(stale, stale), vm, tabs));
        Assert.Null(GenesisChannelContextMenuFavoritePresentation.Resolve(tabs, row));
        var calls = provider.HistoryCalls;
        await interaction.RefreshCurrentViewAsync(); // F5 / top Refresh
        Assert.Equal(calls + 1, provider.HistoryCalls); Assert.Equal(0, catalog.Calls); Assert.Equal(0, provider.Scans);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        await interaction.StopAsync(); Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
    }

    [Fact]
    public async Task EmptyHistoryRejectsStaleFallbackAndPreservesStopOutsideHistory()
    {
        var vm = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var tabs = Tabs(new Queries());
        await using var session = GenesisChannelCatalogSession.Create(new PoisonCatalog(), vm,
            new GenesisGuiNextChannelPresencePublisher(tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance));
        var interaction = new GenesisMainWindowInteraction(vm, session);
        var old = new GenesisChannelItem("Old", "pcp://fake/old"); vm.ReplaceChannels([old]); vm.SelectedChannel = old;
        await Enter(tabs);
        Assert.Empty(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.False(await interaction.ActivateChannelAsync(0));
        Assert.Null(GenesisChannelDirectContextMenuRowResolver.Resolve(0, new(old, old), vm, tabs));
        Assert.False(GenesisSourceUrlPlaybackDialog.TryShow(0, vm, interaction));
        Assert.False(GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(tabs, null).IsEnabled);
    }

    [Fact]
    public void HistoryNativeStylesLayoutStatusAndCommandIdsMatchContract()
    {
        var ids = new[] { GenesisGuiNextAppShellBridge.RecordingHistoryModeControlId,
            GenesisGuiNextAppShellBridge.RecordingHistoryRefreshControlId, GenesisGuiNextAppShellBridge.RecordingHistoryDetailsControlId };
        Assert.Equal([1128, 1129, 1130], ids); Assert.Equal(3, ids.Distinct().Count());
        Assert.True(GenesisGuiNextAppShellBridge.IsRecordingStatusDraw(5, 1117));
        Assert.False(GenesisGuiNextAppShellBridge.IsRecordingStatusDraw(2, 1117));
        Assert.False(GenesisGuiNextAppShellBridge.IsRecordingStatusDraw(4, 1117));
        Assert.False(GenesisGuiNextAppShellBridge.IsRecordingStatusDraw(5, 1129));
        var details = GenesisGuiNextAppShellBridge.HistoryDetailsLayout;
        var status = GenesisGuiNextAppShellBridge.RecordingStatusLayout(true, true);
        Assert.Equal((40, 70), details); Assert.Equal((114, 32), status);
        Assert.True(details.Top + details.Height < status.Top);
        Assert.True(status.Top + status.Height <= GenesisGuiNextAppShellPlacement.DetailsHeight);
        var style = GenesisGuiNextAppShellBridge.RecordingHistoryDetailsStyle;
        Assert.NotEqual(0u, style & 0x0800); Assert.NotEqual(0u, style & 0x0004);
        Assert.NotEqual(0u, style & 0x00200000); Assert.NotEqual(0u, style & 0x00100000);
        foreach (var id in new[] { 1110, 1112, 1113, 1114, 1115, 1116, 1120, 1121, 1122, 1123, 1124, 1125 })
            Assert.False(GenesisGuiNextAppShellBridge.IsHistoryNavigationCommand(id));
        foreach (var id in new[] { 1100, 1111, 1118, 1119, 1126, 1128, 1129 })
            Assert.True(GenesisGuiNextAppShellBridge.IsHistoryNavigationCommand(id));
    }

    [Fact]
    public void BoundedUnicodeDetailsNeverCutSurrogatePairsOrSourceIdentity()
    {
        var name = new string('あ', 4096 - "…（表示を省略）".Length - 1) + "😀" + new string('x', 40000);
        var fact = Fact(name: name);
        var row = Row(fact);
        Assert.Same(fact, row.Fact); Assert.Equal(name, row.Fact.ChannelName);
        Assert.True(row.Channel.Length <= 4096); Assert.EndsWith("…（表示を省略）", row.Channel);
        Assert.DoesNotContain(row.Channel, char.IsSurrogate);
        Assert.True(row.Details.Length <= 32768); Assert.Contains(Session.ToString("N"), row.Details);
        var bounded = GenesisGuiNextRecordingHistoryProjection.Bound(new string('x', 40000), 32768);
        Assert.Equal(32768, bounded.Length); Assert.EndsWith("…（表示を省略）", bounded);
        var longPath = Path.GetDirectoryName(PathValue)! + Path.DirectorySeparatorChar + new string('字', 5000) + ".flv";
        var longRow = Row(Fact(path: longPath));
        Assert.Equal(longPath, longRow.Fact.WriteResult!.Path);
        Assert.Contains("表示を省略", longRow.Details);
    }

    [Fact]
    public async Task QueryAdapterForwardsOnlyExistingGettersAndLeavesOwnershipInDaily()
    {
        var daily = new QueryDaily(); await using var adapter = new GuiNextDailyPlaybackSessionController(daily);
        var vm = new GenesisMainViewModel(adapter);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection(),
            new GenesisRecordingRuleRepository(path: null), recordingHistoryController: vm.RecordingHistoryController);
        Assert.Equal((0, 0, 0), daily.Queries.Counts); Assert.Equal(0, daily.Commands);
        await Enter(tabs); Assert.Equal((1, 1, 1), daily.Queries.Counts); Assert.Equal(0, daily.Commands);
        Assert.Equal(0, daily.Queries.Scans);
        await tabs.DisposeAsync(); Assert.Equal(0, daily.Queries.OwnerDisposals);
        Assert.Equal(["GetRecordingHistorySnapshot", "GetRecordingHistoryDurabilitySnapshot", "GetRecordingLibrarySnapshot"],
            typeof(IGenesisRecordingHistoryController).GetMethods().Select(x => x.Name));
    }

    [Fact]
    public void SchemaTransitionClearsRowsAndFailsClosedForEveryModePair()
    {
        foreach (var from in Enum.GetValues<GenesisGuiNextRecordingPageMode>())
            foreach (var to in Enum.GetValues<GenesisGuiNextRecordingPageMode>())
            {
                var calls = new List<string>();
                var before = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, from);
                var after = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, to);
                Assert.NotEmpty(before); Assert.NotEmpty(after);
                Assert.True(GenesisGuiNextRecordingListPresentation.TryReplaceColumns(
                    () => { calls.Add("clearRows"); return true; }, () => { calls.Add("replaceColumns"); return true; },
                    () => calls.Add("clearColumns")));
                Assert.Equal(["clearRows", "replaceColumns"], calls);
                calls.Clear();
                Assert.False(GenesisGuiNextRecordingListPresentation.TryReplaceColumns(
                    () => { calls.Add("clearRows"); return true; }, () => { calls.Add("replaceColumns"); return false; },
                    () => calls.Add("clearColumns")));
                Assert.Equal(["clearRows", "replaceColumns", "clearColumns"], calls);
            }
    }

    private static GenesisGuiNextTabsViewModel Tabs(Queries provider) =>
        new(new GenesisFavoriteChannelCollection(), new GenesisRecordingRuleRepository(path: null),
            recordingHistoryController: provider);

    [Fact]
    public void NativeSourceWiresSameImmutableStatusQueryTickAndModeGuardedLegacyRoutes()
    {
        var shell = Source("GenesisGuiNextAppShellBridge.cs");
        var native = Source("SystemGenesisMainWindowInteractionBridgeFactory.cs");
        var sourceEnter = Source("GenesisSourceEnterBridge.cs");
        var legacy = Source("GenesisChannelContextMenuBridge.cs");
        var direct = Source("GenesisChannelDirectContextMenuBridge.cs");
        var history = Source("GenesisGuiNextTabsViewModel.RecordingHistory.cs");
        Assert.Contains("lParam == _recordingHistoryModeButton && commandId == RecordingHistoryModeControlId", shell);
        Assert.Contains("lParam == _recordingHistoryRefreshButton && commandId == RecordingHistoryRefreshControlId", shell);
        Assert.Contains("if (_tabs.IsRecordingHistoryPage && !IsHistoryNavigationCommand(commandId)) return;", shell);
        Assert.Contains("WindowStyleChild | WindowStyleTabStop | RecordingHistoryDetailsStyle", shell);
        Assert.Contains("ApplyDefaultFont(_recordingHistoryModeButton)", shell);
        Assert.Contains("ApplyDefaultFont(_recordingHistoryRefreshButton)", shell);
        Assert.Contains("CreateWindowExW(0, \"EDIT\"", shell);
        Assert.Contains("_ = SetWindowTextW(_detailsRecordingStatus, _historyStatusPresentation.Text);", shell);
        Assert.Contains("_historyStatusPresentation.Text, -1, ref rectangle", shell);
        Assert.Contains("string.Equals(detailsText, _historyDetailsText, StringComparison.Ordinal)", shell);
        Assert.Contains("DrawItemTypeStatic = 5", shell);
        Assert.Contains("AccentWidth = 5", shell);
        Assert.Contains("IsRecordingHistoryView", sourceEnter);
        Assert.Contains("_interaction.StopAsync()", sourceEnter);
        Assert.Contains("_interaction.PlaySourceAsync(", sourceEnter);
        Assert.Contains("_interaction.StopAsync()", legacy);
        Assert.Contains("_interaction.StopAsync()", direct);
        Assert.Contains("VirtualKeyEscape", legacy);
        Assert.Contains("GenesisChannelDirectKeyboardCommand.Stop", direct);
        Assert.Contains("ObserveStatusTick()", native);
        Assert.Contains("_guiNextTabs.RefreshRecordingHistoryAsync()", native);
        Assert.Contains("Volatile.Read(ref _detached)", native);
        Assert.Contains("_guiNextTabs.PropertyChanged -= OnGuiNextTabsPropertyChanged", native);
        Assert.Contains("historyMode: _guiNextTabs?.IsRecordingHistoryPage == true", native);
        foreach (var forbidden in new[] { "File.", "Directory.", "Process.", "RefreshRecordingLibraryAsync",
            "SaveAsync", "FlushAsync", "Register", "ReloadRecordingRules", "RefreshChannels", "Changed +=" })
            Assert.DoesNotContain(forbidden, history);
    }

    [Fact]
    public async Task PendingCallerCancellationDoesNotPublishOrPerformAnExtraQuery()
    {
        var entered = Gate(); var release = Gate(); var provider = new Queries();
        provider.Handler = () => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Snapshot(Fact()); };
        await using var tabs = Tabs(provider); var first = Enter(tabs);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var pending = tabs.RefreshRecordingHistoryAsync(cancellation.Token);
        cancellation.Cancel(); release.TrySetResult();
        await Task.WhenAll(first, pending).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((1, 1, 1), provider.Counts);
        Assert.Single(tabs.RecordingHistoryRows);
    }

    [Fact]
    public void FaultedWithoutDescriptorAndUnavailableDurabilityRemainExplicitlyUnknown()
    {
        var fact = new RecordingSessionTerminalFact(RecordingSinkSession.Manual(new(Session), new(Channel)),
            null, null, null, Time, Time, RecordingSinkSessionLifecycleKind.Faulted);
        var row = Row(fact);
        Assert.Equal("失敗", row.Result); Assert.Equal("—", row.Bytes);
        Assert.Contains("故障段階: 情報なし", row.Details);
        Assert.Contains("ExceptionType: 情報なし", row.Details);
        Assert.True(GenesisGuiNextRecordingHistoryProjection.Status(Snapshot(fact), null, false).IsWarning);
        Assert.Contains("耐久性情報なし", GenesisGuiNextRecordingHistoryProjection.Status(Snapshot(fact), null, false).Text);
    }

    [Fact]
    public async Task NativeProductionCompositionHistoryQueryDoesNotScanRegisterOrChangeActiveOwners()
    {
        var root = Path.GetFullPath(Path.Combine("synthetic-history-runtime", Guid.NewGuid().ToString("N")));
        var files = new PoisonLibraryFileSystem();
        var composition = new PcpRecordingRuntimeComposition(root, null, libraryFileSystem: files);
        var shared = new GenesisSharedPcpPlaybackRuntime(composition);
        var daily = new NovaDailyHttpFlvPlaybackController(new PcrPlayerExternalPlayerLauncher((string?)null),
            _ => throw new InvalidOperationException("no playback"), _ => throw new InvalidOperationException("no playback"),
            composition, shared);
        await using var adapter = new GuiNextDailyPlaybackSessionController(daily);
        composition.RecordingHistory.ObserveTerminal(Fact());
        var roots = composition.KnownRecordingRoots.GetSnapshot();
        var snapshot = daily.GetRecordingHistorySnapshot();
        var vm = new GenesisMainViewModel(adapter);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection(),
            new GenesisRecordingRuleRepository(path: null), recordingHistoryController: vm.RecordingHistoryController);
        Assert.Equal(0, files.Calls);
        await Enter(tabs); await tabs.RefreshRecordingHistoryAsync();
        Assert.Single(tabs.RecordingHistoryRows);
        Assert.Equal("未確認（Library未更新）", tabs.RecordingHistoryRows[0].Association);
        Assert.Equal(0, files.Calls);
        Assert.Equal(roots.Revision, composition.KnownRecordingRoots.GetSnapshot().Revision);
        Assert.Equal(roots.Roots, composition.KnownRecordingRoots.GetSnapshot().Roots);
        Assert.Equal(snapshot.Entries, daily.GetRecordingHistorySnapshot().Entries);
        Assert.Empty(await composition.Recording.GetActiveRecordingSessionsAsync());
        Assert.Equal(PlaybackSessionState.Stopped, adapter.Snapshot.State);
        Assert.Equal(root, composition.RecordingDirectory);
    }

    private static string Source(string filename)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "Nova.App", filename);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(filename);
    }

    private sealed class PoisonLibraryFileSystem : IGenesisRecordingLibraryFileSystem
    {
        public int Calls { get; private set; }
        public IEnumerable<GenesisRecordingLibraryDirectoryEntry> EnumerateDirectEntries(string root, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("History must not enumerate files."); }
        public GenesisRecordingLibraryFileMetadata ReadFileMetadata(string path, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("History must not probe files."); }
    }

    [Fact]
    public async Task InvalidSyntheticStoreRemainsUnmodifiedWhileLiveMemoryFactsStayVisible()
    {
        var owned = Path.Combine(Path.GetTempPath(), "Nova-History-read-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        var storePath = Path.Combine(owned, "history.json");
        try
        {
            await File.WriteAllTextAsync(storePath, "invalid original - MUST NOT REPAIR");
            var original = await File.ReadAllBytesAsync(storePath);
            var files = new PoisonLibraryFileSystem();
            var composition = new PcpRecordingRuntimeComposition(Path.Combine(owned, "not-created-recordings"),
                new GenesisRecordingHistoryStore(storePath), libraryFileSystem: files);
            var shared = new GenesisSharedPcpPlaybackRuntime(composition);
            var daily = new NovaDailyHttpFlvPlaybackController(new PcrPlayerExternalPlayerLauncher((string?)null),
                _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), composition, shared);
            await using (var adapter = new GuiNextDailyPlaybackSessionController(daily))
            {
                composition.RecordingHistory.ObserveTerminal(Fact());
                var roots = composition.KnownRecordingRoots.GetSnapshot();
                var vm = new GenesisMainViewModel(adapter);
                await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection(),
                    new GenesisRecordingRuleRepository(path: null), recordingHistoryController: vm.RecordingHistoryController);
                await Enter(tabs);
                await tabs.RefreshRecordingHistoryAsync();
                Assert.Single(tabs.RecordingHistoryRows);
                Assert.Contains("Invalid", tabs.RecordingHistoryStatus.Text);
                Assert.Contains("自動保存無効", tabs.RecordingHistoryDetails(tabs.RecordingHistoryRows[0]));
                Assert.Equal(roots.Revision, composition.KnownRecordingRoots.GetSnapshot().Revision);
                Assert.Equal(roots.Roots, composition.KnownRecordingRoots.GetSnapshot().Roots);
                Assert.Equal(0, files.Calls);
                Assert.Equal(original, await File.ReadAllBytesAsync(storePath));
                Assert.False(Directory.Exists(composition.RecordingDirectory));
                Assert.Single(Directory.GetFiles(owned));
            }
            Assert.Equal(original, await File.ReadAllBytesAsync(storePath));
            Assert.Single(Directory.GetFiles(owned));
        }
        finally { File.Delete(storePath); Directory.Delete(owned); }
    }
    private static async Task Enter(GenesisGuiNextTabsViewModel tabs)
    { tabs.SelectedTab = GenesisGuiNextTab.Recording; await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.History); }
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static GenesisRecordingHistoryDurabilitySnapshot Healthy() =>
        new(GenesisRecordingHistoryLoadStatus.Available, 1, 1, GenesisRecordingHistoryWriteStatus.Saved, GenesisRecordingHistoryStoreFailure.None, true);
    private static GenesisRecordingHistorySnapshot Snapshot(params RecordingSessionTerminalFact[] facts) => new(1, Array.AsReadOnly(facts), 0, 0);
    private static GenesisRecordingLibrarySnapshot InitialLibrary() => new(0, GenesisRecordingLibraryObservationState.NotObservedYet, [], [], 0, false, 0, false);
    private static RecordingSessionTerminalFact Fact(Guid? id = null, bool automatic = false, string? name = "歴史的なチャンネル",
        long bytes = 123, RecordingSinkSessionLifecycleKind kind = RecordingSinkSessionLifecycleKind.Completed,
        RecordingFileWriteCompletionKind? completion = RecordingFileWriteCompletionKind.EndOfStream, string? path = null) =>
        new(automatic ? new(new(id ?? Session), new(Channel), Rule) : RecordingSinkSession.Manual(new(id ?? Session), new(Channel)),
            name, null, null, Time.AddHours(1), Time, kind,
            completion is null ? null : RecordingFileWriteResult.FromFinalizedFacts(path ?? PathValue, bytes, completion.Value),
            kind == RecordingSinkSessionLifecycleKind.Faulted ? RecordingFaultDescriptor.FromSafeDetails(RecordingFailureStage.FileFinalize, "System.IO.IOException") : null);
    private static GenesisRecordingLibrarySnapshot Library(RecordingSessionTerminalFact fact,
        GenesisRecordingLibraryAvailability availability = GenesisRecordingLibraryAvailability.Available) =>
        new(3, GenesisRecordingLibraryObservationState.Observed,
            [new(PathValue, availability, GenesisRecordingLibraryFailure.None, GenesisRecordingLibraryContainer.FlvCandidate,
                new(FileAttributes.Normal, 999, Time, Time), null, [fact], null)], [], 1, false, 0, false);
    private static GenesisGuiNextRecordingHistoryRow Row(RecordingSessionTerminalFact fact) =>
        GenesisGuiNextRecordingHistoryProjection.CreateRows(Snapshot(fact), InitialLibrary()).Single();
    private static GenesisGuiNextNativeTabRow Native(RecordingSessionTerminalFact fact) =>
        new(new(string.Empty, string.Empty), null, RecordingHistory: Row(fact));

    private sealed class Queries : IGenesisRecordingHistoryController, IGenesisRecordingLibraryController, IAsyncDisposable
    {
        private int _active;
        public GenesisRecordingHistorySnapshot History { get; set; } = Snapshot();
        public GenesisRecordingHistoryDurabilitySnapshot? Durability { get; set; } = Healthy();
        public GenesisRecordingLibrarySnapshot LibrarySnapshot { get; set; } = InitialLibrary();
        public Func<GenesisRecordingHistorySnapshot>? Handler { get; set; }
        public int HistoryCalls { get; private set; }
        public int DurabilityCalls { get; private set; }
        public int LibraryCalls { get; private set; }
        public int MaxConcurrent { get; private set; }
        public int Scans { get; private set; }
        public int OwnerDisposals { get; private set; }
        public ValueTask DisposeAsync() { OwnerDisposals++; return ValueTask.CompletedTask; }
        public (int, int, int) Counts => (HistoryCalls, DurabilityCalls, LibraryCalls);
        public GenesisRecordingHistorySnapshot GetRecordingHistorySnapshot()
        {
            HistoryCalls++; MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _active));
            try { return Handler?.Invoke() ?? History; }
            finally { Interlocked.Decrement(ref _active); }
        }
        public GenesisRecordingHistoryDurabilitySnapshot? GetRecordingHistoryDurabilitySnapshot() { DurabilityCalls++; return Durability; }
        public GenesisRecordingLibrarySnapshot GetRecordingLibrarySnapshot() { LibraryCalls++; return LibrarySnapshot; }
        public GenesisRecordingLibrarySnapshot GetSnapshot() => GetRecordingLibrarySnapshot();
        public Task<GenesisRecordingLibraryRefreshResult> RefreshAsync(CancellationToken cancellationToken)
        { Scans++; throw new InvalidOperationException("History must never initiate a filesystem observation."); }
    }
    private sealed class QueryDaily : INovaDailyPlaybackController, IGenesisRecordingHistoryController
    {
        public Queries Queries { get; } = new();
        public int Commands { get; private set; }
        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded { add { } remove { } }
        public Task PlayAsync(GenesisChannelItem channel, CancellationToken cancellationToken) { Commands++; throw new InvalidOperationException(); }
        public Task StopAsync() { Commands++; throw new InvalidOperationException(); }
        public GenesisRecordingHistorySnapshot GetRecordingHistorySnapshot() => Queries.GetRecordingHistorySnapshot();
        public GenesisRecordingHistoryDurabilitySnapshot? GetRecordingHistoryDurabilitySnapshot() => Queries.GetRecordingHistoryDurabilitySnapshot();
        public GenesisRecordingLibrarySnapshot GetRecordingLibrarySnapshot() => Queries.GetRecordingLibrarySnapshot();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class PoisonCatalog : IGenesisChannelCatalog
    {
        public int Calls { get; private set; }
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("History refresh must not read the catalog."); }
    }
}
