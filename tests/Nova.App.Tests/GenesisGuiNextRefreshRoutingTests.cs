using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRefreshRoutingTests
{
    private const ushort VirtualKeyF5 = 0x74;

    [Fact]
    public async Task RulesModeRefreshReloadsRulesWithoutRefreshingCatalog()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Persisted"));
        await using var harness = await CreateRulesHarnessAsync(path);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available,
            harness.Tabs.RecordingRulesStatus);
        Assert.Equal("Persisted", Assert.Single(harness.Tabs.RecordingRuleRows).Name);
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
    }

    [Fact]
    public async Task RulesModeKeyboardRefreshReloadsRulesWithoutRefreshingCatalog()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Before"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var replacement = CreateRule("Keyboard refresh");
        await WriteRulesAsync(path, replacement);

        Assert.Equal(
            GenesisChannelDirectKeyboardCommand.Refresh,
            GenesisChannelDirectKeyboard.Resolve(VirtualKeyF5, false, false));
        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(replacement.Id, Assert.Single(harness.Tabs.RecordingRuleRows).RuleId);
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
    }

    [Fact]
    public async Task RulesModeRefreshReflectsExternalRuleFileChange()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("File A"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var externalRule = CreateRule("File B");
        await WriteRulesAsync(path, externalRule);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal("File B", Assert.Single(harness.Tabs.RecordingRuleRows).Name);
        Assert.Equal(externalRule.Id, harness.Tabs.RecordingRuleRows[0].RuleId);
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
    }

    [Fact]
    public async Task RulesModeRefreshUpdatesRepositoryRevision()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Original"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var originalRevision = harness.Tabs.RecordingRulesRevision;
        await WriteRulesAsync(path, CreateRule("Revised"));

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.NotEqual(originalRevision, harness.Tabs.RecordingRulesRevision);
        Assert.Equal("Revised", Assert.Single(harness.Tabs.RecordingRuleRows).Name);
        Assert.Equal(0, harness.Catalog.ReadCount);
    }

    [Fact]
    public async Task RulesModeRefreshWithMissingFileDoesNotCreateRuleFile()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("missing-recording-rules.json");
        await using var harness = await CreateHarnessAsync(path);
        await harness.Tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.Rules);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.MissingFile,
            harness.Tabs.RecordingRulesStatus);
        Assert.Empty(harness.Tabs.RecordingRuleRows);
        Assert.False(File.Exists(path));
        Assert.Equal(0, harness.SaveCalls);
        Assert.Equal(0, harness.Catalog.ReadCount);
    }

    [Fact]
    public async Task RulesModeRefreshButtonRemainsEnabledWhenFileIsMissing()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("missing-recording-rules.json");
        await using var harness = await CreateHarnessAsync(path);
        await harness.Tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.Rules);

        Assert.True(harness.Interaction.CanRefreshCurrentView);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.MissingFile,
            harness.Tabs.RecordingRulesStatus);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RulesModeRefreshWithMalformedFileShowsUnavailableState()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Before"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var malformedBytes = System.Text.Encoding.UTF8.GetBytes("not json");
        await File.WriteAllBytesAsync(path, malformedBytes);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Invalid,
            harness.Tabs.RecordingRulesStatus);
        Assert.Empty(harness.Tabs.RecordingRuleRows);
        Assert.Contains("形式が不正", harness.Tabs.RecordingRulesStatusText);
        Assert.Equal(malformedBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(0, harness.Catalog.ReadCount);
    }

    [Fact]
    public async Task RulesModeRefreshWithUnsupportedVersionShowsUnavailableState()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Before"));
        await using var harness = await CreateRulesHarnessAsync(path);
        const string unsupported = "{\"version\":2,\"rules\":[]}";
        var unsupportedBytes = System.Text.Encoding.UTF8.GetBytes(unsupported);
        await File.WriteAllBytesAsync(path, unsupportedBytes);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.UnsupportedVersion,
            harness.Tabs.RecordingRulesStatus);
        Assert.Empty(harness.Tabs.RecordingRuleRows);
        Assert.Contains("未対応", harness.Tabs.RecordingRulesStatusText);
        Assert.Equal(unsupportedBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(0, harness.Catalog.ReadCount);
    }

    [Fact]
    public async Task RulesModeRefreshDoesNotTriggerAutomaticRecordingObserver()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Read only"));
        await using var harness = await CreateRulesHarnessAsync(path);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
        Assert.Equal(0, harness.AutomaticObserver.ReportedStartCount);
        Assert.Equal(0, harness.AutomaticObserver.ReportedStopCount);
    }

    [Fact]
    public async Task RulesModeRefreshDoesNotStartOrStopRecording()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Read only"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var activeManual = new GenesisGuiNextActiveRecording(
            new ChannelId(Guid.NewGuid()),
            new RecordingSessionId(Guid.NewGuid()),
            RecordingSessionOrigin.Manual,
            "Manual channel",
            "録画中",
            AutomaticRuleId: null,
            ErrorMessage: null);
        harness.Tabs.UpdateActiveRecordings([activeManual]);
        var before = harness.Tabs.ActiveRecordings;

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Same(before, harness.Tabs.ActiveRecordings);
        Assert.Equal(activeManual.SessionId,
            Assert.Single(harness.Tabs.ActiveRecordings).SessionId);
        Assert.Equal(RecordingSessionOrigin.Manual,
            harness.Tabs.ActiveRecordings[0].Origin);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
        Assert.Equal(0, harness.AutomaticObserver.PlayCallCount);
        Assert.Equal(0, harness.AutomaticObserver.StopCallCount);
    }

    [Fact]
    public async Task RulesModeRefreshDoesNotSaveOrRewriteRuleFile()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Byte stable"));
        var before = await File.ReadAllBytesAsync(path);
        await using var harness = await CreateRulesHarnessAsync(path);

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal(0, harness.SaveCalls);
        Assert.Equal(0, harness.Catalog.ReadCount);
    }

    [Fact]
    public async Task ChannelModeRefreshStillRefreshesCatalog()
    {
        using var directory = new TemporaryDirectory();
        await using var harness = await CreateHarnessAsync(
            directory.FilePath("recording-rules.json"));
        harness.Tabs.SelectedTab = GenesisGuiNextTab.Channels;

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(GenesisGuiNextTab.Channels, harness.Tabs.SelectedTab);
        Assert.Equal(1, harness.Catalog.ReadCount);
        Assert.Equal(1, harness.AutomaticObserver.CallCount);
    }

    [Fact]
    public async Task RecordingActiveModeRefreshPreservesExistingSemantics()
    {
        using var directory = new TemporaryDirectory();
        await using var harness = await CreateHarnessAsync(
            directory.FilePath("recording-rules.json"));
        harness.Tabs.SelectedTab = GenesisGuiNextTab.Recording;

        await harness.Interaction.RefreshCurrentViewAsync();

        Assert.Equal(GenesisGuiNextRecordingPageMode.ActiveRecordings,
            harness.Tabs.RecordingPageMode);
        Assert.Equal(1, harness.Catalog.ReadCount);
        Assert.Equal(1, harness.AutomaticObserver.CallCount);
    }

    [Fact]
    public async Task RulesRefreshAndModeSwitchRemainRaceSafe()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Race safe"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var activeManual = new GenesisGuiNextActiveRecording(
            new ChannelId(Guid.NewGuid()),
            new RecordingSessionId(Guid.NewGuid()),
            RecordingSessionOrigin.Manual,
            "Manual channel",
            "録画中",
            AutomaticRuleId: null,
            ErrorMessage: null);
        harness.Tabs.UpdateActiveRecordings([activeManual]);

        var refresh = harness.Interaction.RefreshCurrentViewAsync().AsTask();
        var switchToActive = harness.Tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.ActiveRecordings);
        await Task.WhenAll(refresh, switchToActive);

        Assert.Equal(GenesisGuiNextRecordingPageMode.ActiveRecordings,
            harness.Tabs.RecordingPageMode);
        Assert.Equal(activeManual.SessionId,
            Assert.Single(harness.Tabs.ActiveRecordings).SessionId);
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);

        await harness.Tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.Rules);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available,
            harness.Tabs.RecordingRulesStatus);
    }

    [Fact]
    public async Task ApplicationShutdownDrainsInFlightRulesRefresh()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Shutdown"));
        await using var harness = await CreateRulesHarnessAsync(path);

        var refresh = harness.Interaction.RefreshCurrentViewAsync().AsTask();
        await harness.Tabs.DisposeAsync();
        await refresh;

        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
        Assert.False(harness.Interaction.CanRefreshCurrentView);
    }

    [Fact]
    public async Task RepeatedRulesRefreshIsBounded()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("recording-rules.json");
        await WriteRulesAsync(path, CreateRule("Initial"));
        await using var harness = await CreateRulesHarnessAsync(path);
        var finalRule = CreateRule("Latest");
        await WriteRulesAsync(path, finalRule);

        var refreshes = Enumerable.Range(0, 16)
            .Select(_ => harness.Interaction.RefreshCurrentViewAsync().AsTask())
            .ToArray();
        await Task.WhenAll(refreshes);

        Assert.False(harness.Tabs.IsRecordingRulesLoading);
        Assert.Equal(finalRule.Id, Assert.Single(harness.Tabs.RecordingRuleRows).RuleId);
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
    }

    private static async Task<RoutingHarness> CreateRulesHarnessAsync(string path)
    {
        var harness = await CreateHarnessAsync(path);
        await harness.Tabs.SelectRecordingPageModeAsync(
            GenesisGuiNextRecordingPageMode.Rules);
        return harness;
    }

    [Fact]
    public async Task CreateSaveDoesNotTriggerAutomaticEvaluation()
    {
        using var directory = new TemporaryDirectory();
        await using var harness = await CreateRulesHarnessAsync(directory.FilePath("rules.json"));
        var draft = harness.Tabs.CreateRecordingRule()! with
        {
            Name = "explicit",
            IsEnabled = true,
            NamePattern = new("specific"),
        };
        Assert.True((await harness.Tabs.RuleEditor.SaveAsync(draft)).Applied);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
        Assert.Equal(0, harness.Catalog.ReadCount);
    }

    [Fact]
    public async Task CreateSaveDoesNotTriggerCatalogRefresh()
    {
        using var directory = new TemporaryDirectory();
        await using var harness = await CreateRulesHarnessAsync(directory.FilePath("rules.json"));
        var draft = harness.Tabs.CreateRecordingRule()! with { Name = "explicit", NamePattern = new("specific") };
        Assert.True((await harness.Tabs.RuleEditor.SaveAsync(draft)).Applied);
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
    }

    [Theory]
    [InlineData(RecordingSessionOrigin.Manual)]
    [InlineData(RecordingSessionOrigin.AutomaticRule)]
    public async Task SavingRulePreservesCurrentRecordingAndDispatchesNoEvaluation(RecordingSessionOrigin origin)
    {
        using var directory = new TemporaryDirectory();
        await using var harness = await CreateRulesHarnessAsync(directory.FilePath("rules.json"));
        var active = new GenesisGuiNextActiveRecording(new ChannelId(Guid.NewGuid()),
            new RecordingSessionId(Guid.NewGuid()), origin, "active", "録画中", Guid.NewGuid(), null);
        harness.Tabs.UpdateActiveRecordings([active]);
        var rows = harness.Tabs.ActiveRecordings;
        var draft = harness.Tabs.CreateRecordingRule()! with { Name = "Ignore", Action = RecordingRuleAction.Ignore, MatchAll = true };
        Assert.True((await harness.Tabs.RuleEditor.SaveAsync(draft, true)).Applied);
        Assert.Same(rows, harness.Tabs.ActiveRecordings);
        Assert.Same(active, Assert.Single(harness.Tabs.ActiveRecordings));
        Assert.Equal(0, harness.Catalog.ReadCount);
        Assert.Equal(0, harness.AutomaticObserver.CallCount);
    }

    private static Task<RoutingHarness> CreateHarnessAsync(string path)
    {
        var catalog = new CountingCatalog();
        var automaticObserver = new AutomaticObserverSpy();
        var saveCallCount = 0;
        var repository = new GenesisRecordingRuleRepository(
            path,
            (_, _, _) =>
            {
                Interlocked.Increment(ref saveCallCount);
                return ValueTask.CompletedTask;
            });
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection(),
            repository)
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };
        var viewModel = new GenesisMainViewModel(automaticObserver);
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var session = GenesisChannelCatalogSession.Create(
            catalog,
            viewModel,
            publisher);
        return Task.FromResult(new RoutingHarness(
            repository,
            tabs,
            viewModel,
            session,
            catalog,
            automaticObserver,
            () => Volatile.Read(ref saveCallCount)));
    }

    private static async Task WriteRulesAsync(string path, params RecordingRule[] rules) =>
        await File.WriteAllTextAsync(
            path,
            RecordingRuleJsonCodec.Serialize(new RecordingRuleSet(rules)));

    private static RecordingRule CreateRule(string name) =>
        new(
            Guid.NewGuid(),
            name,
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll,
            priority: 0,
            isEnabled: true);

    private sealed class RoutingHarness : IAsyncDisposable
    {
        private readonly GenesisRecordingRuleRepository _repository;
        private readonly GenesisGuiNextTabsViewModel _tabs;
        private readonly GenesisChannelCatalogSession _session;
        private readonly Func<int> _getSaveCallCount;

        public RoutingHarness(
            GenesisRecordingRuleRepository repository,
            GenesisGuiNextTabsViewModel tabs,
            GenesisMainViewModel viewModel,
            GenesisChannelCatalogSession session,
            CountingCatalog catalog,
            AutomaticObserverSpy automaticObserver,
            Func<int> getSaveCallCount)
        {
            _repository = repository;
            _tabs = tabs;
            _session = session;
            _getSaveCallCount = getSaveCallCount;
            Tabs = tabs;
            Interaction = new(viewModel, session);
            Catalog = catalog;
            AutomaticObserver = automaticObserver;
        }

        public GenesisGuiNextTabsViewModel Tabs { get; }

        public GenesisMainWindowInteraction Interaction { get; }

        public CountingCatalog Catalog { get; }

        public AutomaticObserverSpy AutomaticObserver { get; }

        public int SaveCalls => _getSaveCallCount();

        public async ValueTask DisposeAsync()
        {
            await _session.DisposeAsync();
            await _tabs.DisposeAsync();
            await _repository.DisposeAsync();
        }
    }

    private sealed class CountingCatalog : IGenesisChannelCatalog
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
        }
    }

    private sealed class AutomaticObserverSpy :
        IPlaybackSessionController,
        IGenesisAutomaticRecordingCatalogObserver
    {
        private int _callCount;
        private int _playCallCount;
        private int _stopCallCount;
        private int _reportedStartCount;
        private int _reportedStopCount;

        public PlaybackSessionSnapshot Snapshot => PlaybackSessionSnapshot.Stopped;

        public int CallCount => Volatile.Read(ref _callCount);

        public int PlayCallCount => Volatile.Read(ref _playCallCount);

        public int StopCallCount => Volatile.Read(ref _stopCallCount);

        public int ReportedStartCount => Volatile.Read(ref _reportedStartCount);

        public int ReportedStopCount => Volatile.Read(ref _reportedStopCount);

        public ValueTask<GenesisAutomaticRecordingEvaluationSummary> ObserveAsync(
            IReadOnlyList<GenesisChannelItem> channels,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            Interlocked.Increment(ref _reportedStartCount);
            Interlocked.Increment(ref _reportedStopCount);
            return ValueTask.FromResult(new GenesisAutomaticRecordingEvaluationSummary(
                RuleSetAvailable: true,
                CandidateCount: channels.Count,
                EvaluatedCount: channels.Count,
                StartedCount: 1,
                StoppedCount: 1,
                FailedCount: 0,
                Cancelled: false));
        }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            _ = source;
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _playCallCount);
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _stopCallCount);
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                PlaybackSessionSnapshot.Stopped));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-refresh-routing-tests",
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
}
