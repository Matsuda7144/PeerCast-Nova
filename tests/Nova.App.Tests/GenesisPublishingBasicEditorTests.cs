namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPublishingBasicEditorTests
{
    [Fact]
    public async Task BasicSaveUsesExistingEditorAndPreservesAdvancedSavedAndAppliedValues()
    {
        var store = new Store();
        store.Snapshot = store.Snapshot with { Values = store.Snapshot.Values with { Announce = new(false, "retained", ""), Lan = new(false, "saved-nic", "", 0) } };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var original = settings.Saved;
        settings.MarkApplied(original);
        using var page = new GenesisPublishingBasicEditor(settings);
        Assert.Null(page.Input); Assert.True(settings.CanStart);
        Assert.True(page.Begin());
        Assert.Throws<InvalidOperationException>(() => new PublishingSettingsEditor(settings));
        page.Edit("  架空チャンネル  ", "ジャンル", "概要", "コメント", "https://example.invalid/contact", "1234");
        Assert.True(page.Dirty); Assert.False(settings.CanStart);
        Assert.Same(original, settings.Saved);
        await page.SaveAsync();
        Assert.True(page.Result.Saved); Assert.False(page.Editing); Assert.True(settings.CanStart);
        Assert.Equal("架空チャンネル", settings.Saved.Values.ChannelName);
        Assert.Equal("ジャンル", settings.Saved.Values.Genre); Assert.Equal("概要", settings.Saved.Values.Description);
        Assert.Equal("コメント", settings.Saved.Values.Comment); Assert.Equal("https://example.invalid/contact", settings.Saved.Values.ContactUrl);
        Assert.Equal(1234, settings.Saved.Values.BitrateKbps);
        Assert.Equal(original.Values.Lan, settings.Saved.Values.Lan); Assert.Equal(original.Values.Announce, settings.Saved.Values.Announce);
        Assert.Equal(original.SettingsRevision + 1, settings.Saved.SettingsRevision);
        Assert.Same(original, settings.Applied); // No runtime callback or Hot Apply.
        Assert.Same(store.Snapshot, settings.Saved);
    }

    [Theory]
    [InlineData("", "", "ChannelName")]
    [InlineData("valid", "0", "BitrateKbps")]
    [InlineData("valid", "2147483648", "BitrateKbps")]
    public async Task ValidationRetainsDraftAndSavedRevision(string name, string bitrate, string field)
    {
        var store = new Store();
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var original = settings.Saved;
        using var page = new GenesisPublishingBasicEditor(settings);
        Assert.True(page.Begin());
        page.Edit(name, "", "", "", "", bitrate);
        await page.SaveAsync();
        Assert.False(page.Result.Saved); Assert.Equal(field, page.Result.Field);
        Assert.True(page.Editing); Assert.Equal(name, page.Input!.ChannelName);
        Assert.Same(original, settings.Saved); Assert.Equal(0, store.Writes);
        Assert.True(page.Cancel()); Assert.False(page.Editing); Assert.True(settings.CanStart);
    }

    [Fact]
    public async Task FailedSaveRetainsAllInputAndCancelReleasesEditorWithoutWriting()
    {
        var store = new Store { FailSave = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        using var page = new GenesisPublishingBasicEditor(settings);
        Assert.True(page.Begin());
        var text = new string('長', 2000);
        page.Edit("draft", "g", text, "c", "https://example.invalid/?token=fake", "");
        await page.SaveAsync();
        Assert.False(page.Result.Saved); Assert.True(page.Editing);
        Assert.Equal(text, page.Input!.Description); Assert.Equal("", page.Input.BitrateKbps);
        Assert.Equal(PublishingSettingsSnapshot.Default, settings.Saved);
        Assert.True(page.Cancel());
        using var legacy = new PublishingSettingsEditor(settings);
        Assert.Equal(PublishingSettingsInput.From(settings.Saved.Values), legacy.Input);
    }

    [Fact]
    public async Task LegacyEditorAndSavedRevisionsRemainExclusiveAndFresh()
    {
        var store = new Store();
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        using var page = new GenesisPublishingBasicEditor(settings);
        using (var legacy = new PublishingSettingsEditor(settings))
        {
            Assert.False(page.Begin()); Assert.Contains("別の画面", page.Result.Message);
            legacy.Edit(legacy.Input with { ChannelName = "legacy revision" });
            await legacy.SaveAsync();
        }
        Assert.True(page.Begin()); Assert.Equal("legacy revision", page.Input!.ChannelName);
        page.Edit("discard", "", "", "", "", "");
        Assert.True(page.Cancel()); Assert.True(page.Begin());
        Assert.Equal("legacy revision", page.Input!.ChannelName); Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task LoadFailureRequiresExistingExplicitRecoveryAndNeverWritesDefaults()
    {
        var store = new Store { FailLoad = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        using var page = new GenesisPublishingBasicEditor(settings);
        Assert.False(page.Begin()); Assert.Contains("復旧", page.Result.Message);
        await page.SaveAsync();
        Assert.False(settings.CanStart); Assert.Equal(0, store.Writes);
        using (var recovery = new PublishingSettingsEditor(settings)) await recovery.SaveAsync();
        Assert.True(page.Begin()); Assert.False(settings.LoadFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSaveRejectsDuplicateOperationsAndRemainsRootOwnedOnRelease(bool releasePage)
    {
        var store = new Store { Block = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        using var page = new GenesisPublishingBasicEditor(settings);
        Assert.True(page.Begin()); page.Edit("pending", "", "", "", "", "");
        var save = page.SaveAsync();
        try
        {
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(page.Saving); Assert.False(page.Cancel()); Assert.False(page.Begin()); Assert.False(settings.CanStart);
            await page.SaveAsync(); page.Edit("must not replace", "", "", "", "", "");
            Assert.Equal("pending", page.Input!.ChannelName); Assert.Equal(1, store.Writes);
            if (releasePage)
            {
                page.Dispose();
                Assert.False(page.Editing);
                var shutdown = settings.DisposeAsync().AsTask();
                Assert.False(shutdown.IsCompleted);
                store.Release.TrySetResult();
                await shutdown;
            }
        }
        finally { store.Release.TrySetResult(); await save; }
        Assert.Equal("pending", settings.Saved.Values.ChannelName);
        Assert.Null(settings.Saved.Values.BitrateKbps);
        Assert.False(page.Editing); Assert.False(page.Saving);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void CompactViewportCanRevealEveryFieldWithoutOverlappingFixedActions(uint dpi)
    {
        foreach (var height in new[] { 32, 80, 160, 300 })
        {
            for (var i = 0; i < 6; i++)
            {
                var scroll = GenesisPublishingBasicLayout.Reveal(0, height, i);
                var layout = new GenesisPublishingBasicLayout(260, height, scroll);
                var input = layout.Input(i);
                Assert.InRange(input.Top, 0, height - input.Height);
                Assert.True(layout.Label(i).Right <= input.Left);
                Assert.True(input.Right <= layout.Width);
                Assert.True(GenesisMainWindowLayout.Scale(input.Bottom, dpi) <= GenesisMainWindowLayout.Scale(height, dpi));
            }
        }
        Assert.False(GenesisGuiNextPublishingModernLayout.Applies(false, GenesisGuiNextTab.Publishing));
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void MainWindowMinimumRetainsEditorViewportActionsAndAdvancedSummary(uint dpi)
    {
        foreach (var logicalHeight in new[] { GenesisMainWindowLayout.MinimumClientHeight, 620, 1080 })
        {
            Assert.True(GenesisMainWindowLayout.TryResolve(
                GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientWidth, dpi),
                GenesisMainWindowLayout.Scale(logicalHeight, dpi), dpi, out _, out _, out var list, out var details));
            var layout = new GenesisGuiNextPublishingModernLayout(list.Width, details.Bottom - list.Top);
            Assert.True(layout.BasicTitle.Bottom <= layout.Basic.Top);
            Assert.True(layout.Basic.Height - 36 >= 26); // viewport separate from fixed 26px Save/Cancel
            Assert.True(layout.Options.Height >= 24);
            Assert.True(layout.Feedback.Bottom <= layout.StatusCard.Bottom);
            Assert.True(layout.StatusCard.Bottom < layout.BasicCard.Top);
            Assert.True(layout.BasicCard.Bottom < layout.OptionsCard.Top);
            Assert.True(layout.OptionsCard.Bottom < layout.Start.Top);
            Assert.True(layout.Instructions.Height >= 26);
        }
    }

    private sealed class Store : IPublishingPreferencesStore
    {
        internal PublishingSettingsSnapshot Snapshot = PublishingSettingsSnapshot.Default;
        internal bool FailSave, FailLoad, Block;
        internal int Writes;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PublishingSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            FailLoad ? ValueTask.FromException<PublishingSettingsSnapshot>(new IOException("fixture")) : ValueTask.FromResult(Snapshot);
        public async ValueTask SaveAsync(PublishingSettingsSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Writes++; Entered.TrySetResult();
            if (Block) await Release.Task.WaitAsync(cancellationToken);
            if (FailSave) throw new IOException("fixture");
            Snapshot = snapshot;
        }
    }
}
