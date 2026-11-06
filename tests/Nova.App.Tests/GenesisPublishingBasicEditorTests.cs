using PeerCast.Nova.Network.Publishing;

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

    private static PublishingSettingsInput WanInput(PublishingSettingsInput input) => input with
    {
        WanEnabled = true,
        LanEnabled = false,
        WanInterfaceId = "fixture-nic",
        WanAddress = "10.0.0.10",
        WanPort = "7144",
        AdvertisedAddress = "8.8.8.8",
        AdvertisedPort = "7144",
        AnnounceEnabled = true,
        AnnounceName = "fixture tracker",
        AnnounceEndpoint = "pcp://tracker.invalid:7144",
    };

    [Fact]
    public async Task BasicAndAdvancedShareDraftAndPreserveSavedAppliedAndExtensions()
    {
        var store = new Store();
        store.Snapshot = store.Snapshot with { Extensions = store.Snapshot.Extensions.Add("future", System.Text.Json.JsonSerializer.SerializeToElement("keep")) };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync(); var original = settings.Saved; settings.MarkApplied(original);
        using var page = new GenesisPublishingBasicEditor(settings);
        Assert.True(page.Begin());
        page.Edit("unsaved basic", "genre", "description", "comment", "https://example.invalid", "1700");
        Assert.True(page.EditAdvanced(WanInput(page.Input!)));
        Assert.Equal("unsaved basic", page.Input!.ChannelName);
        page.Edit("final basic", "genre", "description", "comment", "https://example.invalid", "1700");
        Assert.True(page.Input.WanEnabled); Assert.True(page.Input.AnnounceEnabled);
        Assert.False(settings.CanStart); Assert.Equal(0, store.Writes);
        await page.SaveAsync();
        Assert.True(page.Result.Saved); Assert.Equal(1, store.Writes);
        Assert.Equal("final basic", settings.Saved.Values.ChannelName);
        Assert.Equal("10.0.0.10", settings.Saved.Values.Wan.Address);
        Assert.Equal("8.8.8.8", settings.Saved.Values.Wan.AdvertisedAddress);
        Assert.Equal("pcp://tracker.invalid:7144", settings.Saved.Values.Announce.Endpoint);
        Assert.Equal("keep", settings.Saved.Extensions["future"].GetString());
        Assert.Same(original, settings.Applied); Assert.Equal(original.SettingsRevision + 1, settings.Saved.SettingsRevision);
        Assert.True(page.Begin()); page.Edit("cancel both", "", "", "", "", "");
        Assert.True(page.EditAdvanced(page.Input! with { WanEnabled = false, AnnounceEnabled = false }));
        Assert.True(page.Cancel()); Assert.True(settings.Saved.Values.Wan.Enabled); Assert.Equal(1, store.Writes);
    }

    [Theory]
    [InlineData("http://tracker.invalid/index.txt", "7144", "8.8.8.8", "7144")]
    [InlineData("pcp://tracker.invalid:7144", "65536", "8.8.8.8", "7144")]
    [InlineData("pcp://tracker.invalid:7144", "7144", "192.168.1.1", "7144")]
    [InlineData("pcp://tracker.invalid:7144", "7144", "8.8.8.8", "0")]
    public async Task AdvancedValidationCannotSaveInvalidPortsAddressesOrHttpYp(string endpoint, string port, string address, string publicPort)
    {
        var store = new Store(); await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync(); using var page = new GenesisPublishingBasicEditor(settings);
        Assert.True(page.Begin());
        Assert.True(page.EditAdvanced(WanInput(page.Input!) with { AnnounceEndpoint = endpoint, WanPort = port, AdvertisedAddress = address, AdvertisedPort = publicPort }));
        var draft = page.Input;
        await page.SaveAsync();
        Assert.False(page.Result.Saved); Assert.True(page.Editing); Assert.Equal(draft, page.Input);
        Assert.Equal(0, store.Writes); Assert.Equal(PublishingSettingsSnapshot.Default, settings.Saved);
    }

    [Fact]
    public async Task ModeChangesPreservePairsAndYpCannotBeNewlyEnabledWithoutWan()
    {
        var store = new Store(); await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync(); using var page = new GenesisPublishingBasicEditor(settings);
        Assert.True(page.Begin());
        Assert.False(page.EditAdvanced(page.Input! with { AnnounceEnabled = true }));
        Assert.False(page.Input!.AnnounceEnabled); Assert.Equal("AnnounceEnabled", page.Result.Field);
        Assert.True(page.EditAdvanced(WanInput(page.Input) with { LanInterfaceId = "lan-old", LanAddress = "192.168.1.10", LanPort = "7100" }));
        var wan = page.Input;
        Assert.True(page.EditAdvanced(wan! with { WanEnabled = false, LanEnabled = true }));
        Assert.True(page.Input!.AnnounceEnabled); // Preserve user input; invalid combination must fail closed.
        await page.SaveAsync(); Assert.False(page.Result.Saved); Assert.Equal(0, store.Writes);
        Assert.True(page.EditAdvanced(page.Input! with { AnnounceEnabled = false }));
        await page.SaveAsync(); Assert.True(page.Result.Saved);
        Assert.True(settings.Saved.Values.Lan.Enabled); Assert.Equal("lan-old", settings.Saved.Values.Lan.InterfaceId);
        Assert.Equal(wan!.WanAddress, settings.Saved.Values.Wan.Address);
        Assert.Equal(wan.WanInterfaceId, settings.Saved.Values.Wan.InterfaceId);
    }

    [Fact]
    public async Task MissingAmbiguousOrFailedInterfaceReadNeverSelectsAnotherNic()
    {
        var store = new Store { Snapshot = PublishingSettingsSnapshot.Default with { Values = PublishingChannelPreferences.Default with { Lan = new(true, "missing", "10.0.0.40", 7144) } } };
        var interfaces = new Interfaces();
        interfaces.Values = [new("other", "other", true, "10.0.0.10", "255.255.255.0", 24),
            new("a", "ambiguous", true, "10.0.0.20", "255.255.255.0", 24), new("b", "ambiguous", true, "10.0.0.20", "255.255.255.0", 24)];
        await using var settings = new PublishingSettingsController(store, interfaces); await settings.LoadAsync();
        using var page = new GenesisPublishingBasicEditor(settings); Assert.True(page.Begin());
        var choices = page.InterfaceChoices();
        Assert.Equal(2, choices.Count); Assert.DoesNotContain(choices, n => n.Address == "10.0.0.20");
        var missing = Assert.Single(choices, n => n.Id == "missing");
        Assert.False(missing.IsUp); Assert.Contains("未確認", missing.Name);
        Assert.Equal("missing", page.Input!.LanInterfaceId); Assert.Equal("10.0.0.40", page.Input.LanAddress);
        interfaces.Fail = true;
        Assert.False(Assert.Single(page.InterfaceChoices()).IsUp);
        Assert.Equal("missing", page.Input.LanInterfaceId);
    }

    [Fact]
    public async Task AdvancedFailedAndPendingSavesRetainOneSharedEditor()
    {
        var store = new Store { FailSave = true, Block = true };
        await using var settings = new PublishingSettingsController(store); await settings.LoadAsync();
        using var page = new GenesisPublishingBasicEditor(settings); Assert.True(page.Begin());
        Assert.True(page.EditAdvanced(WanInput(page.Input!)));
        var draft = page.Input; var save = page.SaveAsync();
        try
        {
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(page.EditAdvanced(page.Input! with { WanEnabled = false }));
            await page.SaveAsync(); Assert.Equal(1, store.Writes);
            Assert.False(page.Cancel()); Assert.Equal(draft, page.Input);
            Assert.Throws<InvalidOperationException>(() => new PublishingSettingsEditor(settings));
        }
        finally { store.Release.TrySetResult(); await save; }
        Assert.False(page.Result.Saved); Assert.Equal(draft, page.Input); Assert.True(page.Editing);
        Assert.Equal(PublishingSettingsSnapshot.Default, settings.Saved);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void ExpandedCardsKeepConnectionControlsAndActionsWithinMinimumAndLargeWindows(uint dpi)
    {
        foreach (var h in new[] { GenesisMainWindowLayout.MinimumClientHeight, 620, 1080 })
        {
            Assert.True(GenesisMainWindowLayout.TryResolve(GenesisMainWindowLayout.Scale(GenesisMainWindowLayout.MinimumClientWidth, dpi),
                GenesisMainWindowLayout.Scale(h, dpi), dpi, out _, out _, out var list, out var details));
            var layout = new GenesisGuiNextPublishingModernLayout(list.Width, details.Bottom - list.Top, true);
            Assert.True(layout.BasicCard.Right < layout.OptionsCard.Left);
            Assert.True(layout.ObsCard.Bottom < layout.BasicCard.Top);
            Assert.True(layout.OptionsTitle.Right < layout.AdvancedToggle.Left);
            Assert.True(layout.Instructions.Bottom < layout.ObsCard.Bottom);
            Assert.True(layout.AdvancedViewport.Height >= 28);
            Assert.True(layout.AdvancedViewport.Bottom < layout.AdvancedAction.Top);
            Assert.True(layout.AdvancedAction.Bottom < layout.OptionsCard.Bottom);
            Assert.True(layout.OptionsCard.Bottom < layout.Start.Top);
            Assert.True(layout.Basic.Height >= 24);
            foreach (var input in new[] { PublishingSettingsInput.From(PublishingChannelPreferences.Default), WanInput(PublishingSettingsInput.From(PublishingChannelPreferences.Default)) })
            {
                var rows = GenesisPublishingAdvancedLayout.Rows(input);
                for (var i = 0; i < rows.Length; i++)
                {
                    var scroll = GenesisPublishingAdvancedLayout.Reveal(0, layout.AdvancedViewport.Height, i, rows.Length);
                    Assert.InRange(4 + i * 32 - scroll, 0, layout.AdvancedViewport.Height - 26);
                }
            }
        }
    }

    private sealed class Interfaces : IObsPublishLanInterfaces
    {
        internal IReadOnlyList<ObsPublishLanInterface> Values = [];
        internal bool Fail;
        public IReadOnlyList<ObsPublishLanInterface> Read() => Fail ? throw new IOException("fixture") : Values;
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
