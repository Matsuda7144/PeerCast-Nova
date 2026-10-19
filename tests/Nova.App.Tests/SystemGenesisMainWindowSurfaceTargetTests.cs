namespace PeerCast.Nova.App.Tests;

public sealed class SystemGenesisMainWindowSurfaceTargetTests
{
    [Fact]
    public async Task SupportedWindowCreatesOneNativeSurface()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisMainWindowSurfaceTarget(factory, static () => true);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        var lifetime = await target.OpenAsync(7144, viewModel, CancellationToken.None);

        Assert.NotNull(lifetime);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(7144, factory.LastWindowToken);
        Assert.Same(viewModel, factory.LastViewModel);
        await lifetime.DisposeAsync();
        Assert.True(factory.Lifetime.Disposed);
    }

    [Fact]
    public async Task UnsupportedPlatformRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisMainWindowSurfaceTarget(factory, static () => false);

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task ZeroWindowRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisMainWindowSurfaceTarget(factory, static () => true);

        var lifetime = await target.OpenAsync(
            0,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            CancellationToken.None);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task CancellationRejectsBeforeNativeSurfaceCreation()
    {
        var factory = new RecordingFactory();
        var target = new SystemGenesisMainWindowSurfaceTarget(factory, static () => true);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var lifetime = await target.OpenAsync(
            7144,
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            cancellationSource.Token);

        Assert.Null(lifetime);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public void InitialFocusTargetsSourceField()
    {
        nint focusedControl = 0;

        var focused = GenesisMainWindowNativeSurface.FocusInitialControl(
            (nint)7144,
            control =>
            {
                focusedControl = control;
                return 0;
            });

        Assert.True(focused);
        Assert.Equal((nint)7144, focusedControl);
    }

    [Fact]
    public void InitialFocusRejectsMissingSourceField()
    {
        var focusCalled = false;

        var focused = GenesisMainWindowNativeSurface.FocusInitialControl(
            0,
            control =>
            {
                focusCalled = true;
                return control;
            });

        Assert.False(focused);
        Assert.False(focusCalled);
    }

    [Fact]
    public void FailedRowReplacementClearsPartialRows()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel");
        var operations = new List<string>();

        var replaced = GenesisMainWindowNativeSurface.ReplaceRows(
            (nint)7144,
            [channel],
            window =>
            {
                Assert.Equal((nint)7144, window);
                operations.Add("clear");
                return true;
            },
            (window, channels) =>
            {
                Assert.Equal((nint)7144, window);
                Assert.Single(channels);
                Assert.Same(channel, channels[0]);
                operations.Add("add");
                return false;
            });

        Assert.False(replaced);
        Assert.Equal(["clear", "add", "clear"], operations);
    }

    [Fact]
    public void RecordingModeColumnReplacementDeletesOldColumnsAndAddsRuleColumns()
    {
        var operations = new List<string>();
        var ruleColumns = GenesisRecordingRulesListPresentation.Columns;

        var replaced = GenesisMainWindowNativeSurface.ReplaceColumns(
            (nint)7144,
            existingColumnCount: GenesisChannelListPresentation.Columns.Count,
            ruleColumns,
            (window, index) =>
            {
                Assert.Equal((nint)7144, window);
                operations.Add($"delete:{index}");
                return true;
            },
            (window, columns) =>
            {
                Assert.Equal((nint)7144, window);
                Assert.Same(ruleColumns, columns);
                operations.Add("add");
                return true;
            });

        Assert.True(replaced);
        Assert.Equal(
            ["delete:6", "delete:5", "delete:4", "delete:3", "delete:2", "delete:1", "delete:0", "add"],
            operations);
    }

    private sealed class RecordingFactory : IGenesisMainWindowNativeSurfaceFactory
    {
        public RecordingLifetime Lifetime { get; } = new();

        public int CreateCount { get; private set; }

        public long LastWindowToken { get; private set; }

        public GenesisMainViewModel? LastViewModel { get; private set; }

        public IAsyncDisposable? Create(
            long windowToken,
            GenesisMainViewModel viewModel)
        {
            CreateCount++;
            LastWindowToken = windowToken;
            LastViewModel = viewModel;
            return Lifetime;
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
