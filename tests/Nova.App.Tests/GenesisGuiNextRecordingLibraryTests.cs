using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRecordingLibraryTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 7, 1, 2, 3, TimeSpan.Zero);
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Nova-library-projection-fictional"));
    private static readonly Guid Channel = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Session = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid Rule = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task ObservedCountStatusFeedsNativeDrawWithoutSelectionOrWordingChange()
    {
        var snapshot = Snapshot(Enumerable.Range(0, 17).Select(i => Entry($"observed-{i}.flv")).ToArray());
        await using var tabs = Tabs(new FakeLibrary(snapshot));
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.Equal("録画ファイルを 17 件表示しています", tabs.RecordingLibraryStatusText);
        Assert.Equal(tabs.RecordingLibraryStatusText,
            GenesisGuiNextAppShellBridge.RecordingStatusText(true, tabs.RecordingLibraryStatusText, "active"));
    }

    [Fact]
    public async Task ModesInitialSnapshotAndConstructionAreInert()
    {
        var library = new FakeLibrary();
        await using var tabs = Tabs(library);
        Assert.Equal(new[] { GenesisGuiNextRecordingPageMode.ActiveRecordings, GenesisGuiNextRecordingPageMode.Library,
            GenesisGuiNextRecordingPageMode.Rules, GenesisGuiNextRecordingPageMode.History }, GenesisGuiNextRecordingPageModePresentation.Items.Select(x => x.Mode));
        Assert.Equal(["録画中", "ライブラリ", "ルール", "履歴"], GenesisGuiNextRecordingPageModePresentation.Items.Select(x => x.Label));
        Assert.Equal(4, Enum.GetValues<GenesisGuiNextRecordingPageMode>().Length);
        Assert.Equal(GenesisGuiNextRecordingPageMode.ActiveRecordings, tabs.RecordingPageMode);
        Assert.Equal(GenesisRecordingLibraryObservationState.NotObservedYet, tabs.RecordingLibrarySnapshot.State);
        Assert.Equal(GenesisGuiNextRecordingLibraryProjection.NotObservedText, tabs.RecordingLibraryStatusText);
        _ = GenesisGuiNextNativeTabProjection.Create(tabs);
        _ = GenesisGuiNextRecordingListPresentation.Columns(tabs.SelectedTab, tabs.RecordingPageMode);
        Assert.Equal(0, library.Calls);
        Assert.False(tabs.CanRefreshRecordingLibrary);
    }

    [Fact]
    public async Task FirstEntrySetsBusyImmediatelyThenAtomicEmptyAndBackDoesNotRescan()
    {
        var entered = Gate(); var release = Gate();
        var library = new FakeLibrary
        {
            Handler = async _ =>
            {
                entered.SetResult(); await release.Task;
                return new(GenesisRecordingLibraryRefreshStatus.Published, Snapshot());
            }
        };
        await using var tabs = Tabs(library);
        var task = tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.True(tabs.IsRecordingLibraryRefreshing);
        Assert.StartsWith(GenesisGuiNextRecordingLibraryProjection.RefreshingText, tabs.RecordingLibraryStatusText);
        Assert.False(tabs.CanRefreshRecordingLibrary);
        await entered.Task;
        await tabs.RefreshRecordingLibraryAsync();
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.Equal(1, library.Calls);
        release.SetResult(); await task;
        Assert.Empty(tabs.RecordingLibraryRows);
        Assert.Equal("録画ファイルは見つかりませんでした", tabs.RecordingLibraryStatusText);
        Assert.True(tabs.CanRefreshRecordingLibrary);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        Assert.False(tabs.CanRefreshRecordingLibrary);
        tabs.SelectedTab = GenesisGuiNextTab.Recording;
        Assert.True(tabs.CanRefreshRecordingLibrary);
        Assert.Equal(1, library.Calls);
    }

    [Fact]
    public async Task ManualRefreshRetainsOldRowsRejectsOverlapAndPublishesOnce()
    {
        var first = Snapshot(Entry("first.flv"));
        var next = Snapshot(Entry("second.bin")) with { Revision = 2 };
        var entered = Gate(); var release = Gate();
        var library = new FakeLibrary(first)
        {
            Handler = async _ =>
            {
                entered.SetResult(); await release.Task;
                return new(GenesisRecordingLibraryRefreshStatus.Published, next);
            }
        };
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.Equal(0, library.Calls);
        var before = tabs.RecordingLibraryRows;
        var notices = new List<string?>();
        tabs.PropertyChanged += (_, e) => notices.Add(e.PropertyName);
        var task = tabs.RefreshRecordingLibraryAsync();
        await entered.Task;
        Assert.Same(first, tabs.RecordingLibrarySnapshot); Assert.Same(before, tabs.RecordingLibraryRows);
        Assert.False(tabs.CanRefreshRecordingLibrary);
        await tabs.RefreshRecordingLibraryAsync();
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.Equal(1, library.Calls); Assert.False(library.Token.IsCancellationRequested);
        release.SetResult(); await task;
        Assert.Same(next, tabs.RecordingLibrarySnapshot); Assert.NotSame(before, tabs.RecordingLibraryRows);
        Assert.Equal(next.Entries[0].Path, Assert.Single(tabs.RecordingLibraryRows).Path);
        Assert.Equal(1, notices.Count(x => x == nameof(tabs.RecordingLibraryRows)));
        Assert.True(tabs.CanRefreshRecordingLibrary);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationKeepsInitialOrPreviouslyObservedSnapshot(bool observed, bool throws)
    {
        var before = observed ? Snapshot(Entry("keep.flv")) : Initial();
        var entered = Gate(); var release = Gate();
        var library = new FakeLibrary(before)
        {
            Handler = async token =>
        {
            entered.SetResult(); await release.Task;
            if (throws) throw new OperationCanceledException(token);
            return new(GenesisRecordingLibraryRefreshStatus.Cancelled, Snapshot());
        }
        };
        await using var tabs = Tabs(library);
        if (observed) await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        using var cancellation = new CancellationTokenSource();
        var task = observed ? tabs.RefreshRecordingLibraryAsync(cancellation.Token)
            : tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        await entered.Task; cancellation.Cancel(); release.SetResult(); await task;
        Assert.Same(before, tabs.RecordingLibrarySnapshot);
        Assert.Equal(observed ? 1 : 0, tabs.RecordingLibraryRows.Count);
        Assert.DoesNotContain("更新できません", tabs.RecordingLibraryStatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedFailureIsGenericAndKeepsRows(bool observed)
    {
        var before = observed ? Snapshot(Entry("keep.flv")) : Initial();
        var library = new FakeLibrary(before) { Handler = _ => throw new InvalidOperationException("secret root credential") };
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        if (observed) await tabs.RefreshRecordingLibraryAsync();
        Assert.Same(before, tabs.RecordingLibrarySnapshot);
        Assert.Contains(GenesisGuiNextRecordingLibraryProjection.RefreshFailedText, tabs.RecordingLibraryStatusText);
        Assert.DoesNotContain("secret", tabs.RecordingLibraryStatusText);
        Assert.Equal(observed ? 1 : 0, tabs.RecordingLibraryRows.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeCancelsDrainsAndRejectsLatePublicationAndCallbacks(bool ignoresCancellation)
    {
        var entered = Gate(); var cancelled = Gate(); var release = Gate();
        var library = new FakeLibrary
        {
            Handler = async token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.SetResult(); await release.Task;
            if (!ignoresCancellation) token.ThrowIfCancellationRequested();
            return new(GenesisRecordingLibraryRefreshStatus.Published, Snapshot(Entry("late.flv")));
        }
        };
        var tabs = Tabs(library);
        var before = tabs.RecordingLibrarySnapshot;
        var task = tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        await entered.Task;
        var callbacks = 0;
        tabs.PropertyChanged += (_, _) => callbacks++;
        var shutdown = tabs.DisposeAsync().AsTask();
        await cancelled.Task; Assert.False(shutdown.IsCompleted);
        release.SetResult(); await task; await shutdown;
        Assert.Same(before, tabs.RecordingLibrarySnapshot); Assert.Empty(tabs.RecordingLibraryRows);
        Assert.Equal(0, callbacks); Assert.False(tabs.CanRefreshRecordingLibrary);
        await tabs.RefreshRecordingLibraryAsync();
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        await tabs.DisposeAsync();
        Assert.Equal(1, library.Calls); Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task UnchangedResultKeepsNativeRowIdentityAndDoesNotRaiseRowsChanged()
    {
        var snapshot = Snapshot(Entry("keep.flv"));
        var library = new FakeLibrary(snapshot);
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        var before = tabs.RecordingLibraryRows;
        var identity = GenesisGuiNextNativeRowRebuildIdentity.Create(before, tabs.SelectedTab, tabs.RecordingPageMode, new object());
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await tabs.RefreshRecordingLibraryAsync();
        Assert.Same(before, tabs.RecordingLibraryRows);
        Assert.DoesNotContain(nameof(tabs.RecordingLibraryRows), changed);
        Assert.False(GenesisGuiNextNativeRowRebuildIdentity.Create(tabs.RecordingLibraryRows,
            tabs.SelectedTab, tabs.RecordingPageMode, new object()).RequiresRebuild(identity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RefreshUnavailableOutsideLibraryAndForNoController(int tabValue)
    {
        var library = new FakeLibrary(Snapshot());
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        tabs.SelectedTab = (GenesisGuiNextTab)tabValue;
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.False(tabs.CanRefreshRecordingLibrary); await tabs.RefreshRecordingLibraryAsync();
        Assert.Equal(0, library.Calls);
        await using var unsupported = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        unsupported.SelectedTab = GenesisGuiNextTab.Recording;
        await unsupported.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.False(unsupported.CanRefreshRecordingLibrary);
        Assert.Equal(GenesisRecordingLibraryObservationState.NotObservedYet, unsupported.RecordingLibrarySnapshot.State);
    }

    [Theory]
    [InlineData("flv", "FLV候補")]
    [InlineData("bin", "不明（.bin）")]
    [InlineData("other", "未対応形式")]
    public void LegacyRowsAreTruthfulAndCultureIndependent(string kind, string label)
    {
        var entry = Entry("malformed." + kind) with
        {
            Container = kind switch
            {
                "bin" => GenesisRecordingLibraryContainer.UnknownContainerCandidate,
                "flv" => GenesisRecordingLibraryContainer.FlvCandidate,
                _ => GenesisRecordingLibraryContainer.UnsupportedExtension,
            },
            FileMetadata = new(FileAttributes.Normal, 9219498, Time, Time.AddDays(-1))
        };
        var row = GenesisGuiNextRecordingLibraryProjection.CreateRow(entry);
        Assert.Equal("利用可能", row.Availability); Assert.Equal(label, row.Container);
        Assert.Equal("履歴なし", row.Relation); Assert.Equal("不明", row.Channel);
        Assert.Equal("malformed." + kind, row.Details.Title); Assert.Equal("9,219,498 B", row.Size);
        Assert.Equal("2026-10-07 01:02:03 UTC", row.Modified);
        Assert.Empty(row.Details.HistoryFacts); Assert.Equal(entry.Path, row.Path);
        Assert.DoesNotContain("由来", row.Details.Metadata); Assert.DoesNotContain("Kind", row.Details.Metadata);
        Assert.DoesNotContain("正常", row.Container); Assert.DoesNotContain("再生可能", row.Container);
        Assert.Equal(7, GenesisRecordingLibraryListPresentation.CreateRowValues(row).Length);
        Assert.Equal(entry.Filename, GenesisRecordingLibraryListPresentation.CreateRowValues(row)[2]);
        Assert.DoesNotContain(Root, GenesisRecordingLibraryListPresentation.CreateRowValues(row)[2]);
    }

    [Fact]
    public void FilenameClaimsStayVisiblyUnverifiedAndCannotBecomeHistory()
    {
        var claims = new GenesisRecordingLibraryFilenameClaims(Time, "encoded_name", Channel, Session);
        var row = GenesisGuiNextRecordingLibraryProjection.CreateRow(Entry("legacy.flv") with { FilenameClaims = claims });
        Assert.Equal("encoded_name（ファイル名）", row.Channel);
        Assert.Contains("未確認情報", row.Details.Identity); Assert.Contains(Channel.ToString("N"), row.Details.Identity);
        Assert.Contains(Session.ToString("N"), row.Details.Identity);
        Assert.Equal("履歴なし", row.Relation); Assert.Empty(row.Details.HistoryFacts);
        Assert.DoesNotContain("RuleId", row.Details.Identity); Assert.DoesNotContain("由来", row.Details.Metadata);
        Assert.DoesNotContain("terminal", row.Details.Metadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryLinkedDetailsKeepActualFactsSeparateFromCurrentSize(bool automatic)
    {
        var entry = Entry("linked.flv");
        var fact = Fact(entry.Path, automatic);
        entry = entry with
        {
            HistoryFacts = Array.AsReadOnly(new[] { fact }),
            FileMetadata = new(FileAttributes.Normal, 456, Time, Time)
        };
        var row = GenesisGuiNextRecordingLibraryProjection.CreateRow(entry);
        Assert.Equal("Original historical name", row.Channel); Assert.Equal("履歴あり", row.Relation);
        Assert.Equal("456 B", row.Size); Assert.Contains("録画時の記録サイズ: 123 B", row.Details.Metadata);
        Assert.Contains(GenesisGuiNextRecordingLibraryProjection.LengthMismatchText, row.Details.Description);
        Assert.Contains(automatic ? "由来（履歴）: 自動" : "由来（履歴）: 手動", row.Details.Metadata);
        Assert.Contains(Session.ToString("N"), row.Details.Identity);
        Assert.Contains(Channel.ToString("N"), row.Details.Identity);
        Assert.Equal(automatic, row.Details.Identity.Contains(Rule.ToString("D"), StringComparison.Ordinal));
        Assert.Contains("Completed", row.Details.Metadata); Assert.Contains("Stopped", row.Details.Metadata);
        Assert.Same(fact, Assert.Single(row.Details.HistoryFacts));
        Assert.Equal(123, fact.WriteResult!.BytesWritten); Assert.Equal(456, entry.CurrentFileLength);
        Assert.DoesNotContain("破損", row.Details.Description); Assert.DoesNotContain("録画失敗", row.Details.Description);
    }

    [Theory]
    [InlineData("Missing", "見つかりません")]
    [InlineData("Unavailable", "確認できません")]
    public void MissingAndUnavailableHistoryRemainRowsWithNoCurrentMetadata(string state, string label)
    {
        var entry = Entry("linked.flv");
        entry = entry with
        {
            Availability = Enum.Parse<GenesisRecordingLibraryAvailability>(state),
            Failure = GenesisRecordingLibraryFailure.AccessDenied,
            HistoryFacts = [Fact(entry.Path)]
        };
        var row = Assert.Single(GenesisGuiNextRecordingLibraryProjection.CreateRows(Snapshot(entry)));
        Assert.Equal(label, row.Availability); Assert.Equal("—", row.Size); Assert.Equal("—", row.Modified);
        Assert.Contains("Completed", row.Details.Metadata); Assert.Contains("123 B", row.Details.Metadata);
        Assert.DoesNotContain("削除", row.Details.Description);
        Assert.DoesNotContain("AccessDenied", row.Details.Description);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleFactsNeverChooseAWinningIdentityOrConflictingValue(bool sharedName)
    {
        var entry = Entry("ambiguous.flv");
        var first = Fact(entry.Path);
        var second = Fact(entry.Path, true, Guid.NewGuid(), sharedName ? first.ChannelName : "Other historical name");
        entry = entry with { HistoryFacts = Array.AsReadOnly(new[] { first, second }) };
        var row = GenesisGuiNextRecordingLibraryProjection.CreateRow(entry);
        Assert.Equal(sharedName ? first.ChannelName : "複数の履歴", row.Channel);
        Assert.Equal("履歴あり", row.Relation); Assert.Same(entry.HistoryFacts, row.Details.HistoryFacts);
        Assert.Contains(GenesisGuiNextRecordingLibraryProjection.MultipleHistoryText, row.Details.Description);
        Assert.DoesNotContain("SessionId（履歴）", row.Details.Identity);
        Assert.DoesNotContain("RuleId（履歴）", row.Details.Identity);
        Assert.DoesNotContain("由来（履歴）", row.Details.Metadata);
        Assert.Contains(Channel.ToString("N"), row.Details.Identity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyHistoricalNameUsesOnlyMarkedFilenameClaimOrFilename(string? historicalName)
    {
        var entry = Entry("fallback.flv");
        entry = entry with { HistoryFacts = [Fact(entry.Path, name: historicalName)] };
        var row = GenesisGuiNextRecordingLibraryProjection.CreateRow(entry);
        Assert.Equal("不明", row.Channel); Assert.Equal("fallback.flv", row.Details.Title);
        entry = entry with { FilenameClaims = new(Time, "claim", Channel, Session) };
        Assert.Equal("claim（ファイル名）", GenesisGuiNextRecordingLibraryProjection.CreateRow(entry).Channel);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(16, 1)]
    [InlineData(32, 1)]
    [InlineData(64, 1)]
    [InlineData(127, 1)]
    [InlineData(127, 0)]
    public void CoverageWarningsComposeWithoutLosingEntryCountOrLeakingRoot(int flags, int count)
    {
        var root = new GenesisRecordingLibraryRootObservation("sensitive-root", GenesisRecordingRootProvenance.Explicit,
            (flags & 1) != 0 ? GenesisRecordingLibraryAvailability.Unavailable : GenesisRecordingLibraryAvailability.Available,
            GenesisRecordingLibraryFailure.Io, 1, 1, count, (flags & 2) != 0, (flags & 32) != 0 ? 1 : 0);
        var entry = Entry("shown.flv") with
        {
            Availability = (flags & 64) != 0
            ? GenesisRecordingLibraryAvailability.Unavailable : GenesisRecordingLibraryAvailability.Available
        };
        var snapshot = Snapshot(count == 0 ? [] : [entry]) with
        {
            Roots = [root],
            IsSnapshotTruncated = (flags & 4) != 0,
            ExcludedHistoryPathCount = (flags & 8) != 0 ? 1 : 0,
            HistoryHasGap = (flags & 16) != 0
        };
        var text = GenesisGuiNextRecordingLibraryProjection.StatusText(snapshot);
        Assert.Equal(text, GenesisGuiNextAppShellBridge.RecordingStatusText(true, text, "active"));
        Assert.Contains(count == 0 ? "録画ファイルは見つかりませんでした" : "録画ファイルを 1 件表示しています", text);
        Assert.Equal((flags & (1 | 32 | 64)) != 0, text.Contains("一部の保存先またはファイルを確認できません", StringComparison.Ordinal));
        Assert.Equal((flags & (2 | 4)) != 0, text.Contains("一部のみ表示しています", StringComparison.Ordinal));
        Assert.Equal((flags & 8) != 0, text.Contains("一部の履歴パスはライブラリ対象外です", StringComparison.Ordinal));
        Assert.Equal((flags & 16) != 0, text.Contains("録画履歴に欠落があります", StringComparison.Ordinal));
        Assert.DoesNotContain("sensitive-root", text); Assert.DoesNotContain("Io", text);
    }

    [Fact]
    public async Task LibraryRowsAreTypedAndDenyAllLiveRuleAndContextActions()
    {
        var library = new FakeLibrary(Snapshot(Entry("readonly.flv")));
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.NotNull(row.RecordingLibrary); Assert.False(row.IsInteractive); Assert.Null(row.InteractiveChannel);
        Assert.Null(row.ActiveRecording); Assert.Null(row.RecordingRule); Assert.Null(row.Suppression);
        Assert.Equal(string.Empty, row.DisplayChannel.Source);
        var vm = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(vm);
        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(vm, tabs.SelectedTab, row).IsPlayEnabled);
        Assert.False(await GenesisGuiNextPlaybackCommandDispatch.ActivateSelectedChannelAsync(vm, interaction, tabs.SelectedTab, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(vm, tabs.SelectedTab, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopSelectedAsync(vm, tabs.SelectedTab, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(vm, row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(vm, row));
        Assert.False(GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(tabs, row).IsEnabled);
        Assert.Equal(GenesisGuiNextFavoriteToggleResult.Unavailable, GenesisGuiNextFavoriteToggle.Apply(tabs, row));
        Assert.Null(tabs.CreateRecordingRule()); Assert.Null(tabs.EditRecordingRule(Rule));
        Assert.False(tabs.CanQuickMutateRecordingRule(Rule));
        Assert.False(GenesisChannelDirectContextMenuDispatch.TryOpen(row, _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException()));
        Assert.Null(GenesisChannelContextMenuKeyboard.ResolveRenderedTarget((nint)1, (_, _) => 0, (_, _) => row));
        Assert.Null(GenesisChannelDirectContextMenuRowResolver.Resolve(0, row, vm, tabs));
        Assert.Null(GenesisChannelContextMenuFavoritePresentation.Resolve(tabs, row));
        Assert.Empty(tabs.FavoriteChannels);
    }

    [Theory]
    [InlineData("identical")]
    [InlineData("reorder")]
    [InlineData("removed")]
    [InlineData("sameFilenameElsewhere")]
    [InlineData("case")]
    public void SelectionUsesPathIdentityAndClearsRatherThanStealingRow(string kind)
    {
        var list = (nint)(0x71442000 + kind.Length);
        var original = Native(Entry("selected.flv"));
        var other = Native(Entry("other.flv"));
        var selected = kind switch
        {
            "sameFilenameElsewhere" => Native(Entry("selected.flv") with { Path = Path.Combine(Root, "other", "selected.flv") }),
            "case" => Native(original.RecordingLibrary!.Entry with { Path = original.RecordingLibrary.Path.ToUpperInvariant() }),
            _ => Native(original.RecordingLibrary!.Entry with { FileMetadata = new(FileAttributes.Normal, 456, Time, Time) }),
        };
        IReadOnlyList<GenesisGuiNextNativeTabRow> next = kind switch
        {
            "removed" => [other],
            "reorder" => [other, selected],
            _ => [selected, other],
        };
        try
        {
            GenesisChannelListRenderedChannels.SetRows(list, [original, other], (_, _) => true);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(list, 0);
            var restored = -1;
            GenesisChannelListRenderedChannels.SetRows(list, next, (_, index) => { restored = index; return true; });
            var expected = kind is "removed" or "sameFilenameElsewhere" || kind == "case" && !OperatingSystem.IsWindows()
                ? -1 : kind == "reorder" ? 1 : 0;
            Assert.Equal(expected, restored);
            Assert.Equal(expected, GenesisChannelListRenderedChannels.FindAnchoredRow(list, next));
            if (expected < 0) Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(list, [original]));
        }
        finally { GenesisChannelListRenderedChannels.Remove(list); }
    }

    [Fact]
    public void ModeSpecificColumnsAndValuesRemainSeparatedAndRestoreChannelSchema()
    {
        var active = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, GenesisGuiNextRecordingPageMode.ActiveRecordings);
        var library = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, GenesisGuiNextRecordingPageMode.Library);
        var rules = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, GenesisGuiNextRecordingPageMode.Rules);
        Assert.Equal(GenesisChannelListPresentation.Columns.Select(x => x.Header), active.Select(x => x.Header));
        Assert.Same(GenesisRecordingLibraryListPresentation.Columns, library);
        Assert.Same(GenesisRecordingRulesListPresentation.Columns, rules);
        Assert.Equal(["状態", "チャンネル", "ファイル", "種類", "サイズ", "更新日時", "関連"], library.Select(x => x.Header));
        Assert.Equal(["状態", "ルール名", "動作", "優先度", "保存順", "条件（すべて一致）"], rules.Select(x => x.Header));
        Assert.All(library, column => Assert.InRange(column.PreferredWidth, 64, 300));
        var row = Native(Entry("candidate.flv"));
        Assert.Equal(GenesisRecordingLibraryListPresentation.CreateRowValues(row.RecordingLibrary!),
            GenesisGuiNextRecordingListPresentation.Values(row));
        Assert.Same(active, GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Channels, GenesisGuiNextRecordingPageMode.Library));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ColumnFailureIsInertAndCannotApplyRows(bool clearSuccess, bool columnSuccess)
    {
        var operations = new List<string>();
        var success = GenesisGuiNextRecordingListPresentation.TryReplaceColumns(
            () => { operations.Add("clear-rows"); return clearSuccess; },
            () => { operations.Add("replace-columns"); return columnSuccess; },
            () => operations.Add("clear-partial-columns"));
        Assert.Equal(clearSuccess && columnSuccess, success);
        string[] expected = clearSuccess ? columnSuccess ? ["clear-rows", "replace-columns"]
            : ["clear-rows", "replace-columns", "clear-partial-columns"] : ["clear-rows"];
        Assert.Equal(expected, operations);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public void ModeTransitionReplacesEveryColumnBeforeAddingTypedRows(int fromValue, int toValue)
    {
        var from = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, (GenesisGuiNextRecordingPageMode)fromValue);
        var to = GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Recording, (GenesisGuiNextRecordingPageMode)toValue);
        var operations = new List<string>();
        Assert.True(GenesisGuiNextRecordingListPresentation.TryReplaceColumns(
            () => GenesisMainWindowNativeSurface.ReplaceRows((nint)1, Array.Empty<string[]>(),
                _ => { operations.Add("clear-rows"); return true; }, (_, _) => true),
            () => GenesisMainWindowNativeSurface.ReplaceColumns((nint)1, from.Count, to,
                (_, index) => { operations.Add("delete:" + index); return true; },
                (_, columns) => { Assert.Same(to, columns); operations.Add("add-columns"); return true; }),
            () => throw new InvalidOperationException()));
        Assert.Equal("clear-rows", operations[0]);
        Assert.Equal(Enumerable.Range(0, from.Count).Reverse().Select(x => "delete:" + x),
            operations.Skip(1).Take(from.Count));
        Assert.Equal("add-columns", operations[^1]);
    }

    [Fact]
    public void FailedTypedRowsAreClearedAndNeverPublished()
    {
        var rows = new[] { GenesisGuiNextRecordingListPresentation.Values(Native(Entry("example.flv"))) };
        var clears = 0;
        Assert.False(GenesisMainWindowNativeSurface.ReplaceRows((nint)1, rows,
            _ => { clears++; return true; }, (_, _) => false));
        Assert.Equal(2, clears);
    }

    [Fact]
    public async Task AppControllerForwardsToSameDailyBoundaryWithoutStartupEffects()
    {
        var library = new FakeLibrary(Snapshot(Entry("forward.flv")));
        var daily = new FakeDaily(library);
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var vm = new GenesisMainViewModel(controller);
        Assert.Same(controller, vm.RecordingLibraryController);
        var contract = Assert.IsAssignableFrom<IGenesisRecordingLibraryController>(vm.RecordingLibraryController);
        Assert.Same(library.Current, contract.GetSnapshot()); Assert.Equal(0, library.Calls);
        using var cancellation = new CancellationTokenSource();
        var result = await contract.RefreshAsync(cancellation.Token);
        Assert.Same(library.Current, result.Snapshot); Assert.Equal(1, library.Calls);
        Assert.Equal(cancellation.Token, library.Token);
        Assert.Equal(0, daily.PlayCalls); Assert.Equal(0, daily.StopCalls);
    }

    [Fact]
    public async Task CatalogCompositionAndAllOtherProjectionEventsNeverScanLibrary()
    {
        var library = new FakeLibrary(Snapshot(Entry("z.flv"), Entry("a.bin")));
        var daily = new FakeDaily(library);
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var vm = new GenesisMainViewModel(controller);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        await using var catalog = GenesisChannelCatalogSession.Create(new EmptyCatalog(), vm,
            new GenesisGuiNextChannelPresencePublisher(tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance));
        Assert.Same(library.Current, tabs.RecordingLibrarySnapshot);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library); // wrong primary page, no scan
        await catalog.RefreshAsync();
        tabs.SearchText = "unrelated"; tabs.SelectedYellowPageId = "absent";
        tabs.UpdateActiveRecordings([]); tabs.UpdateSuppression([]);
        tabs.UpdatePlayback(PlaybackSessionSnapshot.Stopped);
        for (var i = 0; i < 3; i++) { _ = tabs.RecordingLibraryStatusText; _ = GenesisGuiNextNativeTabProjection.Create(tabs); }
        Assert.Equal(0, library.Calls);
        Assert.Equal(library.Current.Entries.Select(x => x.Path), tabs.RecordingLibraryRows.Select(x => x.Path));
        Assert.Equal(0, daily.PlayCalls); Assert.Equal(0, daily.StopCalls);
    }

    [Fact]
    public async Task ActiveLibraryRulesActiveKeepsExistingDataAndRuleAvailability()
    {
        var library = new FakeLibrary(Snapshot(Entry("read.flv")));
        await using var repository = new GenesisRecordingRuleRepository(path: null);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection(), repository, library)
        { SelectedTab = GenesisGuiNextTab.Recording };
        var manual = new GenesisGuiNextActiveRecording(new(Channel), new(Session), RecordingSessionOrigin.Manual,
            "Manual", "録画中", null, null);
        var automatic = manual with { Origin = RecordingSessionOrigin.AutomaticRule, AutomaticRuleId = Rule, ChannelName = "Automatic" };
        var suppression = new GenesisAutomaticRecordingSuppressionRow(new(Channel), Rule,
            GenesisAutomaticRecordingSuppressionState.Committed, "抑止中");
        tabs.UpdateActiveRecordings([manual, automatic]); tabs.UpdateSuppression([suppression]);
        var activeRows = tabs.ActiveRecordings; var suppressed = tabs.SuppressionRows;
        var before = GenesisGuiNextNativeTabProjection.Create(tabs);
        Assert.Equal(3, before.Count); Assert.Same(manual, before[0].ActiveRecording);
        Assert.Same(automatic, before[1].ActiveRecording); Assert.Same(suppression, before[2].Suppression);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Assert.False(tabs.CanCreateRecordingRule);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        Assert.DoesNotContain(tabs.RecordingLibraryRows, x => x.Path == string.Empty);
        Assert.False(tabs.CanRefreshRecordingLibrary); Assert.True(tabs.CanReloadRecordingRules);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Unavailable, tabs.RecordingRulesStatus);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.Same(activeRows, tabs.ActiveRecordings); Assert.Same(suppressed, tabs.SuppressionRows);
        Assert.Equal(before, GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(0, library.Calls);
    }

    [Fact]
    public void NativeSourceWiresLibraryControlsRowsStatusAndDetachedNotificationGuards()
    {
        var shell = Source("GenesisGuiNextAppShellBridge.cs");
        var native = Source("SystemGenesisMainWindowInteractionBridgeFactory.cs");
        var lifecycle = Source("GenesisGuiNextTabsViewModel.RecordingLibrary.cs");
        Assert.Contains("GenesisGuiNextRecordingPageModePresentation.LibraryLabel", shell);
        Assert.Contains("detailsPane, RecordingLibraryModeControlId", shell);
        Assert.Contains("detailsPane, RecordingLibraryRefreshControlId", shell);
        Assert.Contains("ButtonStyleOwnerDraw", shell);
        Assert.Contains("isLibraryPage ? ShowWindowShowNoActivate : ShowWindowHide", shell);
        Assert.Contains("hideRecordingActions ? ShowWindowHide", shell);
        Assert.Contains("_tabs.CanRefreshRecordingLibrary", shell);
        Assert.Contains("_tabs.RecordingLibraryStatusText", shell);
        Assert.Contains("libraryDetails?.Identity", shell);
        Assert.Contains("_tabs.PropertyChanged -= OnTabsPropertyChanged", shell);
        Assert.Contains("Volatile.Read(ref _detached)", shell);
        Assert.Contains("nameof(GenesisGuiNextTabsViewModel.RecordingLibraryRows)", native);
        Assert.Contains("_guiNextTabs.RecordingLibraryRows", native);
        Assert.Contains("GenesisGuiNextRecordingListPresentation.Values", native);
        Assert.Contains("_renderedColumns = null", native);
        Assert.Contains("if (_disposed || cancellation.IsCancellationRequested", lifecycle);
        Assert.DoesNotContain("File.", lifecycle);
        Assert.DoesNotContain("Directory.", lifecycle);
        Assert.DoesNotContain("Process.", lifecycle);
        var projection = Source("GenesisGuiNextRecordingLibraryProjection.cs");
        Assert.DoesNotContain("OpenFile", projection); Assert.DoesNotContain("OpenFolder", projection);
        Assert.DoesNotContain("ShellExecute", projection); Assert.DoesNotContain("Process.Start", projection);
        Assert.DoesNotContain("Delete", projection); Assert.DoesNotContain("Move(", projection);
    }

    private static GenesisGuiNextTabsViewModel Tabs(FakeLibrary library) => new(new GenesisFavoriteChannelCollection(),
        new GenesisRecordingRuleRepository(path: null), library)
    { SelectedTab = GenesisGuiNextTab.Recording };

    [Fact]
    public async Task CallerCancellationRejectsEvenSuccessfulLateResult()
    {
        var entered = Gate(); var release = Gate();
        var before = Snapshot(Entry("keep.flv"));
        var library = new FakeLibrary(before)
        {
            Handler = async _ =>
            {
                entered.SetResult(); await release.Task;
                return new(GenesisRecordingLibraryRefreshStatus.Published, Snapshot(Entry("late.flv")));
            }
        };
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        using var cancellation = new CancellationTokenSource();
        var task = tabs.RefreshRecordingLibraryAsync(cancellation.Token);
        await entered.Task; cancellation.Cancel(); release.SetResult(); await task;
        Assert.Same(before, tabs.RecordingLibrarySnapshot);
        Assert.Equal(before.Entries[0].Path, Assert.Single(tabs.RecordingLibraryRows).Path);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotDispatchOrChangeBusyState()
    {
        var library = new FakeLibrary(Snapshot());
        await using var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await tabs.RefreshRecordingLibraryAsync(cancellation.Token);
        Assert.Equal(0, library.Calls); Assert.False(tabs.IsRecordingLibraryRefreshing);
    }

    [Fact]
    public async Task CompletionNotificationCannotAccidentallyDrainTheNextRefresh()
    {
        var firstRelease = Gate(); var firstEntered = Gate(); var secondRelease = Gate(); var secondEntered = Gate();
        var cancelled = Gate(); var calls = 0;
        var library = new FakeLibrary(Snapshot())
        {
            Handler = async token =>
        {
            if (Interlocked.Increment(ref calls) == 1) { firstEntered.SetResult(); await firstRelease.Task; }
            else
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                secondEntered.SetResult(); await secondRelease.Task;
            }
            return new(GenesisRecordingLibraryRefreshStatus.Unchanged, Snapshot());
        }
        };
        var tabs = Tabs(library);
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
        Task? second = null;
        var restart = true;
        tabs.PropertyChanged += (_, e) =>
        {
            if (restart && e.PropertyName == nameof(tabs.IsRecordingLibraryRefreshing) && !tabs.IsRecordingLibraryRefreshing)
            {
                restart = false; second = tabs.RefreshRecordingLibraryAsync();
            }
        };
        var first = tabs.RefreshRecordingLibraryAsync(); await firstEntered.Task;
        firstRelease.SetResult(); await first; await secondEntered.Task;
        var shutdown = tabs.DisposeAsync().AsTask(); await cancelled.Task;
        Assert.False(shutdown.IsCompleted);
        secondRelease.SetResult(); await second!; await shutdown;
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProductionCompositionQueriesAndNonLibraryEventsDoNotScanAndRefreshCannotPersist()
    {
        var owned = Path.Combine(Path.GetTempPath(), "Nova-library-native-managed-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var root = Path.Combine(owned, "recordings");
            var settings = new GenesisRecordingOutputDirectorySettings(new(Path.Combine(owned, "settings.json")), root);
            var store = new GenesisRecordingHistoryStore(Path.Combine(owned, "history.json"));
            var files = new EmptyLibraryFileSystem();
            var composition = new PcpRecordingRuntimeComposition(root, store,
                defaultRecordingDirectory: root, libraryFileSystem: files);
            var shared = new GenesisSharedPcpPlaybackRuntime(composition);
            var daily = new NovaDailyHttpFlvPlaybackController(new PcrPlayerExternalPlayerLauncher((string?)null),
                _ => throw new InvalidOperationException("no playback"), _ => throw new InvalidOperationException("no playback"),
                composition, shared, new EmptyRules());
            await using var controller = new GuiNextDailyPlaybackSessionController(daily);
            var vm = new GenesisMainViewModel(controller);
            await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection(),
                new GenesisRecordingRuleRepository(path: null), vm.RecordingLibraryController);
            Assert.Equal(0, files.Enumerations); Assert.Equal(0, files.Probes);
            Assert.Equal(GenesisRecordingLibraryObservationState.NotObservedYet, tabs.RecordingLibrarySnapshot.State);
            daily.RegisterRecordingLibraryRoot(Path.Combine(owned, "other"));
            composition.RecordingHistory.ObserveTerminal(Fact(Path.Combine(root, "historic.flv")));
            _ = settings.Save(root); _ = settings.ResetToDefault();
            _ = daily.GetRecordingHistorySnapshot(); _ = daily.GetKnownRecordingRootsSnapshot();
            _ = vm.RecordingLibraryController!.GetSnapshot();
            Assert.Equal(0, files.Enumerations); Assert.Equal(0, files.Probes);
            var rootsBefore = composition.KnownRecordingRoots.GetSnapshot();
            var historyBefore = composition.RecordingHistory.GetSnapshot();
            var destinationBefore = settings.CurrentRuntimeDirectory;
            tabs.SelectedTab = GenesisGuiNextTab.Recording;
            await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
            Assert.Equal(2, files.Enumerations); Assert.Equal(1, files.Probes);
            Assert.Single(tabs.RecordingLibraryRows);
            Assert.Equal("見つかりません", tabs.RecordingLibraryRows[0].Availability);
            Assert.Equal(rootsBefore.Revision, composition.KnownRecordingRoots.GetSnapshot().Revision);
            Assert.Equal(rootsBefore.Roots, composition.KnownRecordingRoots.GetSnapshot().Roots);
            Assert.Equal(historyBefore.Entries, composition.RecordingHistory.GetSnapshot().Entries);
            Assert.Equal(destinationBefore, settings.CurrentRuntimeDirectory);
            Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
            await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
            await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Library);
            Assert.Equal(2, files.Enumerations);
            Assert.Equal(1, files.Probes);
        }
        finally { if (Directory.Exists(owned)) Directory.Delete(owned, recursive: true); }
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void CurrentMetadataFormattingDoesNotDependOnMachineCulture(string cultureName)
    {
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            var row = GenesisGuiNextRecordingLibraryProjection.CreateRow(Entry("format.flv") with
            { FileMetadata = new(FileAttributes.Normal, 9219498, Time, Time) });
            Assert.Equal("9,219,498 B", row.Size); Assert.Equal("2026-10-07 01:02:03 UTC", row.Modified);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
    }

    private sealed class EmptyLibraryFileSystem : IGenesisRecordingLibraryFileSystem
    {
        public int Enumerations { get; private set; }
        public int Probes { get; private set; }
        public IEnumerable<GenesisRecordingLibraryDirectoryEntry> EnumerateDirectEntries(string root, CancellationToken cancellationToken)
        { Enumerations++; return []; }
        public GenesisRecordingLibraryFileMetadata ReadFileMetadata(string path, CancellationToken cancellationToken)
        { Probes++; throw new FileNotFoundException(); }
    }
    private sealed class EmptyRules : IGenesisRecordingRuleSetProvider
    {
        public ValueTask<RecordingRuleSet> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RecordingRuleSet([]));
    }
    private static GenesisRecordingLibrarySnapshot Initial() => new(0, GenesisRecordingLibraryObservationState.NotObservedYet, [], [], 0, false, 0, false);
    private static GenesisRecordingLibrarySnapshot Snapshot(params GenesisRecordingLibraryEntry[] entries) =>
        new(1, GenesisRecordingLibraryObservationState.Observed, Array.AsReadOnly(entries), [], entries.Length, false, 0, false);
    private static GenesisRecordingLibraryEntry Entry(string filename) => new(Path.Combine(Root, filename),
        GenesisRecordingLibraryAvailability.Available, GenesisRecordingLibraryFailure.None,
        filename.EndsWith(".bin", StringComparison.Ordinal) ? GenesisRecordingLibraryContainer.UnknownContainerCandidate : GenesisRecordingLibraryContainer.FlvCandidate,
        new(FileAttributes.Normal, 123, Time, Time), Root, [], null);
    private static RecordingSessionTerminalFact Fact(string path, bool automatic = false, Guid? id = null, string? name = "Original historical name") =>
        new(automatic ? new RecordingSinkSession(new(id ?? Session), new(Channel), Rule)
                : RecordingSinkSession.Manual(new(id ?? Session), new(Channel)),
            name, "FLV", "flv", Time, Time.AddHours(1), RecordingSinkSessionLifecycleKind.Completed,
            RecordingFileWriteResult.FromFinalizedFacts(path, 123, RecordingFileWriteCompletionKind.Stopped));
    private static GenesisGuiNextNativeTabRow Native(GenesisRecordingLibraryEntry entry) =>
        new(new(string.Empty, string.Empty), null, RecordingLibrary: GenesisGuiNextRecordingLibraryProjection.CreateRow(entry));
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private sealed class FakeLibrary(GenesisRecordingLibrarySnapshot? snapshot = null) : IGenesisRecordingLibraryController
    {
        public GenesisRecordingLibrarySnapshot Current { get; } = snapshot ?? Initial();
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public Func<CancellationToken, Task<GenesisRecordingLibraryRefreshResult>>? Handler { get; init; }
        public GenesisRecordingLibrarySnapshot GetSnapshot() => Current;
        public Task<GenesisRecordingLibraryRefreshResult> RefreshAsync(CancellationToken cancellationToken)
        {
            Calls++; Token = cancellationToken;
            return Handler?.Invoke(cancellationToken) ?? Task.FromResult(new GenesisRecordingLibraryRefreshResult(
                GenesisRecordingLibraryRefreshStatus.Unchanged, Current));
        }
    }
    private sealed class FakeDaily(FakeLibrary library) : INovaDailyPlaybackController, IGenesisRecordingLibraryController
    {
        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded { add { } remove { } }
        public int PlayCalls { get; private set; }
        public int StopCalls { get; private set; }
        public Task PlayAsync(GenesisChannelItem channel, CancellationToken cancellationToken) { PlayCalls++; return Task.CompletedTask; }
        public Task StopAsync() { StopCalls++; return Task.CompletedTask; }
        public GenesisRecordingLibrarySnapshot GetSnapshot() => library.GetSnapshot();
        public Task<GenesisRecordingLibraryRefreshResult> RefreshAsync(CancellationToken cancellationToken) => library.RefreshAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class EmptyCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
    }
}
