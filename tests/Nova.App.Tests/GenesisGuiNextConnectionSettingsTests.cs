using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextConnectionSettingsTests
{
    [Fact]
    public void DefaultsRetainPortAndIpv6PolicyAndUnlimitedLimits()
    {
        var value = GenesisGuiNextConnectionPreferences.Default;
        Assert.Equal(new NovaOptions().ListenPort, value.ListenPort);
        Assert.False(value.UseIpv6);
        Assert.Null(value.TotalUpstreamIpv4Kbps);
        Assert.Null(value.TotalUpstreamIpv6Kbps);
        Assert.Null(value.MaxRelaysTotal);
        Assert.Null(value.MaxRelaysPerBroadcastChannel);
        Assert.Null(value.MaxRelaysPerViewingChannel);
        Assert.Equal(string.Empty, GenesisGuiNextConnectionInput.From(value).MaxRelaysTotal);
    }

    [Fact]
    public async Task SaveRoundTripCapturesNextStartupWithoutChangingCurrentStartup()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonGenesisGuiNextConnectionPreferencesStore(Path.Combine(directory.Path, "connection.json"));
        await using var first = await CreateStateAsync(store);
        var input = ValidInput();
        Assert.True((await first.Connection.SaveAsync(input)).Saved);
        Assert.Equal(GenesisGuiNextConnectionPreferences.Default, first.Connection.Startup);
        Assert.Equal(17144, first.Connection.Saved.ListenPort);
        Assert.True(first.Connection.Saved.UseIpv6);
        Assert.True(first.Connection.HasPendingRestart);
        var saved = await store.LoadAsync();
        Assert.Equal(first.Connection.Saved, saved);
        Assert.Equal(2000, saved.TotalUpstreamIpv4Kbps);
        Assert.Equal(4000, saved.TotalUpstreamIpv6Kbps);
        Assert.Equal(10, saved.MaxRelaysTotal);
        Assert.Equal(4, saved.MaxRelaysPerBroadcastChannel);
        Assert.Equal(2, saved.MaxRelaysPerViewingChannel);
        await using var restarted = await CreateStateAsync(store);
        Assert.Equal(saved, restarted.Connection.Startup);
        Assert.Equal(saved, restarted.Connection.Saved);
        Assert.False(restarted.Connection.HasPendingRestart);
    }

    [Fact]
    public async Task BlankLimitsAndExplicitZeroRemainDifferentAfterRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonGenesisGuiNextConnectionPreferencesStore(Path.Combine(directory.Path, "connection.json"));
        await using var settings = new GenesisGuiNextConnectionSettings(GenesisGuiNextConnectionPreferences.Default, store);
        var input = ValidInput() with
        {
            TotalUpstreamIpv4Kbps = " ",
            TotalUpstreamIpv6Kbps = "0",
            MaxRelaysTotal = "",
            MaxRelaysPerBroadcastChannel = "2147483647",
            MaxRelaysPerViewingChannel = "0"
        };
        Assert.True((await settings.SaveAsync(input)).Saved);
        var loaded = await store.LoadAsync();
        Assert.Null(loaded.TotalUpstreamIpv4Kbps);
        Assert.Equal(0, loaded.TotalUpstreamIpv6Kbps);
        Assert.Null(loaded.MaxRelaysTotal);
        Assert.Equal(int.MaxValue, loaded.MaxRelaysPerBroadcastChannel);
        Assert.Equal(0, loaded.MaxRelaysPerViewingChannel);
        var restored = GenesisGuiNextConnectionInput.From(loaded);
        Assert.Equal("", restored.TotalUpstreamIpv4Kbps);
        Assert.Equal("0", restored.TotalUpstreamIpv6Kbps);
        Assert.Equal("", restored.MaxRelaysTotal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("+7144")]
    [InlineData("7.1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    [InlineData("７１４４")]
    public async Task InvalidPortIsRejectedBeforeStoreAndPreservesBothSnapshots(string port)
    {
        var store = new MemoryStore();
        await using var settings = new GenesisGuiNextConnectionSettings(store.Value, store);
        var initial = settings.Saved;
        var result = await settings.SaveAsync(ValidInput() with { ListenPort = port });
        Assert.False(result.Saved);
        Assert.Contains("ポート番号", result.Message, StringComparison.Ordinal);
        Assert.Same(initial, settings.Saved);
        Assert.Same(initial, settings.Startup);
        Assert.Same(initial, store.Value);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("65535")]
    [InlineData(" 7144 ")]
    public void BoundaryPortsAreValid(string port) =>
        Assert.True((ValidInput() with { ListenPort = port }).TryValidate(out _, out _));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task EveryLimitRejectsNegativeMalformedAndOverflowValues(int field)
    {
        var store = new MemoryStore();
        await using var settings = new GenesisGuiNextConnectionSettings(store.Value, store);
        foreach (var invalid in new[] { "-1", "+1", "1.5", "NaN", "1e3", "2147483648" })
        {
            var input = SetLimit(ValidInput(), field, invalid);
            var result = await settings.SaveAsync(input);
            Assert.False(result.Saved);
            Assert.Contains("整数", result.Message, StringComparison.Ordinal);
            Assert.Equal(GenesisGuiNextConnectionPreferences.Default, settings.Saved);
            Assert.Equal(GenesisGuiNextConnectionPreferences.Default, store.Value);
        }
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task EditorRetainsRejectedTextAndClearsOldErrorAfterCorrection()
    {
        var store = new MemoryStore();
        await using var settings = new GenesisGuiNextConnectionSettings(store.Value, store);
        var editor = new GenesisGuiNextConnectionEditor(settings) { Input = ValidInput() with { MaxRelaysTotal = "-7" } };
        var entered = editor.Input;
        await editor.SaveAsync();
        Assert.Same(entered, editor.Input);
        Assert.Contains("合計リレー数", editor.Status, StringComparison.Ordinal);
        editor.Input = ValidInput();
        await editor.SaveAsync();
        Assert.Equal("設定を保存しました。次回起動時に読み込みます。", editor.Status);
        Assert.Equal(editor.Input, GenesisGuiNextConnectionInput.From(settings.Saved));
    }

    [Fact]
    public async Task StoreFailureKeepsSavedStartupAndPersistedValuesAndAllowsRetry()
    {
        var store = new MemoryStore { FailSave = true };
        await using var settings = new GenesisGuiNextConnectionSettings(store.Value, store);
        var editor = new GenesisGuiNextConnectionEditor(settings) { Input = ValidInput() };
        await editor.SaveAsync();
        Assert.Contains("保存できません", editor.Status, StringComparison.Ordinal);
        Assert.Equal(ValidInput(), editor.Input);
        Assert.Equal(GenesisGuiNextConnectionPreferences.Default, settings.Saved);
        Assert.Equal(settings.Startup, store.Value);
        store.FailSave = false;
        await editor.SaveAsync();
        Assert.Equal(17144, settings.Saved.ListenPort);
        Assert.Equal(settings.Saved, store.Value);
        Assert.Equal(7144, settings.Startup.ListenPort);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"ListenPort\":0}")]
    [InlineData("{\"MaxRelaysTotal\":-1}")]
    [InlineData("{\"SchemaVersion\":2}")]
    public async Task LoadFailureFallsBackAndShutdownCannotOverwriteOriginalData(string content)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "connection.json");
        await File.WriteAllTextAsync(path, content);
        var store = new JsonGenesisGuiNextConnectionPreferencesStore(path);
        var state = await CreateStateAsync(store);
        Assert.Equal(GenesisGuiNextApplicationStateLoadFailure.ConnectionPreferences, state.LoadFailures);
        Assert.True(state.Connection.LoadFailed);
        Assert.Equal(GenesisGuiNextConnectionPreferences.Default, state.Connection.Startup);
        Assert.Contains("接続・帯域・リレー設定", GenesisGuiNextApplicationStateLoadFeedback.Resolve(state.LoadFailures)!.Message, StringComparison.Ordinal);
        Assert.Contains("読み込めません", new GenesisGuiNextConnectionEditor(state.Connection).Status, StringComparison.Ordinal);
        await state.DisposeAsync();
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ExplicitSaveRecoversFromFailedLoadIncludingUnchangedDefaults()
    {
        var store = new MemoryStore { FailLoad = true };
        await using var state = await CreateStateAsync(store);
        var result = await state.Connection.SaveAsync(GenesisGuiNextConnectionInput.From(state.Connection.Startup));
        Assert.True(result.Saved);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task MissingFileAndUnavailableRootAreSafeAndDoNotCreateSettingsOnShutdown()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "missing", "connection.json");
        await using (var state = await CreateStateAsync(new JsonGenesisGuiNextConnectionPreferencesStore(path)))
            Assert.Equal(GenesisGuiNextApplicationStateLoadFailure.None, state.LoadFailures);
        Assert.False(File.Exists(path));
        await using var unavailable = await CreateStateAsync(UnavailableGenesisGuiNextConnectionPreferencesStore.Instance);
        Assert.True(unavailable.Connection.LoadFailed);
        Assert.False((await unavailable.Connection.SaveAsync(ValidInput())).Saved);
    }

    [Fact]
    public async Task InvalidModelAndCanceledSaveCannotReplaceExistingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "connection.json");
        var store = new JsonGenesisGuiNextConnectionPreferencesStore(path);
        await store.SaveAsync(GenesisGuiNextConnectionPreferences.Default);
        var old = await File.ReadAllTextAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new() { MaxRelaysTotal = -1 }).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new() { ListenPort = 17144 }, cancellation.Token).AsTask());
        Assert.Equal(old, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task ConnectionSaveCoexistsWithPlaybackNotificationAndYellowPageData()
    {
        using var directory = new TemporaryDirectory();
        var connection = new JsonGenesisGuiNextConnectionPreferencesStore(Path.Combine(directory.Path, "connection.json"));
        var playback = new JsonGenesisGuiNextPlaybackPreferencesStore(Path.Combine(directory.Path, "playback.json"));
        var notification = new JsonGenesisChannelNotificationPreferenceStore(Path.Combine(directory.Path, "notification.json"));
        var yp = new JsonGenesisYellowPageConfigurationStore(Path.Combine(directory.Path, "yp.json"));
        await playback.SaveAsync(new("C:\\Approved\\PCRPlayer64.exe"));
        await notification.SaveAsync(new(false, false, false));
        await yp.SaveAsync([GenesisYellowPageDefaults.Sp]);
        var before = Directory.GetFiles(directory.Path).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);
        await using var state = await GenesisGuiNextApplicationState.LoadAsync(new FavoriteStore(), notification, yp, playback,
            connection, new SilentSound());
        Assert.True((await state.Connection.SaveAsync(ValidInput())).Saved);
        Assert.Equal("C:\\Approved\\PCRPlayer64.exe", state.Playback.PcrPlayerExecutablePath);
        Assert.False(state.Notifications.Current.PlaySound);
        Assert.Equal([GenesisYellowPageDefaults.Sp], state.YellowPages.Definitions);
        foreach (var file in before) Assert.Equal(file.Value, await File.ReadAllTextAsync(Path.Combine(directory.Path, file.Key!)));
    }

    [Fact]
    public async Task OrderedSavesAndShutdownWaitForPersistenceAndNeverHotApply()
    {
        var store = new BlockingStore();
        var settings = new GenesisGuiNextConnectionSettings(GenesisGuiNextConnectionPreferences.Default, store);
        var first = settings.SaveAsync(ValidInput()).AsTask();
        await store.Entered.Task;
        var second = settings.SaveAsync(ValidInput() with { ListenPort = "17145" }).AsTask();
        var closing = settings.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.False((await settings.SaveAsync(ValidInput())).Saved);
        Assert.Equal(1, store.SaveCount);
        store.Release.SetResult();
        Assert.True((await first).Saved);
        Assert.True((await second).Saved);
        await closing;
        Assert.Collection(store.Ports, port => Assert.Equal(17144, port), port => Assert.Equal(17145, port));
        Assert.Equal(17145, settings.Saved.ListenPort);
        Assert.Equal(7144, settings.Startup.ListenPort);
    }

    private static GenesisGuiNextConnectionInput ValidInput() => new("17144", true, "2000", "4000", "10", "4", "2");
    [Fact]
    public async Task ProfileAccessGuardRunsBeforeLoadAndBeforeReplace()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "connection.json");
        var initial = new JsonGenesisGuiNextConnectionPreferencesStore(path);
        await initial.SaveAsync(GenesisGuiNextConnectionPreferences.Default);
        var before = await File.ReadAllTextAsync(path);
        var denied = new JsonGenesisGuiNextConnectionPreferencesStore(path, () => throw new IOException("Denied"));
        await Assert.ThrowsAsync<IOException>(() => denied.LoadAsync().AsTask());
        await Assert.ThrowsAsync<IOException>(() => denied.SaveAsync(new() { ListenPort = 17144 }).AsTask());
        var checks = 0;
        var changed = new JsonGenesisGuiNextConnectionPreferencesStore(path, () =>
        {
            if (++checks == 3) throw new IOException("Access changed before rename");
        });
        await Assert.ThrowsAsync<IOException>(() => changed.SaveAsync(new() { ListenPort = 17144 }).AsTask());
        Assert.Equal(before, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static GenesisGuiNextConnectionInput SetLimit(GenesisGuiNextConnectionInput input, int field, string value) => field switch
    {
        0 => input with { TotalUpstreamIpv4Kbps = value },
        1 => input with { TotalUpstreamIpv6Kbps = value },
        2 => input with { MaxRelaysTotal = value },
        3 => input with { MaxRelaysPerBroadcastChannel = value },
        _ => input with { MaxRelaysPerViewingChannel = value },
    };
    private static ValueTask<GenesisGuiNextApplicationState> CreateStateAsync(IGenesisGuiNextConnectionPreferencesStore store) =>
        GenesisGuiNextApplicationState.LoadAsync(new FavoriteStore(), new NotificationStore(), DefaultGenesisYellowPageConfigurationStore.Instance,
            DefaultGenesisGuiNextPlaybackPreferencesStore.Instance, store, new SilentSound());
    private sealed class MemoryStore : IGenesisGuiNextConnectionPreferencesStore
    {
        public GenesisGuiNextConnectionPreferences Value { get; private set; } = GenesisGuiNextConnectionPreferences.Default;
        public bool FailLoad { get; init; }
        public bool FailSave { get; set; }
        public int SaveCount { get; private set; }
        public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            FailLoad ? ValueTask.FromException<GenesisGuiNextConnectionPreferences>(new IOException()) : ValueTask.FromResult(Value);
        public ValueTask SaveAsync(GenesisGuiNextConnectionPreferences preferences, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailSave) throw new UnauthorizedAccessException();
            Value = preferences;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class BlockingStore : IGenesisGuiNextConnectionPreferencesStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveCount { get; private set; }
        public List<int> Ports { get; } = [];
        public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisGuiNextConnectionPreferences.Default);
        public async ValueTask SaveAsync(GenesisGuiNextConnectionPreferences preferences, CancellationToken cancellationToken = default)
        {
            if (++SaveCount == 1) { Entered.SetResult(); await Release.Task; }
            Ports.Add(preferences.ListenPort);
        }
    }
    private sealed class FavoriteStore : IGenesisFavoriteChannelStore
    {
        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>([]);
        public ValueTask SaveAsync(IReadOnlyList<GenesisFavoriteChannel> favorites, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class NotificationStore : IGenesisChannelNotificationPreferenceStore
    {
        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        public ValueTask SaveAsync(GenesisChannelNotificationPreferences preferences, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    { public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask; }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NovaConnectionSettingsTests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
