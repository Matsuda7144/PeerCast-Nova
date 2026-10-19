using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRecordingRulesPageTests
{
    [Fact]
    public void ModePresentationKeepsTheExistingRecordingPageAsDefault()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());

        Assert.Equal(
            GenesisGuiNextRecordingPageMode.ActiveRecordings,
            tabs.RecordingPageMode);
        Assert.Equal(
            ["録画中", "ライブラリ", "ルール", "履歴"],
            GenesisGuiNextRecordingPageModePresentation.Items
                .Select(static item => item.Label));
    }

    [Fact]
    public void RuleProjectionRetainsPersistedOrderForEqualPriorities()
    {
        var first = CreateRule(
            "Zeta",
            priority: 7,
            isEnabled: false,
            action: RecordingRuleAction.Ignore);
        var second = CreateRule("Alpha", priority: 7);

        var rows = GenesisGuiNextRecordingRuleProjection.CreateRows(
            new RecordingRuleSet([first, second]));

        Assert.Equal([first.Id, second.Id], rows.Select(static row => row.RuleId));
        Assert.Equal([1, 2], rows.Select(static row => row.PersistedIndex));
        Assert.Equal([7, 7], rows.Select(static row => row.Priority));
        Assert.Equal(["Zeta", "Alpha"], rows.Select(static row => row.Name));
        Assert.False(rows[0].IsEnabled);
        Assert.True(rows[1].IsEnabled);
        Assert.Equal(RecordingRuleAction.Ignore, rows[0].Action);
    }

    [Fact]
    public void CriteriaSummaryShowsEveryPersistedCriterionAndMatchMode()
    {
        var channelId = new ChannelId(Guid.Parse(
            "531dc8df-c7fb-4292-8ac2-c0a626517a87"));
        var criteria = new RecordingRuleCriteria(
            channelId: channelId,
            name: new("foo", RecordingTextMatchMode.Contains),
            genre: new("Talk", RecordingTextMatchMode.Exact),
            description: new("^game", RecordingTextMatchMode.Regex),
            contentType: new("audio/flv", RecordingTextMatchMode.Exact),
            streamType: new("FLV", RecordingTextMatchMode.Exact),
            comment: new("live", RecordingTextMatchMode.Contains),
            minimumBitrateKbps: 128,
            maximumBitrateKbps: 320);

        var summary = GenesisGuiNextRecordingRuleProjection.FormatCriteria(criteria);

        Assert.Contains($"ChannelId: {channelId}", summary);
        Assert.Contains("名前: \"foo\"（一部一致）", summary);
        Assert.Contains("Genre: \"Talk\"（完全一致）", summary);
        Assert.Contains("説明: \"^game\"（正規表現）", summary);
        Assert.Contains("ContentType: \"audio/flv\"（完全一致）", summary);
        Assert.Contains("StreamType: \"FLV\"（完全一致）", summary);
        Assert.Contains("Comment: \"live\"（一部一致）", summary);
        Assert.Contains("ビットレート: 128–320 kbps", summary);
        Assert.StartsWith("すべての条件に一致:", summary, StringComparison.Ordinal);
        Assert.Equal(
            "すべてのチャンネルに一致",
            GenesisGuiNextRecordingRuleProjection.FormatCriteria(
                RecordingRuleCriteria.MatchAll));
    }

    [Theory]
    [InlineData(128, null, "ビットレート: 128 kbps以上")]
    [InlineData(null, 320, "ビットレート: 320 kbps以下")]
    [InlineData(128, 320, "ビットレート: 128–320 kbps")]
    public void CriteriaSummaryShowsBitrateBoundsTruthfully(
        int? minimum,
        int? maximum,
        string expected)
    {
        var summary = GenesisGuiNextRecordingRuleProjection.FormatCriteria(
            new RecordingRuleCriteria(
                minimumBitrateKbps: minimum,
                maximumBitrateKbps: maximum));

        Assert.Contains(expected, summary);
    }

    [Fact]
    public async Task RulesModeLoadsFreshRepositorySnapshotAndPreservesRevision()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        var firstRule = CreateRule("First", priority: 5);
        var firstSet = new RecordingRuleSet([firstRule]);
        await File.WriteAllTextAsync(path, RecordingRuleJsonCodec.Serialize(firstSet));
        var repository = new GenesisRecordingRuleRepository(path);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection(),
            repository)
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };

        Assert.Equal(
            GenesisGuiNextRecordingPageMode.ActiveRecordings,
            tabs.RecordingPageMode);
        Assert.Null(tabs.RecordingRulesSnapshot);

        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        var firstRevision = Assert.IsType<GenesisRecordingRuleRevision>(
            tabs.RecordingRulesRevision);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, tabs.RecordingRulesStatus);
        Assert.Equal(firstRule.Id, Assert.Single(tabs.RecordingRuleRows).RuleId);
        Assert.Equal(firstRevision, tabs.RecordingRulesSnapshot?.Revision);

        await tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.ActiveRecordings);
        var secondRule = CreateRule("Second", priority: 5);
        var secondSet = new RecordingRuleSet([secondRule]);
        await File.WriteAllTextAsync(path, RecordingRuleJsonCodec.Serialize(secondSet));
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);

        Assert.Equal(secondRule.Id, Assert.Single(tabs.RecordingRuleRows).RuleId);
        Assert.NotEqual(firstRevision, tabs.RecordingRulesRevision);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, tabs.RecordingRulesStatus);

        await tabs.DisposeAsync();
        await repository.DisposeAsync();
    }

    [Fact]
    public async Task RulesModeDoesNotMutateSavedFileOrTriggerAutomaticEvaluation()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        var savedRules = new RecordingRuleSet([CreateRule("Persisted")]);
        await File.WriteAllTextAsync(path, RecordingRuleJsonCodec.Serialize(savedRules));
        var before = await File.ReadAllBytesAsync(path);
        var repository = new GenesisRecordingRuleRepository(path);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection(),
            repository)
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };

        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal(GenesisGuiNextRecordingPageMode.Rules, tabs.RecordingPageMode);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, tabs.RecordingRulesStatus);

        await tabs.DisposeAsync();
        await repository.DisposeAsync();
    }

    [Fact]
    public async Task RulesListProjectsOrderedNonInteractiveRowsAndRuleColumns()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        var first = CreateRule("Second by name", priority: 100, isEnabled: false);
        var second = CreateRule(
            "First by name",
            priority: 100,
            action: RecordingRuleAction.Ignore);
        await File.WriteAllTextAsync(
            path,
            RecordingRuleJsonCodec.Serialize(new RecordingRuleSet([first, second])));
        var repository = new GenesisRecordingRuleRepository(path);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection(),
            repository)
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);

        var rows = GenesisGuiNextNativeTabProjection.Create(tabs);
        var firstRow = rows[0];
        var secondRow = rows[1];

        Assert.Equal(first.Id, firstRow.RecordingRule?.RuleId);
        Assert.Equal(second.Id, secondRow.RecordingRule?.RuleId);
        Assert.Null(firstRow.InteractiveChannel);
        Assert.False(firstRow.IsInteractive);
        Assert.Equal(string.Empty, firstRow.DisplayChannel.Name);
        Assert.Equal(
            ["無効", "Second by name", "録画", "100", "1", "すべてのチャンネルに一致"],
            GenesisRecordingRulesListPresentation.CreateRowValues(
                firstRow.RecordingRule!));
        Assert.Equal("除外", GenesisGuiNextRecordingRuleProjection.ActionLabel(
            secondRow.RecordingRule!.Action));
        Assert.Equal(
            ["状態", "ルール名", "動作", "優先度", "保存順", "条件（すべて一致）"],
            GenesisRecordingRulesListPresentation.Columns.Select(
                static column => column.Header));

        await tabs.DisposeAsync();
        await repository.DisposeAsync();
    }

    [Fact]
    public void RuleSelectionAnchorUsesRuleId()
    {
        var listView = (nint)0x71440031;
        var ruleId = Guid.NewGuid();
        var first = NativeRuleRow(ruleId, "First");
        var refreshed = NativeRuleRow(ruleId, "Updated name");
        var differentRule = NativeRuleRow(Guid.NewGuid(), "Different");
        var restoreCalls = 0;

        try
        {
            GenesisChannelListRenderedChannels.SetRows(listView, [first]);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 0);
            GenesisChannelListRenderedChannels.SetRows(
                listView,
                [refreshed],
                (_, row) =>
                {
                    Assert.Equal(0, row);
                    restoreCalls++;
                    return true;
                });

            Assert.Equal(1, restoreCalls);
            Assert.Equal(
                -1,
                GenesisChannelListRenderedChannels.FindAnchoredRow(
                    listView,
                    [differentRule]));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }
    }

    [Fact]
    public async Task RuleRowsCannotPlayRecordStopFavoriteOrOpenContextActions()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        var rule = CreateRule("Read only");
        await File.WriteAllTextAsync(
            path,
            RecordingRuleJsonCodec.Serialize(new RecordingRuleSet([rule])));
        var repository = new GenesisRecordingRuleRepository(path);
        var favorites = new GenesisFavoriteChannelCollection();
        var tabs = new GenesisGuiNextTabsViewModel(favorites, repository)
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        var playback = new GenesisMainViewModel(new FakePlaybackSessionController());

        Assert.False(GenesisGuiNextPlaybackCommandPresentation.Resolve(
            playback,
            GenesisGuiNextTab.Recording,
            row).IsPlayEnabled);
        Assert.False(GenesisGuiNextRecordingCommandPresentation.Resolve(
            playback,
            GenesisGuiNextTab.Recording,
            row).IsRecordEnabled);
        Assert.False(GenesisGuiNextRecordingCommandPresentation.Resolve(
            playback,
            GenesisGuiNextTab.Recording,
            row).IsStopRecordingEnabled);
        Assert.False(GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(tabs, row).IsEnabled);
        Assert.Equal(
            GenesisGuiNextFavoriteToggleResult.Unavailable,
            GenesisGuiNextFavoriteToggle.Apply(tabs, row));

        var interaction = new GenesisMainWindowInteraction(playback);
        Assert.False(await GenesisGuiNextPlaybackCommandDispatch
            .ActivateSelectedChannelAsync(
                playback,
                interaction,
                GenesisGuiNextTab.Recording,
                row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StartSelectedAsync(
            playback,
            GenesisGuiNextTab.Recording,
            row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopSelectedAsync(
            playback,
            GenesisGuiNextTab.Recording,
            row));
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
            playback,
            row));

        var selectionSynchronized = false;
        var menuOpened = false;
        Assert.False(GenesisChannelDirectContextMenuDispatch.TryOpen(
            row,
            _ =>
            {
                selectionSynchronized = true;
                return true;
            },
            _ => menuOpened = true));
        Assert.False(selectionSynchronized);
        Assert.False(menuOpened);
        Assert.Empty(favorites.Snapshot);

        await tabs.DisposeAsync();
        await repository.DisposeAsync();
    }

    [Fact]
    public async Task MissingAndMalformedRulesAreNotPresentedAsEmpty()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = directory.FilePath("missing-recording-rules.json");
        var missingRepository = new GenesisRecordingRuleRepository(missingPath);
        var missingTabs = CreateRecordingTabs(missingRepository);

        await missingTabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.MissingFile, missingTabs.RecordingRulesStatus);
        Assert.Equal(GenesisGuiNextRecordingRuleProjection.EmptyStateText,
            missingTabs.RecordingRulesStatusText);
        Assert.False(File.Exists(missingPath));
        await missingTabs.DisposeAsync();
        await missingRepository.DisposeAsync();

        var malformedPath = directory.FilePath("malformed-recording-rules.json");
        await File.WriteAllTextAsync(malformedPath, "not json");
        var malformedRepository = new GenesisRecordingRuleRepository(malformedPath);
        var malformedTabs = CreateRecordingTabs(malformedRepository);

        await malformedTabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Invalid, malformedTabs.RecordingRulesStatus);
        Assert.Empty(malformedTabs.RecordingRuleRows);
        Assert.NotEqual(GenesisGuiNextRecordingRuleProjection.EmptyStateText,
            malformedTabs.RecordingRulesStatusText);
        Assert.Contains("形式が不正", malformedTabs.RecordingRulesStatusText);
        await malformedTabs.DisposeAsync();
        await malformedRepository.DisposeAsync();

        var emptyPath = directory.FilePath("empty-recording-rules.json");
        await File.WriteAllTextAsync(
            emptyPath,
            RecordingRuleJsonCodec.Serialize(new RecordingRuleSet([])));
        var emptyRepository = new GenesisRecordingRuleRepository(emptyPath);
        var emptyTabs = CreateRecordingTabs(emptyRepository);
        await emptyTabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, emptyTabs.RecordingRulesStatus);
        Assert.Empty(emptyTabs.RecordingRuleRows);
        Assert.Equal(GenesisGuiNextRecordingRuleProjection.EmptyStateText,
            emptyTabs.RecordingRulesStatusText);
        await emptyTabs.DisposeAsync();
        await emptyRepository.DisposeAsync();

        var unsupportedPath = directory.FilePath("unsupported-recording-rules.json");
        await File.WriteAllTextAsync(unsupportedPath, "{\"version\":2,\"rules\":[]}");
        var unsupportedRepository = new GenesisRecordingRuleRepository(unsupportedPath);
        var unsupportedTabs = CreateRecordingTabs(unsupportedRepository);
        await unsupportedTabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        Assert.Equal(
            GenesisRecordingRuleSnapshotStatus.UnsupportedVersion,
            unsupportedTabs.RecordingRulesStatus);
        Assert.Empty(unsupportedTabs.RecordingRuleRows);
        Assert.Contains("未対応", unsupportedTabs.RecordingRulesStatusText);
        Assert.NotEqual(
            GenesisGuiNextRecordingRuleProjection.EmptyStateText,
            unsupportedTabs.RecordingRulesStatusText);
        await unsupportedTabs.DisposeAsync();
        await unsupportedRepository.DisposeAsync();
    }

    [Fact]
    public async Task UnsupportedAndUnavailableStatusMessagesRemainDistinctFromEmpty()
    {
        foreach (var status in new[]
                 {
                     GenesisRecordingRuleSnapshotStatus.Invalid,
                     GenesisRecordingRuleSnapshotStatus.UnsupportedVersion,
                     GenesisRecordingRuleSnapshotStatus.Unavailable,
                     GenesisRecordingRuleSnapshotStatus.Cancelled,
                 })
        {
            var text = GenesisGuiNextRecordingRuleProjection.StatusText(
                status,
                GenesisRecordingRuleRepositoryFailureKind.Io,
                ruleCount: 0);

            Assert.NotEqual(GenesisGuiNextRecordingRuleProjection.EmptyStateText, text);
        }

        Assert.Contains("未対応", GenesisGuiNextRecordingRuleProjection.StatusText(
            GenesisRecordingRuleSnapshotStatus.UnsupportedVersion,
            GenesisRecordingRuleRepositoryFailureKind.UnsupportedVersion,
            ruleCount: 0));
        Assert.Contains("保存先", GenesisGuiNextRecordingRuleProjection.StatusText(
            GenesisRecordingRuleSnapshotStatus.Unavailable,
            GenesisRecordingRuleRepositoryFailureKind.NoPath,
            ruleCount: 0));
        Assert.Contains("キャンセル", GenesisGuiNextRecordingRuleProjection.StatusText(
            GenesisRecordingRuleSnapshotStatus.Cancelled,
            GenesisRecordingRuleRepositoryFailureKind.Cancelled,
            ruleCount: 0));
    }

    [Fact]
    public async Task SwitchingRulesAndActiveRecordingsPreservesTheRecordingProjection()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await File.WriteAllTextAsync(
            path,
            RecordingRuleJsonCodec.Serialize(new RecordingRuleSet([CreateRule("Saved")])));
        var repository = new GenesisRecordingRuleRepository(path);
        var tabs = CreateRecordingTabs(repository);
        var recording = new GenesisGuiNextActiveRecording(
            new ChannelId(Guid.NewGuid()),
            new RecordingSessionId(Guid.NewGuid()),
            RecordingSessionOrigin.AutomaticRule,
            "Currently recording",
            "録画中",
            Guid.NewGuid(),
            null);
        tabs.UpdateActiveRecordings([recording]);

        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        var activeRows = tabs.ActiveRecordings;
        await tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.ActiveRecordings);

        Assert.Same(activeRows, tabs.ActiveRecordings);
        Assert.Same(recording, Assert.Single(tabs.ActiveRecordings));
        Assert.Same(recording, Assert.Single(
            GenesisGuiNextNativeTabProjection.Create(tabs)).ActiveRecording);

        await tabs.DisposeAsync();
        await repository.DisposeAsync();
    }

    [Fact]
    public async Task ApplicationShutdownDisposesItsRulesRepository()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        GenesisRecordingRuleRepository? repository = null;
        var factoryCalls = 0;
        var state = await GenesisGuiNextApplicationState
            .LoadWithRecordingRuleRepositoryFactoryAsync(
                new EmptyFavoriteStore(),
                new EmptyNotificationStore(),
                new EmptyYellowPageStore(),
                new EmptyPlaybackPreferencesStore(),
                new SilentSoundPlayer(),
                pathSource: null,
                () =>
                {
                    factoryCalls++;
                    repository = new GenesisRecordingRuleRepository(path);
                    return repository;
                });

        Assert.Equal(1, factoryCalls);
        await state.DisposeAsync();
        var afterShutdown = await Assert.IsType<GenesisRecordingRuleRepository>(repository)
            .GetSnapshotAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Cancelled, afterShutdown.Status);
        Assert.Equal(GenesisRecordingRuleRepositoryFailureKind.ShuttingDown,
            afterShutdown.FailureKind);
    }

    private static GenesisGuiNextTabsViewModel CreateRecordingTabs(
        GenesisRecordingRuleRepository repository) =>
        new(new GenesisFavoriteChannelCollection(), repository)
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };

    [Fact]
    public async Task ApplicationStateShutdownCancelsAndDrainsSaveBeforeDisposingRepository()
    {
        using var directory = new TemporaryDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var repository = new GenesisRecordingRuleRepository(directory.FilePath("rules.json"),
            async (_, _, token) =>
            {
                entered.SetResult();
                using var registration = token.Register(() => cancelled.TrySetResult());
                await release.Task;
                token.ThrowIfCancellationRequested();
            });
        await using var state = await GenesisGuiNextApplicationState.LoadWithRecordingRuleRepositoryFactoryAsync(
            new EmptyFavoriteStore(), new EmptyNotificationStore(), new EmptyYellowPageStore(),
            new EmptyPlaybackPreferencesStore(), new SilentSoundPlayer(), null, () => repository);
        state.Tabs.SelectedTab = GenesisGuiNextTab.Recording;
        await state.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        var draft = state.Tabs.CreateRecordingRule()! with { Name = "shutdown", NamePattern = new("specific") };
        var save = state.Tabs.RuleEditor.SaveAsync(draft);
        await entered.Task;
        var shutdown = state.DisposeAsync().AsTask();
        await cancelled.Task;
        Assert.False(shutdown.IsCompleted);
        Assert.Null(state.Tabs.CreateRecordingRule());
        release.SetResult();
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, (await save).Status);
        await shutdown;
        var after = await repository.GetSnapshotAsync();
        Assert.Equal(GenesisRecordingRuleRepositoryFailureKind.ShuttingDown, after.FailureKind);
        Assert.False(File.Exists(directory.FilePath("rules.json")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ApplicationShutdownDrainsQuickSaveBeforeRepositoryDisposal(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        using var directory = new TemporaryDirectory();
        var entered = RuleMutationFixture.Signal(); var cancelled = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var path = directory.FilePath("rules.json");
        var rule = RuleMutationFixture.Rule("target", action == GenesisRecordingRuleQuickAction.Disable);
        var rules = action is GenesisRecordingRuleQuickAction.MoveUp or GenesisRecordingRuleQuickAction.MoveDown
            ? new[] { RuleMutationFixture.Rule("before", false), rule, RuleMutationFixture.Rule("after", false) } : [rule];
        await File.WriteAllTextAsync(path, RecordingRuleJsonCodec.Serialize(new(rules)));
        await using var repository = new GenesisRecordingRuleRepository(path, async (_, _, token) =>
        {
            entered.SetResult();
            using var registration = token.Register(() => cancelled.TrySetResult());
            await release.Task; token.ThrowIfCancellationRequested();
        });
        await using var state = await GenesisGuiNextApplicationState.LoadWithRecordingRuleRepositoryFactoryAsync(
            new EmptyFavoriteStore(), new EmptyNotificationStore(), new EmptyYellowPageStore(),
            new EmptyPlaybackPreferencesStore(), new SilentSoundPlayer(), null, () => repository);
        state.Tabs.SelectedTab = GenesisGuiNextTab.Recording;
        await state.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        var pending = state.Tabs.QuickMutateRecordingRuleAsync(rule.Id, action, RuleMutationFixture.Yes);
        await entered.Task;
        var shutdown = state.DisposeAsync().AsTask();
        await cancelled.Task;
        Assert.False(shutdown.IsCompleted);
        Assert.False(state.Tabs.CanQuickMutateRecordingRule(rule.Id));
        release.SetResult();
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, (await pending).Status);
        await shutdown;
        Assert.Equal(GenesisRecordingRuleRepositoryFailureKind.ShuttingDown, (await repository.GetSnapshotAsync()).FailureKind);
        Assert.Equal(RecordingRuleJsonCodec.Serialize(new(rules)), await File.ReadAllTextAsync(path));
    }

    private static GenesisGuiNextNativeTabRow NativeRuleRow(Guid ruleId, string name) =>
        new(
            new GenesisChannelItem(string.Empty, string.Empty),
            InteractiveChannel: null,
            RecordingRule: new GenesisGuiNextRecordingRuleRow(
                ruleId,
                PersistedIndex: 1,
                name,
                IsEnabled: true,
                RecordingRuleAction.Record,
                Priority: 0,
                "すべてのチャンネルに一致"));

    private static RecordingRule CreateRule(
        string name,
        int priority = 0,
        bool isEnabled = true,
        RecordingRuleAction action = RecordingRuleAction.Record) =>
        new(Guid.NewGuid(), name, action, RecordingRuleCriteria.MatchAll, priority, isEnabled);

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-recording-rules-page-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string FilePath(string fileName) => Path.Combine(_path, fileName);

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }

    private sealed class EmptyFavoriteStore : IGenesisFavoriteChannelStore
    {
        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>(
                Array.Empty<GenesisFavoriteChannel>());

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisFavoriteChannel> favorites,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class EmptyNotificationStore : IGenesisChannelNotificationPreferenceStore
    {
        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);

        public ValueTask SaveAsync(
            GenesisChannelNotificationPreferences preferences,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class EmptyYellowPageStore : IGenesisYellowPageConfigurationStore
    {
        public ValueTask<IReadOnlyList<GenesisYellowPageDefinition>> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GenesisYellowPageDefinition>>(
                Array.Empty<GenesisYellowPageDefinition>());

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisYellowPageDefinition> definitions,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class EmptyPlaybackPreferencesStore : IGenesisGuiNextPlaybackPreferencesStore
    {
        public ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisGuiNextPlaybackPreferences.Default);

        public ValueTask SaveAsync(
            GenesisGuiNextPlaybackPreferences preferences,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class SilentSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
