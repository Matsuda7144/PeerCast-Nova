using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGeneralSettingsPageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NovaGeneral-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private NovaSettingsStore Store => new(SettingsPath);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void OnlyModernWithANativePageUsesIntegratedSettings(bool modern, bool available, bool expected) =>
        Assert.Equal(expected, GenesisGeneralSettingsPage.UseIntegrated(modern, available));

    [Fact]
    public void OpenSelectCancelCloseAndReleaseNeverWriteSettings()
    {
        var appearance = new GenesisAppearanceSettings(Store);
        var page = new GenesisGeneralSettingsPage(appearance);
        Assert.True(page.Open());
        page.Select(NovaGuiMode.Modern);
        Assert.True(page.IsDirty);
        Assert.True(page.Open()); // Repeated navigation to Settings preserves the draft.
        Assert.Equal(NovaGuiMode.Modern, page.Draft);
        page.Cancel();
        Assert.Equal(NovaGuiMode.Classic, page.Draft);
        Assert.False(page.IsDirty);
        page.Select(NovaGuiMode.Modern);
        page.Close();
        Assert.False(page.IsOpen);
        Assert.False(page.Save());
        Assert.True(page.Open());
        Assert.Equal(NovaGuiMode.Classic, page.Draft);
        page.Release();
        page.Select(NovaGuiMode.Modern);
        Assert.False(page.Open());
        Assert.False(page.Save());
        Assert.False(File.Exists(SettingsPath));
        Assert.Equal(NovaGuiMode.Classic, appearance.ConfiguredMode);
    }

    [Theory]
    [InlineData(NovaGuiMode.Classic, NovaGuiMode.Modern)]
    [InlineData(NovaGuiMode.Modern, NovaGuiMode.Classic)]
    public async Task SaveChangesOnlyNextStartupAppearanceAndPreservesOtherSettings(NovaGuiMode runtime, NovaGuiMode next)
    {
        await Store.SaveAsync(new() { GuiMode = runtime, ListenPort = 17144, NodeName = "keep" });
        var appearance = new GenesisAppearanceSettings(Store);
        var page = new GenesisGeneralSettingsPage(appearance);
        Assert.True(page.Open());
        page.Select(next);
        Assert.Equal(runtime, page.RuntimeMode);
        Assert.Equal(runtime, page.ConfiguredMode);
        Assert.True(page.IsDirty);
        Assert.True(page.Save());
        Assert.False(page.IsDirty);
        Assert.False(page.IsError);
        Assert.Equal(runtime, page.RuntimeMode);
        Assert.Equal(next, page.ConfiguredMode);
        var saved = Store.Load();
        Assert.Equal(next, saved.GuiMode);
        Assert.Equal(17144, saved.ListenPort);
        Assert.Equal("keep", saved.NodeName);
        Assert.Equal(next, new GenesisAppearanceSettings(Store).CurrentRuntimeMode);
        page.Close();
        Assert.True(page.Open());
        Assert.Equal(next, page.Draft);
        Assert.Equal(runtime, page.RuntimeMode);
    }

    [Fact]
    public async Task SaveFailureKeepsConfiguredValueAndDocumentAndAllowsCancelOrRetry()
    {
        await Store.SaveAsync(new() { GuiMode = NovaGuiMode.Modern });
        var before = File.ReadAllText(SettingsPath);
        var fail = false;
        var appearance = new GenesisAppearanceSettings(new(SettingsPath, () => { if (fail) throw new IOException("test"); }));
        var page = new GenesisGeneralSettingsPage(appearance);
        Assert.True(page.Open());
        page.Select(NovaGuiMode.Classic);
        fail = true;
        Assert.False(page.Save());
        Assert.True(page.IsError);
        Assert.True(page.IsDirty);
        Assert.Contains("保存できません", page.Message);
        Assert.Equal(NovaGuiMode.Modern, page.ConfiguredMode);
        Assert.Equal(before, File.ReadAllText(SettingsPath));
        page.Cancel();
        Assert.False(page.IsDirty);
        page.Select(NovaGuiMode.Classic);
        fail = false;
        Assert.True(page.Save());
        Assert.False(page.IsError);
        Assert.Equal(NovaGuiMode.Modern, page.RuntimeMode);
    }

    [Fact]
    public async Task LegacyAppearanceChangesShareTheSameConfiguredValue()
    {
        await Store.SaveAsync(new() { GuiMode = NovaGuiMode.Modern });
        var appearance = new GenesisAppearanceSettings(Store);
        var page = new GenesisGeneralSettingsPage(appearance);
        page.Open();
        Assert.True(appearance.Save(NovaGuiMode.Classic)); // The existing modeless appearance window uses this object.
        Assert.Equal(NovaGuiMode.Classic, page.ConfiguredMode);
        page.Refresh();
        Assert.Equal(NovaGuiMode.Classic, page.Draft);
        Assert.False(page.IsDirty);
        page.Cancel();
        Assert.Equal(NovaGuiMode.Classic, page.Draft);
        Assert.Equal(NovaGuiMode.Modern, page.RuntimeMode);
        page.Select(NovaGuiMode.Modern);
        Assert.True(page.IsDirty);
        Assert.Equal(NovaGuiMode.Modern, page.Draft);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
