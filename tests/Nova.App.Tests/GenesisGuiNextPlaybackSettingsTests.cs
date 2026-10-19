using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPlaybackSettingsTests
{
    [Fact]
    public void PathNormalizationTrimsWhitespaceAndMatchingQuotes()
    {
        const string path = "C:\\Player Tools\\配信\\PCRPlayer64.exe";

        Assert.Equal(
            path,
            GenesisGuiNextPlaybackSettings.NormalizePath($"  \"{path}\"  "));
        Assert.Null(GenesisGuiNextPlaybackSettings.NormalizePath("  \"  \"  "));
        Assert.Null(GenesisGuiNextPlaybackSettings.NormalizePath(" \t "));
    }

    [Fact]
    public async Task PlaybackPreferenceJsonRoundTripsUnsetAndUnicodeSpacePath()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonGenesisGuiNextPlaybackPreferencesStore(
            Path.Combine(directory.Path, "playback-preferences.json"));
        const string path = "C:\\Player Tools\\配信\\PCRPlayer64.exe";

        await store.SaveAsync(GenesisGuiNextPlaybackPreferences.Default);
        Assert.Equal(
            GenesisGuiNextPlaybackPreferences.Default,
            await store.LoadAsync());

        await store.SaveAsync(new(path));
        Assert.Equal(path, (await store.LoadAsync()).PcrPlayerExecutablePath);

        await store.SaveAsync(new(null));
        Assert.Null((await store.LoadAsync()).PcrPlayerExecutablePath);
    }

    [Fact]
    public async Task LoadedAndUpdatedPathFlowsThroughApplicationStateToPlaybackSource()
    {
        const string initialPath = "C:\\Saved Player\\PCRPlayer64.exe";
        const string updatedPath = "D:\\配信 Tools\\PCRPlayer64.exe";
        var store = new RecordingPlaybackPreferencesStore(new(initialPath));
        var pathSource = new GenesisGuiNextPlaybackPathSource();
        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            new EmptyFavoriteStore(),
            new EmptyNotificationStore(),
            DefaultGenesisYellowPageConfigurationStore.Instance,
            store,
            new SilentSoundPlayer(),
            pathSource);

        Assert.Equal(initialPath, state.Playback.PcrPlayerExecutablePath);
        Assert.Equal(initialPath, pathSource.Current);
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            state.Playback.SetPcrPlayerExecutablePath($" \"{updatedPath}\" "));
        Assert.Equal(updatedPath, state.Playback.PcrPlayerExecutablePath);
        Assert.Equal(updatedPath, pathSource.Current);
        Assert.Equal(updatedPath, store.Saved?.PcrPlayerExecutablePath);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            state.Playback.SetPcrPlayerExecutablePath(" "));
        Assert.Null(state.Playback.PcrPlayerExecutablePath);
        Assert.Null(pathSource.Current);
        Assert.Null(store.Saved?.PcrPlayerExecutablePath);
    }

    [Fact]
    public void FailedPersistenceDoesNotChangeConfiguredPath()
    {
        const string currentPath = "C:\\Player\\PCRPlayer64.exe";
        var source = new GenesisGuiNextPlaybackPathSource();
        var settings = new GenesisGuiNextPlaybackSettings(new(currentPath), source);
        settings.BindPersistence(static _ => false);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed,
            settings.SetPcrPlayerExecutablePath("D:\\Missing Save\\PCRPlayer64.exe"));
        Assert.Equal(currentPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(currentPath, source.Current);
    }

    [Fact]
    public void BrowseSelectionAcceptsNormalizedWindowsPath()
    {
        const string path = "C:\\Player Tools\\配信\\PCRPlayer64.exe";

        Assert.True(GenesisGuiNextPlaybackSettingsWindow.TryResolveBrowsePath(
            $" \"{path}\" ",
            out var selected));
        Assert.Equal(path, selected);
        Assert.False(GenesisGuiNextPlaybackSettingsWindow.TryResolveBrowsePath(" ", out _));
    }

    [Fact]
    public async Task SaveButtonClickPersistsPathAndUpdatesPlaybackSource()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string path = "C:\\Player Tools\\PCRPlayer64.exe";
        var pathSource = new GenesisGuiNextPlaybackPathSource();
        var settings = new GenesisGuiNextPlaybackSettings(
            GenesisGuiNextPlaybackPreferences.Default,
            pathSource);
        string? persistedPath = null;
        settings.BindPersistence(value =>
        {
            persistedPath = value;
            return true;
        });

        var owner = CreateNativeWindow();
        Assert.NotEqual(0, owner);
        try
        {
            await using var window = GenesisGuiNextPlaybackSettingsWindow.TryCreate(owner, settings);
            Assert.NotNull(window);

            var settingsWindow = FindOwnedWindow(owner, "Playback / PCRPlayer");
            Assert.NotEqual(0, settingsWindow);
            var controls = FindSettingsControls(settingsWindow);
            Assert.NotEqual(0, controls.Path);
            Assert.NotEqual(0, controls.Save);
            Assert.NotEqual(0, controls.Status);

            Assert.True(SetWindowTextW(controls.Path, path));
            _ = SendMessageW(controls.Save, ButtonMessageClick, 0, 0);

            Assert.Equal("PCRPlayer設定を保存しました。次の再生から使用します。", ReadWindowText(controls.Status));
            Assert.Equal(path, persistedPath);
            Assert.Equal(path, settings.PcrPlayerExecutablePath);
            Assert.Equal(path, pathSource.Current);
        }
        finally
        {
            _ = DestroyWindow(owner);
        }
    }

    private sealed class RecordingPlaybackPreferencesStore(
        GenesisGuiNextPlaybackPreferences loaded) : IGenesisGuiNextPlaybackPreferencesStore
    {
        public GenesisGuiNextPlaybackPreferences? Saved { get; private set; }

        public ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(loaded);
        }

        public ValueTask SaveAsync(
            GenesisGuiNextPlaybackPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saved = preferences;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyFavoriteStore : IGenesisFavoriteChannelStore
    {
        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>([]);
        }

        public ValueTask SaveAsync(
            IReadOnlyList<GenesisFavoriteChannel> favorites,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyNotificationStore : IGenesisChannelNotificationPreferenceStore
    {
        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        }

        public ValueTask SaveAsync(
            GenesisChannelNotificationPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SilentSoundPlayer : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"NovaPlaybackSettings-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private const uint ButtonMessageClick = 0x00F5;
    private const uint WindowStylePopup = 0x80000000;

    private static nint CreateNativeWindow()
    {
        var instance = GetModuleHandleW(null);
        return CreateWindowExW(
            0,
            "STATIC",
            "PlaybackSettingsTestOwner",
            WindowStylePopup,
            0,
            0,
            1,
            1,
            0,
            0,
            instance,
            0);
    }

    private static nint FindOwnedWindow(nint owner, string title)
    {
        nint match = 0;
        _ = EnumWindows((window, _) =>
        {
            if (GetWindow(window, GetWindowOwner) == owner && ReadWindowText(window) == title)
            {
                match = window;
                return false;
            }

            return true;
        }, 0);
        return match;
    }

    private static SettingsControls FindSettingsControls(nint window)
    {
        var controls = new SettingsControls();
        _ = EnumChildWindows(window, (child, _) =>
        {
            switch (GetDlgCtrlID(child))
            {
                case 1001:
                    controls.Path = child;
                    break;
                case 1003:
                    controls.Save = child;
                    break;
                case 1006:
                    controls.Status = child;
                    break;
            }

            return true;
        }, 0);
        return controls;
    }

    private static string ReadWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(window, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    private sealed class SettingsControls
    {
        public nint Path { get; set; }

        public nint Save { get; set; }

        public nint Status { get; set; }
    }

    private const uint GetWindowOwner = 4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, [Out] char[] text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint window, string text);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProcedure(nint window, nint parameter);
}
