using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelListActivationTests
{
    [Fact]
    public async Task WindowsNotificationUsesActualHitWithoutAnyNativeSelection()
    {
        if (!OperatingSystem.IsWindows()) return;
        var common = new CommonControls { Size = 8, Classes = 1 };
        Assert.True(InitCommonControlsEx(ref common));
        var owner = CreateWindowExW(0, "STATIC", "Activation test", 0, 0, 0, 800, 600, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        IAsyncDisposable? bridge = null;
        try
        {
            nint Add(string kind, string name) => CreateWindowExW(0, kind, name,
                kind == "SysListView32" ? 0x50000001u : 0x50000000u, 0, 0, 100, 100, owner, 0, 0, 0);
            _ = Add("STATIC", "Source");
            _ = Add("EDIT", "");
            _ = Add("BUTTON", "Play");
            _ = Add("BUTTON", "Stop");
            var list = Add("SysListView32", "");
            _ = Add("STATIC", "Status");
            Assert.True(GenesisMainWindowNativeSurface.ReplaceColumns(list, 0,
                GenesisGuiNextRecordingListPresentation.Columns(GenesisGuiNextTab.Channels,
                    GenesisGuiNextRecordingPageMode.ActiveRecordings)));
            var controller = new CountingController();
            var model = new GenesisMainViewModel(controller);
            var first = Channel("first");
            var hit = Channel("hit");
            model.ReplaceChannels([first, hit]);
            bridge = SystemGenesisMainWindowInteractionBridgeFactory.Instance.Create(owner, model);
            Assert.NotNull(bridge);
            Assert.Same(hit, GenesisChannelListRenderedChannels.Find(list, 1));
            model.SelectedChannel = first;
            var notification = new ItemActivation { Header = new() { From = list, Code = unchecked((uint)-3) }, Item = -1 };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ItemActivation>());
            try
            {
                void Notify()
                {
                    Marshal.StructureToPtr(notification, memory, false);
                    _ = SendMessageW(GetParent(list), 0x004E, 0, memory);
                }
                Notify(); // Blank double-click must not play the managed selection.
                notification.Header.Code = unchecked((uint)-2);
                notification.Item = 1;
                Notify(); // NM_CLICK must not play.
                Assert.Equal(0, controller.Requests);
                notification.Header.Code = unchecked((uint)-3);
                Notify();
                Assert.Equal(1, controller.Requests);
                Assert.Equal(hit.Source, model.CurrentSource);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally
        {
            if (bridge is not null) await bridge.DisposeAsync();
            _ = DestroyWindow(owner);
        }
    }

    [Fact]
    public async Task DoubleClickUsesHitRowInsteadOfPreviousSelectionAndPlaysOnce()
    {
        var controller = new CountingController();
        var model = new GenesisMainViewModel(controller);
        var first = Channel("first");
        var hit = Channel("hit");
        model.ReplaceChannels([first, hit]);
        model.SelectedChannel = first;
        var interaction = new GenesisMainWindowInteraction(model);
        GenesisGuiNextNativeTabRow[] rows = [new(first, first), new(hit, hit)];

        Assert.True(await GenesisChannelListActivation.ActivateAsync(-3, 1, rows, interaction, true));
        Assert.Equal(1, controller.Requests);
        Assert.Same(hit, model.SelectedChannel);
        Assert.Equal(hit.Source, model.CurrentSource);
        Assert.False(await GenesisChannelListActivation.ActivateAsync(-3, 1, rows, interaction, true));
        Assert.Equal(1, controller.Requests);
    }

    [Theory]
    [InlineData(-2, 0, true)] // NM_CLICK only selects; never activates.
    [InlineData(-101, 0, true)]
    [InlineData(-3, -1, true)] // Empty space despite a valid previous selection.
    [InlineData(-3, 1, true)]
    [InlineData(-3, 0, false)] // Native rows are rebuilding / command unavailable.
    public async Task NonActivationNotificationsAndBlankHitsNeverPlay(int code, int item, bool available)
    {
        var controller = new CountingController();
        var model = new GenesisMainViewModel(controller);
        var channel = Channel("selected");
        model.ReplaceChannels([channel]);
        var interaction = new GenesisMainWindowInteraction(model);
        Assert.True(interaction.SelectChannel(channel));
        Assert.False(await GenesisChannelListActivation.ActivateAsync(code, item,
            [new(channel, channel)], interaction, available));
        Assert.Equal(0, controller.Requests);
        Assert.Same(channel, model.SelectedChannel);
    }

    [Fact]
    public async Task NoticeAndRemovedOrReplacedIdentityNeverPlayPreviousSelection()
    {
        var controller = new CountingController();
        var model = new GenesisMainViewModel(controller);
        var channel = Channel("old") with { ChannelId = new string('1', 32) };
        var replacement = channel with { Name = "other identity", ChannelId = new string('2', 32) };
        var interaction = new GenesisMainWindowInteraction(model);
        model.ReplaceChannels([replacement]);
        model.SelectedChannel = replacement;
        Assert.False(await GenesisChannelListActivation.ActivateAsync(-3, 0,
            [new(channel, null)], interaction, true));
        Assert.False(await GenesisChannelListActivation.ActivateAsync(-3, 0,
            [new(channel, channel)], interaction, true));
        model.ReplaceChannels([replacement, channel]); // Same source, conflicting identity.
        Assert.False(await GenesisChannelListActivation.ActivateAsync(-3, 0,
            [new(channel, channel)], interaction, true));
        model.ReplaceChannels([]);
        Assert.False(await GenesisChannelListActivation.ActivateAsync(-3, 0,
            [new(channel, channel)], interaction, true));
        Assert.Equal(0, controller.Requests);
    }

    [Fact]
    public async Task RefreshedAndReorderedRowsResolveLatestCatalogObject()
    {
        var controller = new CountingController();
        var model = new GenesisMainViewModel(controller);
        var first = Channel("first");
        var hit = Channel("hit") with { ChannelId = new string('1', 32) };
        model.ReplaceChannels([first, hit]);
        model.SelectedChannel = first;
        var refreshed = hit with { Name = "refreshed metadata" };
        model.ReplaceChannels([refreshed, first]);
        var interaction = new GenesisMainWindowInteraction(model);
        // Native display order differs from both the original and current model.
        Assert.True(await GenesisChannelListActivation.ActivateAsync(-3, 1,
            [new(first, first), new(hit, hit)], interaction, true));
        Assert.Same(refreshed, model.SelectedChannel);
        Assert.Equal(refreshed.Source, model.CurrentSource);
        Assert.Equal(1, controller.Requests);
    }

    private static GenesisChannelItem Channel(string name) => new(name, $"pcp://example/{name}");

    [StructLayout(LayoutKind.Sequential)] private struct CommonControls { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NotificationHeader
    {
        public nint From;
        public nuint Id;
        public uint Code;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ItemActivation
    {
        public NotificationHeader Header;
        public int Item, SubItem;
        public uint NewState, OldState, Changed;
        public int X, Y;
        public nint Parameter;
        public uint Keys;
    }
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint extended, string kind, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    private sealed class CountingController : IPlaybackSessionController
    {
        private readonly FakePlaybackSessionController _inner = new();
        internal int Requests { get; private set; }
        public PlaybackSessionSnapshot Snapshot => _inner.Snapshot;
        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(string source, CancellationToken cancellationToken = default)
        {
            Requests++;
            return _inner.PlayAsync(source, cancellationToken);
        }
        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(CancellationToken cancellationToken = default) =>
            _inner.StopAsync(cancellationToken);
    }
}
