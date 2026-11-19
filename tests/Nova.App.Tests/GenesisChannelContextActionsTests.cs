namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelContextActionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/recordings")]
    [InlineData("https://user:password@example.test/")]
    [InlineData("https://example.test/\r\n")]
    [InlineData("https://example.test/%0a")]
    [InlineData("https://example.test/a b")]
    [InlineData("https://example.test\\evil")]
    public void UnsafeOrMissingContactIsRejected(string? value) =>
        Assert.Null(GenesisContactUrl.Normalize(value));

    [Fact]
    public void ContactIsBoundedAndCanonicalized()
    {
        Assert.Equal("https://example.test/contact?a=1&b=2",
            GenesisContactUrl.Normalize(" HTTPS://example.test/contact?a=1&b=2 "));
        Assert.Null(GenesisContactUrl.Normalize("https://example.test/" + new string('a', 2048)));
    }

    [Fact]
    public void ParserDecodesContactAndSanitizerAndGuiPreserveIt()
    {
        var fields = new string[18];
        fields[0] = "  Station  ";
        fields[1] = "0123456789ABCDEF0123456789ABCDEF";
        fields[2] = "203.0.113.1:7144";
        fields[3] = "https://example.test/contact?a=1&amp;b=2";
        var parsed = PeerCastIndexChannelCatalogParser.Parse(string.Join("<>", fields),
            new Uri("pcp://tracker.example:7144/"));
        var snapshot = GenesisChannelCatalogSanitizer.Sanitize(parsed);
        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("https://example.test/contact?a=1&b=2", channel.ContactUrl);
        Assert.NotEqual(channel.Source, channel.ContactUrl);

        var favorites = new GenesisFavoriteChannelCollection([GenesisFavoriteChannel.FromChannel(channel)]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites);
        tabs.ApplyChannelViews(GenesisChannelViewProjectionSnapshot.FromCatalog(snapshot));
        foreach (var tab in new[] { GenesisGuiNextTab.Channels, GenesisGuiNextTab.Favorites })
        {
            tabs.SelectedTab = tab;
            var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
            Assert.Equal(channel.ContactUrl, row.InteractiveChannel!.ContactUrl);
        }

        var invalid = GenesisChannelCatalogSanitizer.Sanitize(new([
            channel with { ContactUrl = "javascript:alert(1)" },
        ]));
        Assert.Null(Assert.Single(invalid.Channels).ContactUrl);
    }

    [Fact]
    public async Task ContactCommandsUseCapturedRowAndIgnoreLaterSelection()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var first = new GenesisChannelItem("First", "pcp://tracker/first") { ContactUrl = "https://first.test/contact" };
        var second = new GenesisChannelItem("Second", "pcp://tracker/second") { ContactUrl = "https://second.test/contact" };
        viewModel.ReplaceChannels([first, second]);
        var target = GenesisChannelContextActions.Capture(viewModel, new(first, first));
        viewModel.SelectedChannel = second;
        var effects = new Effects();
        await GenesisChannelContextActions.ExecuteAsync(viewModel, target, GenesisChannelContextMenuCommand.OpenContact, effects);
        await GenesisChannelContextActions.ExecuteAsync(viewModel, target, GenesisChannelContextMenuCommand.CopyContact, effects);
        Assert.Equal(first.ContactUrl, effects.OpenedContact);
        Assert.Equal(first.ContactUrl, effects.CopiedContact);

        var offline = GenesisChannelContextActions.Capture(viewModel, new(first, null));
        Assert.Null(offline.ContactUrl);
        Assert.False(offline.CanRecord);
        effects.OpenedContact = null;
        await GenesisChannelContextActions.ExecuteAsync(viewModel, offline, GenesisChannelContextMenuCommand.OpenContact, effects);
        Assert.Null(effects.OpenedContact);
    }

    [Fact]
    public async Task DetailsCopyUsesCapturedFullProjectionAndBoundsClipboardFailure()
    {
        var model = new GenesisMainViewModel(new FakePlaybackSessionController());
        var first = new GenesisChannelItem("First", "pcp://first", new string('長', 1000));
        var second = new GenesisChannelItem("Second", "pcp://second");
        var captured = GenesisChannelContextActions.Capture(model, new(first, first));
        Assert.Null(captured.DetailsText); // Classic's native menu is unchanged.
        var root = Path.Combine(Path.GetTempPath(), "Nova-phase15-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        GenesisChannelContextTarget target;
        try
        {
            var store = new NovaSettingsStore(Path.Combine(root, "settings.json"));
            await store.UpdateAsync(value => value with { GuiMode = PeerCast.Nova.Foundation.Configuration.NovaGuiMode.Modern });
            model.AppearanceSettings = new GenesisAppearanceSettings(store);
            target = GenesisChannelContextActions.Capture(model, new(first, first));
            Assert.Equal(GenesisGuiNextChannelDetailsProjection.FromChannel(first).CopyText, target.DetailsText);
        }
        finally { Directory.Delete(root, recursive: true); }
        model.ReplaceChannels([first, second]); model.SelectedChannel = second;
        var effects = new Effects();
        Assert.True(GenesisChannelContextActions.IsAction(GenesisChannelContextMenuCommand.CopyDetails));
        await GenesisChannelContextActions.ExecuteAsync(model, target, GenesisChannelContextMenuCommand.CopyDetails, effects);
        Assert.Equal(target.DetailsText, effects.CopiedDetails);
        Assert.Contains(first.Description!, effects.CopiedDetails!);
        Assert.DoesNotContain("Second", effects.CopiedDetails!);
        effects.ThrowOnCopyDetails = true;
        await GenesisChannelContextActions.ExecuteAsync(model, target, GenesisChannelContextMenuCommand.CopyDetails, effects);
        Assert.Equal(GenesisGuiNextChannelContextFailure.DetailsClipboardCopy, effects.Failure);
    }

    [Fact]
    public async Task CopyingFavoriteDetailsUsesOnlyCurrentlyObservedMetadata()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var saved = new GenesisChannelItem(
            "Station", "pcp://old.example/channel", Description: "old details", ChannelId: channelId);
        var current = saved with { Source = "pcp://current.example/channel", Description = "current details" };
        var favorites = new GenesisFavoriteChannelCollection([GenesisFavoriteChannel.FromChannel(saved)]);
        var tabs = new GenesisGuiNextTabsViewModel(favorites) { SelectedTab = GenesisGuiNextTab.Favorites };
        var model = new GenesisMainViewModel(new FakePlaybackSessionController());
        var effects = new Effects();
        var root = Directory.CreateTempSubdirectory("Nova-favorite-copy-");
        try
        {
            var store = new NovaSettingsStore(Path.Combine(root.FullName, "settings.json"));
            await store.UpdateAsync(settings => settings with { GuiMode = PeerCast.Nova.Foundation.Configuration.NovaGuiMode.Modern });
            model.AppearanceSettings = new GenesisAppearanceSettings(store);

            var unobserved = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
            var target = GenesisChannelContextActions.Capture(model, unobserved);
            Assert.Empty(target.SourceToCopy);
            await GenesisChannelContextActions.ExecuteAsync(model, target, GenesisChannelContextMenuCommand.CopyDetails, effects);

            var copied = Assert.IsType<string>(effects.CopiedDetails);
            Assert.Contains("現在の詳細は未取得", copied, StringComparison.Ordinal);
            Assert.DoesNotContain(saved.Source, copied, StringComparison.Ordinal);
            Assert.DoesNotContain(channelId, copied, StringComparison.Ordinal);

            var catalog = new GenesisChannelCatalogSnapshot([current]);
            tabs.ApplyChannelViews(GenesisChannelViewProjectionSnapshot.FromCatalog(catalog));
            var observed = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
            target = GenesisChannelContextActions.Capture(model, observed);
            Assert.Equal(current.Source, target.SourceToCopy);
            await GenesisChannelContextActions.ExecuteAsync(model, target, GenesisChannelContextMenuCommand.CopyDetails, effects);

            copied = Assert.IsType<string>(effects.CopiedDetails);
            Assert.Contains(current.Description!, copied, StringComparison.Ordinal);
            Assert.Contains(current.Source, copied, StringComparison.Ordinal);
            Assert.Contains(channelId, copied, StringComparison.Ordinal);
            Assert.DoesNotContain(saved.Source, copied, StringComparison.Ordinal);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task FolderUsesRuntimeSnapshotAndDoesNotCreateMissingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nova-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var current = Path.Combine(root, "current");
            var next = Path.Combine(root, "next");
            var settings = new GenesisRecordingOutputDirectorySettings(new NovaSettingsStore(Path.Combine(root, "settings.json")), current);
            Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(next));
            var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController()) { RecordingOutputSettings = settings };
            var target = GenesisChannelContextActions.Capture(viewModel, new(new("Offline", "pcp://tracker/offline"), null));
            var effects = new Effects();
            await GenesisChannelContextActions.ExecuteAsync(viewModel, target, GenesisChannelContextMenuCommand.OpenRecordingDirectory, effects);
            Assert.Equal(current, effects.ProbedDirectory);
            Assert.Equal(GenesisGuiNextChannelContextFailure.RecordingDirectoryMissing, effects.Failure);
            Assert.Null(effects.OpenedDirectory);
            Assert.False(Directory.Exists(current));
            Assert.False(Directory.Exists(next));
            effects.Exists = true;
            await GenesisChannelContextActions.ExecuteAsync(viewModel, target, GenesisChannelContextMenuCommand.OpenRecordingDirectory, effects);
            Assert.Equal(current, effects.OpenedDirectory);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LaunchFailureIsReportedWithoutEscapingCommand()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem("Station", "pcp://tracker/live") { ContactUrl = "https://example.test/" };
        var target = GenesisChannelContextActions.Capture(viewModel, new(channel, channel));
        var effects = new Effects { ThrowOnOpen = true };
        await GenesisChannelContextActions.ExecuteAsync(viewModel, target, GenesisChannelContextMenuCommand.OpenContact, effects);
        Assert.Equal(GenesisGuiNextChannelContextFailure.ExternalOpen, effects.Failure);
    }

    internal sealed class Effects : IGenesisChannelContextEffects
    {
        public string? OpenedContact { get; set; }
        public string? CopiedDetails { get; private set; }
        public bool ThrowOnCopyDetails { get; set; }
        public void CopyDetails(string text) { if (ThrowOnCopyDetails) throw new IOException(); CopiedDetails = text; }
        public string? CopiedContact { get; private set; }
        public string? ProbedDirectory { get; private set; }
        public string? OpenedDirectory { get; private set; }
        public GenesisGuiNextChannelContextFailure? Failure { get; private set; }
        public bool Exists { get; set; }
        public bool ThrowOnOpen { get; set; }
        public void OpenContact(string url)
        {
            if (ThrowOnOpen) throw new InvalidOperationException("launch failed");
            OpenedContact = url;
        }
        public void CopyContact(string url) => CopiedContact = url;
        public bool DirectoryExists(string path) { ProbedDirectory = path; return Exists; }
        public void OpenDirectory(string path) => OpenedDirectory = path;
        public void Report(GenesisGuiNextChannelContextFailure failure) => Failure = failure;
    }
}
