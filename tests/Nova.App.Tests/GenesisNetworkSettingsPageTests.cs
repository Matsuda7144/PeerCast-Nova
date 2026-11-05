namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsPageTests
{
    [Fact]
    public async Task ConnectionSavePreservesStartupNullAndZeroAndRejectsDoubleSave()
    {
        var store = new Store { Block = true };
        await using var settings = new GenesisGuiNextConnectionSettings(new(), store);
        var page = new GenesisConnectionSettingsPage(settings);
        page.Open();
        page.Edit(new("17144", true, "0", "", "9", "3", "2"));
        var save = page.BeginSave()!;
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(page.BeginSave());
        page.Edit(page.Input with { ListenPort = "1" });
        Assert.Equal("17144", page.Input.ListenPort);
        store.Release.SetResult();
        await save;
        page.CompleteSave(save);
        Assert.False(page.IsSaving);
        Assert.Equal(7144, settings.Startup.ListenPort);
        Assert.Equal(17144, settings.Saved.ListenPort);
        Assert.Equal(0, settings.Saved.TotalUpstreamIpv4Kbps);
        Assert.Null(settings.Saved.TotalUpstreamIpv6Kbps);
        Assert.True(settings.Saved.UseIpv6);
        Assert.False(page.IsDirty);
    }

    [Theory]
    [InlineData("0", "")]
    [InlineData("65536", "")]
    [InlineData("12a", "")]
    [InlineData("7144", "-1")]
    public async Task InvalidInputPreservesDraftAndSavedValues(string port, string limit)
    {
        var store = new Store();
        await using var settings = new GenesisGuiNextConnectionSettings(new(), store);
        var page = new GenesisConnectionSettingsPage(settings); page.Open();
        page.Edit(page.Input with { ListenPort = port, MaxRelaysTotal = limit });
        var task = page.BeginSave()!; await task; page.CompleteSave(task);
        Assert.True(page.IsError); Assert.Equal(port, page.Input.ListenPort);
        Assert.Equal(GenesisGuiNextConnectionPreferences.Default, settings.Saved);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task ClosedPageDoesNotReceiveLateResultAndReopensAtLatestValue()
    {
        var store = new Store { Block = true };
        await using var settings = new GenesisGuiNextConnectionSettings(new(), store);
        var page = new GenesisConnectionSettingsPage(settings); page.Open();
        page.Edit(page.Input with { ListenPort = "17144" });
        var task = page.BeginSave()!;
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        page.Close(); store.Release.SetResult(); await task; page.CompleteSave(task);
        Assert.False(page.IsOpen); Assert.False(page.IsSaving);
        page.Open(); Assert.Equal("17144", page.Input.ListenPort); Assert.False(page.IsDirty);
        page.Edit(page.Input with { ListenPort = "17145" }); page.Close(); page.Open();
        Assert.Equal("17144", page.Input.ListenPort); Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task FailedPersistenceAndLegacyConflictKeepSavedValues()
    {
        var store = new Store { Fail = true };
        await using var settings = new GenesisGuiNextConnectionSettings(new(), store);
        var page = new GenesisConnectionSettingsPage(settings); page.Open();
        page.Edit(page.Input with { ListenPort = "17144" });
        var task = page.BeginSave()!; await task; page.CompleteSave(task);
        Assert.True(page.IsError); Assert.True(page.IsDirty); Assert.Equal(7144, settings.Saved.ListenPort);
        store.Fail = false;
        Assert.True((await settings.SaveAsync(page.Input with { ListenPort = "17146" })).Saved);
        page.Refresh(); Assert.True(page.Conflict); Assert.Null(page.BeginSave());
        page.Reload(); Assert.Equal("17146", page.Input.ListenPort); Assert.False(page.Conflict);
    }

    [Fact]
    public async Task QueuedLegacySaveCannotBeOverwrittenByStaleIntegratedDraft()
    {
        var store = new Store { Block = true };
        await using var settings = new GenesisGuiNextConnectionSettings(new(), store);
        var baseline = settings.Saved;
        var legacy = settings.SaveAsync(GenesisGuiNextConnectionInput.From(baseline) with { ListenPort = "17145" }).AsTask();
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var integrated = settings.SaveAsync(GenesisGuiNextConnectionInput.From(baseline) with { ListenPort = "17144" }, baseline).AsTask();
        store.Release.SetResult();
        Assert.True((await legacy).Saved); Assert.False((await integrated).Saved);
        Assert.Equal(17145, settings.Saved.ListenPort); Assert.Equal(1, store.Calls);
    }

    private sealed class Store : IGenesisGuiNextConnectionPreferencesStore
    {
        internal bool Block { get; init; }
        internal bool Fail { get; set; }
        internal int Calls { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new GenesisGuiNextConnectionPreferences());
        public async ValueTask SaveAsync(GenesisGuiNextConnectionPreferences preferences, CancellationToken cancellationToken = default)
        {
            Calls++; Entered.TrySetResult();
            if (Block) await Release.Task.WaitAsync(cancellationToken);
            if (Fail) throw new IOException();
        }
    }
}
