namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextRefreshShortcutTests
{
    [Theory]
    [InlineData(-1, 1, 0x0100, 0x74, true, false)]
    [InlineData(0, 0, 0x0100, 0x74, true, false)]
    [InlineData(0, 1, 0x0101, 0x74, true, false)]
    [InlineData(0, 1, 0x0104, 0x74, true, false)]
    [InlineData(0, 1, 0x0100, 0x09, true, false)]
    [InlineData(0, 1, 0x0100, 0x0D, true, false)]
    [InlineData(0, 1, 0x0100, 0x43, true, false)]
    [InlineData(0, 1, 0x0100, 0x41, true, false)]
    [InlineData(0, 1, 0x0100, 0x74, false, false)]
    [InlineData(0, 1, 0x0100, 0x74, true, true)]
    public void UnrelatedMessagesModifiersAndOtherWindowsAreNotConsumed(
        int hookCode,
        int removalAction,
        int message,
        int virtualKey,
        bool isActiveOwnerTarget,
        bool modifiersPressed)
    {
        var calls = 0;
        var handled = GenesisGuiNextMainWindowKeyboardNavigation.TryHandleRefreshKeyDown(
            hookCode,
            (nuint)removalAction,
            (uint)message,
            (nuint)virtualKey,
            0,
            isActiveOwnerTarget,
            modifiersPressed,
            () => { calls++; return true; });

        Assert.False(handled);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisteredF5IsConsumedEvenWhenSharedRefreshRejectsIt(bool accepted)
    {
        var calls = 0;
        var childDispatches = 0;
        var handled = PressF5(() => { calls++; return accepted; });
        if (!handled)
        {
            childDispatches++;
        }

        Assert.True(handled);
        Assert.Equal(1, calls);
        Assert.Equal(0, childDispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBridgeLeavesF5ForExistingChildHandler(bool heldKey)
    {
        var childDispatches = 0;
        var handled = PressF5(
            requestRefresh: null,
            keyData: heldKey ? (nint)(1L << 30) : 0);
        if (!handled)
        {
            childDispatches++;
        }

        Assert.False(handled);
        Assert.Equal(1, childDispatches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnregisteredOwnerHasNoRefreshRequest(int owner)
    {
        var request = GenesisChannelRefreshButtonBridge.FindRefreshRequestForOwner((nint)owner);
        Assert.Null(request);
        Assert.False(PressF5(request));
    }

    [Fact]
    public void HeldF5IsConsumedWithoutDispatchingAnotherRefresh()
    {
        var calls = 0;
        Assert.True(PressF5(() => { calls++; return true; }, (nint)(1L << 30)));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task F5AndButtonSharePendingRefreshAndRearmWithoutQueuingDuplicates(bool keyboardFirst)
    {
        var catalog = new BlockingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(catalog, viewModel);
        var state = new GenesisChannelRefreshButtonBridge.RefreshState(
            (nint)1,
            (nint)2,
            new GenesisMainWindowInteraction(viewModel, session));

        try
        {
            Assert.True(keyboardFirst ? PressF5(state.RequestRefresh) : state.RequestRefresh());
            await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(state.RequestRefresh());
            for (var index = 0; index < 8; index++)
            {
                Assert.True(PressF5(state.RequestRefresh));
            }

            Assert.Equal(1, catalog.ReadCount);
            catalog.Release.TrySetResult(true);
            await state.PendingRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(PressF5(state.RequestRefresh));
            await state.PendingRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, catalog.ReadCount);
            // A final explicit refresh drains the session queue before asserting
            // its count, so shutdown cancellation cannot hide queued duplicates.
            Assert.Equal(
                GenesisChannelCatalogRefreshResult.Completed,
                await session.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(3, catalog.ReadCount);
        }
        finally
        {
            catalog.Release.TrySetResult(true);
            await session.DisposeAsync();
        }

        Assert.Equal(3, catalog.ReadCount);
    }

    [Fact]
    public void MainWindowHookRoutesOwnerAndChildrenThroughSharedRefreshBeforeChildDispatch()
    {
        var source = ReadSource("GenesisGuiNextMainWindowKeyboardNavigation.cs");
        Assert.Contains("TryHandleRefreshKeyDown(", source, StringComparison.Ordinal);
        Assert.Contains("IsMessageForOwner(message.Window) &&", source, StringComparison.Ordinal);
        Assert.Contains("window == _owner || IsChild(_owner, window)", source, StringComparison.Ordinal);
        Assert.Contains("IsWindowEnabled(_owner) &&", source, StringComparison.Ordinal);
        Assert.Contains("GetForegroundWindow() == _owner", source, StringComparison.Ordinal);
        Assert.Contains("IsKeyPressed(VirtualKeyShift)", source, StringComparison.Ordinal);
        Assert.Contains("IsKeyPressed(VirtualKeyControl)", source, StringComparison.Ordinal);
        Assert.Contains("IsKeyPressed(VirtualKeyAlt)", source, StringComparison.Ordinal);
        Assert.Contains(
            "GenesisChannelRefreshButtonBridge.FindRefreshRequestForOwner(_owner)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("if (refreshHandled ||", source, StringComparison.Ordinal);
        Assert.Contains("message.Message = WindowMessageNull;", source, StringComparison.Ordinal);
        Assert.Contains("CallNextHookEx(", source, StringComparison.Ordinal);
    }

    private static bool PressF5(Func<bool>? requestRefresh, nint keyData = 0) =>
        GenesisGuiNextMainWindowKeyboardNavigation.TryHandleRefreshKeyDown(
            0, 1, 0x0100, 0x74, keyData,
            isActiveOwnerTarget: true,
            modifiersPressed: false,
            requestRefresh);

    private static string ReadSource(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "Nova.App", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException(name);
    }

    private sealed class BlockingCatalog : IGenesisChannelCatalog
    {
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return GenesisChannelCatalogSnapshot.Empty;
        }
    }
}
