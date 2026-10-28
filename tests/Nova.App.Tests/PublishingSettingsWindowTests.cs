namespace PeerCast.Nova.App.Tests;

public sealed class PublishingSettingsWindowTests
{
    [Fact]
    public async Task SettingsCommandNotificationsSurviveStableConnectionSuppression()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        await using var composition = NovaGuiNextPublishingComposition.Create(SystemNovaObsPublishingRuntimeFactory.Instance, settings: settings);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection())
        { SelectedTab = GenesisGuiNextTab.Publishing };
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        var connection = tabs.PublishingConnection;
        var connectionNotices = 0;
        var commandNotices = 0;
        tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GenesisGuiNextTabsViewModel.PublishingConnection)) Interlocked.Increment(ref connectionNotices);
            if (e.PropertyName == nameof(GenesisGuiNextTabsViewModel.PublishingCommands)) Interlocked.Increment(ref commandNotices);
        };
        await tabs.RefreshPublishingStatusAsync();
        await tabs.RefreshPublishingStatusAsync();
        Assert.Equal(0, connectionNotices);
        using (var editor = new PublishingSettingsEditor(settings))
        {
            editor.Edit(editor.Input with { ChannelName = "next settings" });
            Assert.False(tabs.PublishingCommands.CanStart);
            Assert.True(commandNotices > 0);
            var draftNotices = commandNotices;
            await editor.SaveAsync();
            await tabs.RefreshPublishingStatusAsync();
            Assert.True(commandNotices > draftNotices);
            Assert.True(tabs.PublishingCommands.CanStart);
            Assert.Equal(1, settings.Saved.SettingsRevision);
            Assert.Null(settings.Applied);
            Assert.Equal("next settings", settings.Saved.Values.ChannelName);
        }
        Assert.Equal(connection, tabs.PublishingConnection);
        Assert.Equal(0, connectionNotices);
        await tabs.DisposeAsync();
        var closedNotices = commandNotices;
        await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values));
        Assert.Equal(closedNotices, commandNotices);
    }

    [Fact]
    public async Task OneBorrowedModelessWindowActivatesReopensAndClosesWithMainLifetime()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var windows = new List<Window>();
        using var host = new GenesisGuiNextPublishingSettingsWindowHost((nint)123, settings, () => false, (owner, borrowed, _) =>
        {
            Assert.Equal((nint)123, owner);
            Assert.Same(settings, borrowed);
            var window = new Window();
            windows.Add(window);
            return window;
        });
        Assert.True(host.Show());
        Assert.True(host.Show());
        Assert.Equal(1, Assert.Single(windows).Activations);
        Assert.Null(settings.Applied);
        windows[0].IsOpen = false;
        Assert.True(host.Show());
        Assert.Equal(2, windows.Count);
        Assert.Equal(1, windows[0].Disposals);
        host.Dispose();
        Assert.Equal(1, windows[1].Disposals);
        Assert.Throws<ObjectDisposedException>(() => host.Show());
        Assert.True(settings.CanStart); // Child host never owns/disposes the root controller.
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void ShortViewportCanReachAllContentAndClampAfterResize(int dpi)
    {
        var content = GenesisGuiNextPublishingSettingsWindow.Scale(PublishingSettingsWindowLayout.ContentHeight, (uint)dpi);
        var page = GenesisGuiNextPublishingSettingsWindow.Scale(300, (uint)dpi);
        var last = PublishingSettingsWindowLayout.ClampScroll(int.MaxValue, page, (uint)dpi);
        Assert.Equal(content - page, last);
        Assert.Equal(0, PublishingSettingsWindowLayout.ClampScroll(last, content + 1, (uint)dpi));
        Assert.Equal(0, PublishingSettingsWindowLayout.ClampScroll(-1, page, (uint)dpi));
    }

    [Fact]
    public async Task SettingsChangesRefreshBorrowedStartStateAndTabDisposalUnsubscribes()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        await using var composition = NovaGuiNextPublishingComposition.Create(SystemNovaObsPublishingRuntimeFactory.Instance, settings: settings);
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        Assert.Same(settings, tabs.PublishingSettings);
        Assert.True(tabs.PublishingCommands.CanStart);
        using var editor = new PublishingSettingsEditor(settings);
        editor.Edit(editor.Input with { ChannelName = "draft" });
        await tabs.RefreshPublishingStatusAsync();
        Assert.False(tabs.PublishingCommands.CanStart);
        Assert.Contains("保存またはキャンセル", tabs.PublishingCommands.DiagnosticText);
        editor.Dispose();
        await tabs.RefreshPublishingStatusAsync();
        Assert.True(tabs.PublishingCommands.CanStart);
        await tabs.DisposeAsync();
        Assert.Null(tabs.PublishingSettings);
        await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values));
    }

    private sealed class Window : IGenesisPublishingSettingsWindow
    {
        public bool IsOpen { get; set; } = true;
        internal int Activations, Disposals;
        public void Activate() => Activations++;
        public void Dispose() { Disposals++; IsOpen = false; }
    }
}
