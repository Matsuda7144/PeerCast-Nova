namespace PeerCast.Nova.App.Tests;

public sealed partial class GuiNextPublishingGenerationTests
{
    [Fact]
    public async Task DraftImmediatelyDisablesCachedGuiStartEvenWhenRuntimeStatusReadIsBlocked()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var owner = new Owner();
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory(owner), settings: settings);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SetPublishingDispatcher(action => action());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        await tabs.RefreshPublishingStatusAsync();
        Assert.True(tabs.PublishingCommands.CanStart);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.ReadSnapshot = () => { entered.TrySetResult(); release.Wait(); };
        var read = tabs.RefreshPublishingStatusAsync();
        using var editor = new PublishingSettingsEditor(settings);
        try
        {
            await entered.Task.WaitAsync(Timeout);
            editor.Edit(editor.Input with { ChannelName = "dirty" });
            Assert.False(tabs.PublishingCommands.CanStart);
            Assert.False(read.IsCompleted);
        }
        finally { owner.ReadSnapshot = null; release.Set(); await read.WaitAsync(Timeout); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleReplacementRejectsReusedChannelOrSessionAndDisposesCandidateOnce(bool channel)
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var retired = new Owner();
        var candidate = new Owner();
        candidate.Current = channel ? candidate.Current with { ChannelId = retired.Current.ChannelId } :
            candidate.Current with { SessionId = retired.Current.SessionId };
        var factory = new Factory(retired, candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "changed" }));
        await composition.RequestStartAsync();
        Assert.Equal(2, factory.Creates);
        Assert.Equal(0, candidate.Starts);
        Assert.Equal(1, candidate.Disposals);
        Assert.Null(settings.Applied);
        Assert.False(composition.ReadCommands().CanStart);
    }
    [Fact]
    public async Task StaleIdleObservationCannotRetireOwnerStartedByAnotherAdmittedCommand()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var factory = new Factory(new Owner(), new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        composition.CommandObservationReady = () =>
        {
            composition.CommandObservationReady = null;
            composition.RequestStartAsync().GetAwaiter().GetResult();
            settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "next" })).GetAwaiter().GetResult();
        };
        await composition.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        Assert.Equal(1, factory.Owners[0].Starts);
        Assert.Equal(0, factory.Owners[0].Disposals);
        Assert.Equal(0, settings.Applied!.SettingsRevision);
        Assert.Equal(1, settings.Saved.SettingsRevision);
    }
    [Fact]
    public async Task LoadedSettingsPrepareFirstOwnerAndRunningSaveOnlyAffectsExplicitRestart()
    {
        var store = new PublishingSettingsTests.MemoryStore
        { Snapshot = PublishingSettingsSnapshot.Default with { SettingsRevision = 9, Values = PublishingChannelPreferences.Default with { ChannelName = "restored" } } };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var factory = new Factory(new Owner(), new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        Assert.Same(settings.Saved, Assert.Single(factory.SettingsSnapshots));
        await composition.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        var applied = settings.Applied;
        Assert.Equal("restored", applied!.Values.ChannelName);
        Assert.True((await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "next" }))).Saved);
        Assert.Same(applied, settings.Applied);
        Assert.Equal(10, settings.Saved.SettingsRevision);
        await composition.RequestInterruptAsync();
        await composition.RequestStartAsync();
        Assert.Equal(2, factory.Creates);
        Assert.Same(settings.Saved, settings.Applied);
        Assert.Equal("next", factory.SettingsSnapshots[1].Values.ChannelName);
        Assert.All(factory.Owners, owner => Assert.Equal(1, owner.Starts));
    }

    [Fact]
    public async Task SavedBeforeFirstStartReplacesIdleOwnerAfterActualCleanupAndKeepsCapturedRevision()
    {
        var store = new PublishingSettingsTests.MemoryStore();
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var retired = new Owner { BlockDispose = true };
        var candidate = new Owner();
        var factory = new Factory(retired, candidate);
        await using var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        Assert.True((await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "admitted" }))).Saved);
        Assert.Equal(1, factory.Creates); // Saving does not allocate or bind a listener.
        var admitted = settings.Saved;
        var start = composition.RequestStartAsync();
        await retired.DisposeEntered.Task.WaitAsync(Timeout);
        await composition.RequestStartAsync(); // Repeated Start cannot allocate a second candidate.
        Assert.Equal(1, factory.Creates);
        Assert.Equal(0, retired.Starts);
        Assert.True((await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "later save" }))).Saved);
        retired.ReleaseDispose.TrySetResult();
        await start.WaitAsync(Timeout);
        Assert.Equal(1, retired.Disposals);
        Assert.Equal(0, retired.Starts);
        Assert.Equal(1, candidate.Starts);
        Assert.Same(admitted, factory.SettingsSnapshots[1]);
        Assert.Same(admitted, settings.Applied);
        Assert.Equal(2, settings.Saved.SettingsRevision);
        Assert.Equal(2, composition.CurrentGeneration);
        await composition.DisposeAsync();
        Assert.Equal(1, retired.Disposals);
        Assert.Equal(1, candidate.Disposals);
    }

    [Fact]
    public async Task IdleOwnerCannotBeReplacedWhenItsCompletionDoesNotProveShutdown()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var retired = new Owner { ShutdownProven = false };
        var factory = new Factory(retired, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "changed" }));
        await composition.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        Assert.Equal(0, retired.Starts);
        Assert.Null(settings.Applied);
        Assert.False(composition.ReadCommands().CanStart);
        await Assert.ThrowsAsync<InvalidOperationException>(() => composition.DisposeAsync().AsTask());
        Assert.Equal(1, retired.Disposals);
    }

    [Fact]
    public async Task CloseDuringIdleReplacementPreventsFactoryAndStart()
    {
        await using var settings = new PublishingSettingsController(new PublishingSettingsTests.MemoryStore());
        await settings.LoadAsync();
        var retired = new Owner { BlockDispose = true };
        var factory = new Factory(retired, new Owner());
        var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        await settings.SaveAsync(PublishingSettingsInput.From(settings.Saved.Values with { ChannelName = "changed" }));
        var start = composition.RequestStartAsync();
        await retired.DisposeEntered.Task.WaitAsync(Timeout);
        composition.CloseAdmission();
        var close = composition.DisposeAsync().AsTask();
        retired.ReleaseDispose.TrySetResult();
        await Task.WhenAll(start, close).WaitAsync(Timeout);
        Assert.Equal(1, factory.Creates);
        Assert.Equal(0, retired.Starts);
        Assert.Equal(1, retired.Disposals);
        Assert.Null(settings.Applied);
    }

    [Fact]
    public async Task StartRechecksSettingsAfterBorrowedObservationAndDraftDoesNotDisableInterrupt()
    {
        var store = new PublishingSettingsTests.MemoryStore { BlockSave = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var factory = new Factory(new Owner(), new Owner());
        await using var composition = NovaGuiNextPublishingComposition.Create(factory, settings: settings);
        using var editor = new PublishingSettingsEditor(settings);
        composition.CommandObservationReady = () => editor.Edit(editor.Input with { ChannelName = "dirty" });
        await composition.RequestStartAsync();
        Assert.Equal(0, factory.Owners[0].Starts);
        Assert.False(composition.ReadCommands().CanStart);
        composition.CommandObservationReady = null;
        var save = editor.SaveAsync();
        await store.SaveEntered.Task.WaitAsync(Timeout);
        await composition.RequestStartAsync();
        Assert.Equal(1, factory.Creates);
        store.ReleaseSave.TrySetResult();
        await save;
        await composition.RequestStartAsync();
        editor.Edit(editor.Input with { Genre = "future draft" });
        Assert.True(composition.ReadCommands().CanInterrupt);
        await composition.RequestInterruptAsync();
        Assert.False(composition.ReadCommands().CanStart);
        editor.Dispose();
        Assert.True(composition.ReadCommands().CanStart);
    }
}
