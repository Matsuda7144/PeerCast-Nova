using System.Reflection;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingOutputDirectorySettingsTests
{
    [Fact]
    public void AbsentSettingUsesPreviousDefaultWithoutCreatingSettingsOrDestination()
    {
        using var fixture = new Fixture();
        var settings = fixture.Load();
        Assert.Equal(fixture.Default, settings.CurrentRuntimeDirectory);
        Assert.True(settings.Presentation.UsesDefault);
        Assert.Null(settings.Presentation.ConfiguredDirectory);
        Assert.False(File.Exists(fixture.SettingsPath));
        Assert.False(Directory.Exists(fixture.Default));
    }

    [Fact]
    public void DefaultResolverPreservesKnownVideosFolderAndLocalAppDataFallback()
    {
        using var fixture = new Fixture();
        var videos = Path.Combine(fixture.Root, "redirected-videos");
        var local = Path.Combine(fixture.Root, "local");
        Assert.Equal(Path.Combine(videos, "PeerCast Nova", "Recordings"),
            GenesisRecordingOutputDirectorySettings.ResolveDefaultDirectory(videos, local));
        Assert.Equal(Path.Combine(local, "PeerCast Nova", "Recordings"),
            GenesisRecordingOutputDirectorySettings.ResolveDefaultDirectory(" ", local));
        var actualVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var actualLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(actualVideos) ? actualLocal : actualVideos,
            "PeerCast Nova", "Recordings")), GenesisRecordingOutputDirectorySettings.ResolveDefaultDirectory());
    }

    [Fact]
    public void ValidNonexistentCustomDirectoryIsNormalizedSavedAndReloadedWithoutIoProbe()
    {
        using var fixture = new Fixture();
        var settings = fixture.Load();
        var path = Path.Combine(fixture.Custom, "child", "..");
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(path));
        var reload = fixture.Load();
        Assert.Equal(fixture.Custom, reload.CurrentRuntimeDirectory);
        Assert.Equal(fixture.Custom, reload.Presentation.ConfiguredDirectory);
        Assert.False(reload.Presentation.UsesDefault);
        Assert.Null(reload.Presentation.Error);
        Assert.False(Directory.Exists(fixture.Custom));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    [InlineData("../recordings")]
    [InlineData("bad\0path")]
    [InlineData("%USERPROFILE%/Videos")]
    public void InvalidInputDoesNotWriteOrChangeEffectiveOrRuntimeDirectory(string? input)
    {
        using var fixture = new Fixture();
        var settings = fixture.Load();
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Invalid, settings.Save(input));
        Assert.NotNull(settings.Presentation.Error);
        Assert.Equal(fixture.Default, settings.Presentation.EffectiveDirectory);
        Assert.Equal(fixture.Default, settings.CurrentRuntimeDirectory);
        Assert.False(File.Exists(fixture.SettingsPath));
    }

    [Theory]
    [InlineData("C:relative")]
    [InlineData(@"C:\invalid?directory")]
    [InlineData(@"C:\invalid|directory")]
    [InlineData(@"C:\dir:stream")]
    [InlineData(@"C:\CON")]
    [InlineData(@"C:\trailing.")]
    [InlineData(@"\\?\C:\recordings")]
    public void WindowsInvalidDirectorySyntaxIsRejected(string input)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(GenesisRecordingOutputDirectorySettings.TryNormalize(input, out _));
    }

    [Fact]
    public void EnvironmentVariableInAbsolutePathIsNotExpanded()
    {
        using var fixture = new Fixture();
        var literal = Path.Combine(fixture.Root, "%USERPROFILE%");
        Assert.True(GenesisRecordingOutputDirectorySettings.TryNormalize(literal, out var normalized));
        Assert.Equal(literal, normalized);
    }

    [Fact]
    public async Task SaveAndResetPreserveLatestNatAndOtherGlobalFields()
    {
        using var fixture = new Fixture();
        var settings = fixture.Load();
        var unrelated = new NovaOptions
        {
            NodeName = "existing-node",
            ListenPort = 17344,
            AutoPortMapping = true,
            EnableDetailedDiagnostics = true
        };
        await fixture.Store.SaveAsync(unrelated);
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(fixture.Custom));
        Assert.Equal(unrelated with { RecordingOutputDirectory = fixture.Custom }, fixture.Store.Load());
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.ResetToDefault());
        Assert.Equal(unrelated, fixture.Store.Load());
        Assert.Equal(fixture.Default, fixture.Load().CurrentRuntimeDirectory);
    }

    [Fact]
    public async Task OldPayloadWithoutOptionalFieldLoadsAndPreservesOptions()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.SettingsPath,
            "{\"NodeName\":\"old-node\",\"ListenPort\":7145,\"AutoPortMapping\":true}");
        var settings = fixture.Load();
        Assert.Equal(fixture.Default, settings.CurrentRuntimeDirectory);
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(fixture.Custom));
        Assert.True(fixture.Store.Load().AutoPortMapping);
        Assert.Equal("old-node", fixture.Store.Load().NodeName);
        Assert.Equal(7145, fixture.Store.Load().ListenPort);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("")]
    [InlineData("bad\0path")]
    public async Task InvalidPersistedValueFallsBackAtStartupWithTruthfulWarning(string input)
    {
        using var fixture = new Fixture();
        await fixture.Store.SaveAsync(new() { RecordingOutputDirectory = input });
        var before = await File.ReadAllTextAsync(fixture.SettingsPath);
        var settings = fixture.Load();
        Assert.Equal(fixture.Default, settings.CurrentRuntimeDirectory);
        Assert.True(settings.Presentation.UsesDefault);
        Assert.NotNull(settings.Presentation.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(fixture.SettingsPath));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    public async Task CorruptPayloadDoesNotCrashOrGetOverwrittenBySaveOrReset(string payload)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.SettingsPath, payload);
        var settings = fixture.Load();
        Assert.Equal(fixture.Default, settings.CurrentRuntimeDirectory);
        Assert.NotNull(settings.Presentation.Error);
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Unavailable, settings.Save(fixture.Custom));
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Unavailable, settings.ResetToDefault());
        Assert.Equal(payload, await File.ReadAllTextAsync(fixture.SettingsPath));
    }

    [Fact]
    public void PersistenceIoFailureDoesNotClaimSuccessOrChangeRuntime()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.SettingsPath);
        var settings = fixture.Load();
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Unavailable, settings.Save(fixture.Custom));
        Assert.Equal(fixture.Default, settings.Presentation.EffectiveDirectory);
        Assert.NotNull(settings.Presentation.Error);
        Assert.False(Directory.Exists(fixture.Custom));
    }

    [Fact]
    public async Task SaveResetAndRecreatedProductionCompositionObeyNextStartContract()
    {
        using var fixture = new Fixture();
        var settings = fixture.Load();
        await using var current = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, fixture.HistoryStore);
        Assert.Equal(fixture.Default, current.RecordingDirectory);
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(fixture.Custom));
        Assert.Equal(fixture.Default, current.RecordingDirectory);
        Assert.Equal(fixture.Default, settings.Presentation.CurrentRuntimeDirectory);
        Assert.Equal(fixture.Custom, settings.Presentation.EffectiveDirectory);
        Assert.Contains("次回起動", settings.Presentation.Notice);
        await using var next = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(fixture.Load(), fixture.HistoryStore);
        Assert.Equal(fixture.Custom, next.RecordingDirectory);
        Assert.Null(next.RelayRuntime.PortMapping);
        Assert.Equal(0, next.RelayRuntime.OwnedCoordinatorCount);
        Assert.Empty(await next.Recording.GetActiveRecordingSessionsAsync());
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.ResetToDefault());
        Assert.Equal(fixture.Custom, next.RecordingDirectory);
        await using var reset = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(fixture.Load(), fixture.HistoryStore);
        Assert.Equal(fixture.Default, reset.RecordingDirectory);
        Assert.False(Directory.Exists(fixture.Default));
        Assert.False(Directory.Exists(fixture.Custom));
    }

    [Fact]
    public async Task NormalGenesisProductionFactoryUsesSameSnapshotAndRuntimeForManualAndAutomatic()
    {
        using var fixture = new Fixture();
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, fixture.Load().Save(fixture.Custom));
        var settings = fixture.Load();
        await using var gui = NovaWindowsStartupComposition.CreateDefaultGenesisPlaybackController(new(), settings, fixture.HistoryStore);
        var daily = Field<NovaDailyHttpFlvPlaybackController>(gui, "_dailyController");
        var composition = Field<PcpRecordingRuntimeComposition>(daily, "_recordingComposition");
        var manual = Field<GenesisManualRecordingSessionController>(daily, "_manualRecordingController");
        var automatic = Field<GenesisAutomaticRecordingEvaluationController>(daily, "_automaticRecordingController");
        Assert.Equal(fixture.Custom, composition.RecordingDirectory);
        Assert.Same(composition.Recording, Field<RecordingRuntime>(manual, "_recording"));
        Assert.Same(composition.Recording, Field<RecordingRuntime>(automatic, "_recording"));
        Assert.Same(Field<GenesisSharedPcpPlaybackRuntime>(daily, "_sharedPlaybackRuntime"),
            Field<IGenesisSharedPcpRecordingOwnerProvider>(automatic, "_ownerProvider"));
        Assert.Null(composition.RelayRuntime.PortMapping);
        Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        Assert.False(Directory.Exists(fixture.Custom));
    }

    [Fact]
    public async Task ResetDoesNotDeleteMoveOrCreateRecordingFiles()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Custom);
        var file = Path.Combine(fixture.Custom, "existing.flv");
        var partial = Path.Combine(fixture.Custom, ".active.partial");
        await File.WriteAllTextAsync(file, "existing media");
        await File.WriteAllTextAsync(partial, "active bytes");
        var settings = fixture.Load();
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(fixture.Custom));
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.ResetToDefault());
        Assert.Equal("existing media", await File.ReadAllTextAsync(file));
        Assert.Equal("active bytes", await File.ReadAllTextAsync(partial));
        Assert.False(Directory.Exists(fixture.Default));
    }

    [Fact]
    public async Task RecordingIoFailureDoesNotChangeSelectedDestinationOrFallback()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Custom, "a file blocks this directory");
        var settings = fixture.Load();
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(fixture.Custom));
        var reload = fixture.Load();
        await using var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(reload, fixture.HistoryStore);
        using var stream = new MemoryStream([0]);
        await Assert.ThrowsAnyAsync<IOException>(() => RecordingStreamFileWriter.WriteAsync(stream,
            Path.Combine(composition.RecordingDirectory, "blocked.flv"), CancellationToken.None).AsTask());
        Assert.Equal(fixture.Custom, composition.RecordingDirectory);
        Assert.Equal(fixture.Custom, reload.Presentation.EffectiveDirectory);
        Assert.False(Directory.Exists(fixture.Default));
    }

    [Fact]
    public async Task ExistingSettingsMenuDispatchesRecordingRouteWithoutTouchingOtherRoutes()
    {
        var calls = 0;
        var notifications = new GenesisChannelNotificationSettings(new(false, false, false), new SilentSoundPlayer());
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(notifications,
            GenesisChannelNotificationSettingsCommand.Recording, null, null, null,
            _ => { calls++; return ValueTask.CompletedTask; });
        Assert.Equal(1, calls);
        Assert.Contains("録画", GenesisGuiNextSettingsWindowFeedback.Resolve(GenesisGuiNextSettingsWindowKind.Recording).Caption);
    }

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));

    private sealed class SilentSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Nova-output-directory-tests", Guid.NewGuid().ToString("N"));
        public string Default => Path.Combine(Root, "default");
        public string Custom => Path.Combine(Root, "custom");
        public string SettingsPath => Path.Combine(Root, "settings.json");
        public GenesisRecordingHistoryStore HistoryStore => new(Path.Combine(Root, "recording-history.json"));
        public NovaSettingsStore Store { get; }
        public Fixture() { Directory.CreateDirectory(Root); Store = new(SettingsPath); }
        public GenesisRecordingOutputDirectorySettings Load() => new(Store, Default);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
