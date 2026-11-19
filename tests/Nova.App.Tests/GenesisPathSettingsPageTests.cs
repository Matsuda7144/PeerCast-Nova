namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPathSettingsPageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NovaSettingsCategories-" + Guid.NewGuid().ToString("N"));
    private string SettingsFile => Path.Combine(_root, "settings.json");
    private string Executable(string name, string fileName = "PCRPlayer64.exe")
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, []); // Validation fixture, never executed.
        return path;
    }

    [Fact]
    public void PlaybackCancelAndSaveReuseValidationPersistenceAndNextPlaySource()
    {
        var first = Executable("first"); var next = Executable("next", "OtherPlayer.exe");
        var source = new GenesisGuiNextPlaybackPathSource();
        var settings = new GenesisGuiNextPlaybackSettings(new(first), source);
        string? saved = null;
        settings.BindPersistence(value => { saved = value; return true; });
        var page = new GenesisPathSettingsPage(settings, null);
        Assert.True(page.Open(GenesisIntegratedSettingsCategory.Playback));
        page.Edit(next); page.Cancel();
        Assert.Equal(first, page.Draft);
        Assert.Null(saved);
        page.Edit(" \"" + next + "\" ");
        Assert.True(page.Save());
        Assert.Equal(next, settings.PcrPlayerExecutablePath);
        Assert.Equal(next, source.Current);
        Assert.Equal(next, saved);
        Assert.Equal(next, page.Draft);
        Assert.False(page.IsDirty);
        Assert.True(page.Save(reset: true));
        Assert.Null(settings.PcrPlayerExecutablePath);
        Assert.Null(source.Current);
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData("other.exe")]
    [InlineData("PCRPlayer64.exe")]
    public void InvalidMissingAndUnsupportedPathsDoNotReplaceSavedValue(string name)
    {
        var first = Executable("first");
        var settings = new GenesisGuiNextPlaybackSettings(new(first));
        var page = new GenesisPathSettingsPage(settings, null);
        page.Open(GenesisIntegratedSettingsCategory.Playback);
        page.Edit(name == "relative.exe" ? name : Path.Combine(_root, name));
        Assert.False(page.Save());
        Assert.True(page.IsError);
        Assert.NotEmpty(page.Message);
        Assert.Equal(first, settings.PcrPlayerExecutablePath);
        page.Cancel();
        Assert.Equal(first, page.Draft);
    }

    [Fact]
    public void PersistenceFailureRetainsValueAndLongPathsAreNeverTruncated()
    {
        var settings = new GenesisGuiNextPlaybackSettings(new("initial"), validateExecutablePath: _ => null);
        var allowSave = false;
        string? persisted = null;
        settings.BindPersistence(value => { if (!allowSave) return false; persisted = value; return true; });
        var page = new GenesisPathSettingsPage(settings, null);
        page.Open(GenesisIntegratedSettingsCategory.Playback);
        var longPath = "C:\\" + new string('長', 10000) + "\\PCRPlayer64.exe";
        page.Edit(longPath);
        Assert.False(page.Save());
        Assert.Equal("initial", page.Saved);
        Assert.Equal(longPath, page.Draft);
        allowSave = true;
        Assert.True(page.Save());
        Assert.Equal(longPath, persisted);
        Assert.Equal(longPath, page.Saved);
    }

    [Fact]
    public void LegacyChangeWithDraftBlocksBothSaveAndResetUntilCancel()
    {
        var settings = new GenesisGuiNextPlaybackSettings(new("first"), validateExecutablePath: _ => null);
        var writes = 0;
        settings.BindPersistence(_ => { writes++; return true; });
        var page = new GenesisPathSettingsPage(settings, null);
        page.Open(GenesisIntegratedSettingsCategory.Playback);
        page.Edit("draft");
        Assert.Equal(GenesisGuiNextPlaybackSettingsUpdateResult.Saved, settings.SetPcrPlayerExecutablePath("legacy"));
        Assert.False(page.Save());
        Assert.False(page.Save(reset: true));
        Assert.True(page.Conflict);
        Assert.Equal("draft", page.Draft);
        Assert.Equal("legacy", page.Saved);
        Assert.Equal(1, writes);
        page.Cancel();
        Assert.False(page.Conflict);
        Assert.Equal("legacy", page.Draft);
        settings.SetPcrPlayerExecutablePath("newer");
        page.Refresh();
        Assert.Equal("newer", page.Draft);
    }

    [Fact]
    public void RecordingSaveCancelAndResetKeepRuntimeAndNeverCreateTheDestination()
    {
        var initial = Path.Combine(_root, "initial"); var next = Path.Combine(_root, "next");
        var recording = new GenesisRecordingOutputDirectorySettings(new(SettingsFile), initial);
        var page = new GenesisPathSettingsPage(null, recording);
        page.Open(GenesisIntegratedSettingsCategory.Recording);
        page.Edit(next); page.Cancel();
        Assert.False(File.Exists(SettingsFile));
        page.Edit(next);
        Assert.True(page.Save());
        Assert.Equal(next, page.EffectiveDirectory);
        Assert.Equal(initial, page.RuntimeDirectory);
        Assert.False(Directory.Exists(next));
        Assert.Equal(next, new NovaSettingsStore(SettingsFile).Load().RecordingOutputDirectory);
        Assert.True(page.Save(reset: true));
        Assert.Equal(initial, page.EffectiveDirectory);
        Assert.Equal(initial, page.RuntimeDirectory);
        Assert.Null(new NovaSettingsStore(SettingsFile).Load().RecordingOutputDirectory);
    }

    [Fact]
    public void RecordingFailureAndCategorySwitchDoNotSaveDraftsOrOtherCategories()
    {
        var fail = false;
        var store = new NovaSettingsStore(SettingsFile, () => { if (fail) throw new IOException("test"); });
        var recording = new GenesisRecordingOutputDirectorySettings(store, _root);
        var playback = new GenesisGuiNextPlaybackSettings(new("initial"), validateExecutablePath: _ => null);
        var page = new GenesisPathSettingsPage(playback, recording);
        page.Open(GenesisIntegratedSettingsCategory.Playback);
        page.Edit("not-saved");
        page.Open(GenesisIntegratedSettingsCategory.Recording);
        Assert.Equal("initial", playback.PcrPlayerExecutablePath);
        page.Edit(Path.Combine(_root, "next"));
        fail = true;
        Assert.False(page.Save());
        Assert.Equal(_root, page.EffectiveDirectory);
        page.Close();
        page.Open(GenesisIntegratedSettingsCategory.Playback);
        Assert.Equal("initial", page.Draft);
        page.Release();
        page.Edit("late");
        Assert.False(page.Save());
        Assert.False(page.Save(reset: true));
        Assert.False(page.Open(GenesisIntegratedSettingsCategory.Recording));
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public async Task IntegratedTestSoundUsesExistingModelEvenWhenAutomaticSoundIsOff()
    {
        var sound = new TestSound();
        var settings = new GenesisChannelNotificationSettings(new(false, false, false), sound);
        await GenesisGuiNextAppShellBridge.RequestSettingsSoundAsync(settings);
        Assert.Equal(1, sound.Calls);
        Assert.False(settings.Current.PlaySound);
    }

    [Fact]
    public async Task RecordingLoadWarningRemainsVisibleUntilExplicitReset()
    {
        var store = new NovaSettingsStore(SettingsFile);
        await store.SaveAsync(new() { RecordingOutputDirectory = "relative" });
        var recording = new GenesisRecordingOutputDirectorySettings(store, _root);
        var page = new GenesisPathSettingsPage(null, recording);
        Assert.True(page.Open(GenesisIntegratedSettingsCategory.Recording));
        Assert.True(page.IsError);
        Assert.Contains("無効", page.Message);
        Assert.Equal(_root, page.RuntimeDirectory);
        Assert.True(page.Save(reset: true));
        Assert.False(page.IsError);
        Assert.Null(store.Load().RecordingOutputDirectory);
    }

    [Fact]
    public void LegacyRecordingChangeDoesNotGetOverwrittenByDraftOrReset()
    {
        var recording = new GenesisRecordingOutputDirectorySettings(new(SettingsFile), _root);
        var page = new GenesisPathSettingsPage(null, recording);
        page.Open(GenesisIntegratedSettingsCategory.Recording);
        page.Edit(Path.Combine(_root, "draft"));
        var legacy = Path.Combine(_root, "legacy");
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, recording.Save(legacy));
        Assert.False(page.Save());
        Assert.False(page.Save(reset: true));
        Assert.Equal(legacy, page.EffectiveDirectory);
        Assert.Equal(_root, page.RuntimeDirectory);
        page.Cancel();
        Assert.Equal(legacy, page.Draft);
        Assert.False(page.Conflict);
    }

    private sealed class TestSound : IGenesisSystemNotificationSoundPlayer
    {
        internal int Calls;
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) { Calls++; return ValueTask.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
