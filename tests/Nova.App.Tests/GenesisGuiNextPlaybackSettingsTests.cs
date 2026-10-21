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
        using var directory = new TemporaryDirectory();
        var initialPath = directory.CreateExecutableFile("Initial");
        var updatedPath = directory.CreateExecutableFile("Updated");
        var environmentPath = directory.CreateExecutableFile("Environment");
        var pathPlayer = directory.CreateExecutableFile("Path");
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

        var restartedPathSource = new GenesisGuiNextPlaybackPathSource();
        await using var restartedState = await GenesisGuiNextApplicationState.LoadAsync(
            new EmptyFavoriteStore(),
            new EmptyNotificationStore(),
            DefaultGenesisYellowPageConfigurationStore.Instance,
            store,
            new SilentSoundPlayer(),
            restartedPathSource);
        Assert.Equal(updatedPath, restartedState.Playback.PcrPlayerExecutablePath);
        Assert.Equal(updatedPath, restartedPathSource.Current);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            restartedState.Playback.SetPcrPlayerExecutablePath(" "));
        Assert.Null(restartedState.Playback.PcrPlayerExecutablePath);
        Assert.Null(restartedPathSource.Current);
        Assert.Null(store.Saved?.PcrPlayerExecutablePath);
        Assert.Equal(
            environmentPath,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                restartedPathSource.Current,
                environmentPath,
                System.IO.Path.GetDirectoryName(pathPlayer)));
        Assert.Equal(
            pathPlayer,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                restartedPathSource.Current,
                null,
                System.IO.Path.GetDirectoryName(pathPlayer)));
    }

    [Fact]
    public void FailedPersistenceDoesNotChangeConfiguredPath()
    {
        const string currentPath = "C:\\Player\\PCRPlayer64.exe";
        var source = new GenesisGuiNextPlaybackPathSource();
        var settings = new GenesisGuiNextPlaybackSettings(new(currentPath), source, static _ => null);
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
    public void ExecutablePathValidationRequiresExpectedAbsoluteExistingFile()
    {
        using var directory = new TemporaryDirectory();
        var validPath = directory.CreateExecutableFile("Player");
        var missingPath = Path.Combine(directory.Path, "Missing", PcrPlayerExternalPlayerLauncher.ExecutableFileName);
        var directoryPath = directory.CreateExecutableDirectory("Directory.exe");
        var wrongNamePath = Path.Combine(directory.Path, "other.exe");

        Assert.Null(GenesisGuiNextPlaybackSettings.ValidateExecutablePath(validPath));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(missingPath));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(directoryPath));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedExecutableName,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(wrongNamePath));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath("PCRPlayer64.exe"));
    }

    [Fact]
    public void WindowsPathValidationCanUseManagedFileSystemBoundary()
    {
        const string windowsPath = @"C:\Player Tools\PCRPlayer64.exe";
        static bool IsWindowsAbsolute(string path) =>
            path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        static string GetWindowsFileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

        Assert.Null(GenesisGuiNextPlaybackSettings.ValidateExecutablePath(
            windowsPath,
            IsWindowsAbsolute,
            GetWindowsFileName,
            static _ => FileAttributes.Normal));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(
                windowsPath,
                IsWindowsAbsolute,
                GetWindowsFileName,
                static _ => throw new FileNotFoundException()));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(
                windowsPath,
                IsWindowsAbsolute,
                GetWindowsFileName,
                static _ => FileAttributes.Directory));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.AccessDenied,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(
                windowsPath,
                IsWindowsAbsolute,
                GetWindowsFileName,
                static _ => throw new UnauthorizedAccessException()));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(
                "relative\\PCRPlayer64.exe",
                IsWindowsAbsolute,
                GetWindowsFileName,
                static _ => FileAttributes.Normal));
    }

    [Fact]
    public async Task InvalidPathKeepsConfiguredSourceAndPersistedValueUntilCorrected()
    {
        using var directory = new TemporaryDirectory();
        var oldPath = directory.CreateExecutableFile("Old");
        var newPath = directory.CreateExecutableFile("New");
        var source = new GenesisGuiNextPlaybackPathSource();
        var store = new RecordingPlaybackPreferencesStore(new(oldPath));
        var rejectNewPath = true;
        var settings = new GenesisGuiNextPlaybackSettings(
            new(oldPath),
            source,
            path => rejectNewPath && string.Equals(path, newPath, StringComparison.Ordinal)
                ? GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound
                : null);
        settings.BindPersistence(path =>
        {
            try
            {
                store.SaveAsync(new(path)).AsTask().GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                return false;
            }
        });

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound,
            settings.SetPcrPlayerExecutablePath(newPath));
        Assert.Equal(oldPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(oldPath, source.Current);
        Assert.Equal(oldPath, (await store.LoadAsync()).PcrPlayerExecutablePath);
        Assert.Null(store.Saved);

        rejectNewPath = false;
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            settings.SetPcrPlayerExecutablePath(newPath));
        Assert.Equal(newPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(newPath, source.Current);
        Assert.Equal(newPath, store.Saved?.PcrPlayerExecutablePath);

        var failedSavePath = directory.CreateExecutableFile("FailedSave");
        store.FailSaves = true;
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed,
            settings.SetPcrPlayerExecutablePath(failedSavePath));
        Assert.Equal(newPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(newPath, source.Current);
        Assert.Equal(newPath, (await store.LoadAsync()).PcrPlayerExecutablePath);
    }

    [Fact]
    public void ValidationAndPersistenceFailuresPreserveCurrentSettings()
    {
        const string currentPath = "C:\\Player\\PCRPlayer64.exe";
        const string newPath = "D:\\Player\\PCRPlayer64.exe";
        var source = new GenesisGuiNextPlaybackPathSource();
        var settings = new GenesisGuiNextPlaybackSettings(
            new(currentPath),
            source,
            static _ => null);
        settings.BindPersistence(static _ => false);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed,
            settings.SetPcrPlayerExecutablePath(newPath));
        Assert.Equal(currentPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(currentPath, source.Current);
    }

    [Fact]
    public void SaveFeedbackExplainsValidationFailureAndClearsOnSuccess()
    {
        Assert.Equal(
            "指定した実行ファイルが見つかりません。",
            GenesisGuiNextPlaybackSettingsWindow.GetPcrPlayerSaveFeedback(
                GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound,
                "C:\\Missing\\PCRPlayer64.exe"));
        Assert.Equal(
            "PCRPlayer設定を保存しました。次の再生から使用します。",
            GenesisGuiNextPlaybackSettingsWindow.GetPcrPlayerSaveFeedback(
                GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
                "C:\\Player\\PCRPlayer64.exe"));
    }

    [Fact]
    public async Task SaveButtonClickPersistsPathAndUpdatesPlaybackSource()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string currentPath = "C:\\Old Player\\PCRPlayer64.exe";
        const string invalidPath = "C:\\Missing Player\\PCRPlayer64.exe";
        const string path = "C:\\Player Tools\\PCRPlayer64.exe";
        var pathSource = new GenesisGuiNextPlaybackPathSource();
        var settings = new GenesisGuiNextPlaybackSettings(
            new(currentPath),
            pathSource,
            value => string.Equals(value, path, StringComparison.Ordinal)
                ? null
                : GenesisGuiNextPlaybackSettingsUpdateResult.ExecutableNotFound);
        string? persistedPath = currentPath;
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
            AssertNativeControlsShareWindowFont(controls.All);

            Assert.True(SetWindowTextW(controls.Path, invalidPath));
            _ = SendMessageW(controls.Save, ButtonMessageClick, 0, 0);

            Assert.Equal(
                "指定した実行ファイルが見つかりません。",
                ReadWindowText(controls.Status));
            Assert.Equal(invalidPath, ReadWindowText(controls.Path));
            Assert.Equal(currentPath, settings.PcrPlayerExecutablePath);
            Assert.Equal(currentPath, persistedPath);
            Assert.Equal(currentPath, pathSource.Current);

            Assert.True(SetWindowTextW(controls.Path, path));
            _ = SendMessageW(controls.Save, ButtonMessageClick, 0, 0);

            Assert.Equal("PCRPlayer設定を保存しました。次の再生から使用します。", ReadWindowText(controls.Status));
            Assert.Equal(path, persistedPath);
            Assert.Equal(path, settings.PcrPlayerExecutablePath);
            Assert.Equal(path, pathSource.Current);

            await window.DisposeAsync();
            await using var reopened = GenesisGuiNextPlaybackSettingsWindow.TryCreate(owner, settings);
            Assert.NotNull(reopened);
            var reopenedWindow = FindOwnedWindow(owner, "Playback / PCRPlayer");
            Assert.NotEqual(0, reopenedWindow);
            AssertNativeControlsShareWindowFont(FindSettingsControls(reopenedWindow).All);
        }
        finally
        {
            _ = DestroyWindow(owner);
        }
    }

    private sealed class RecordingPlaybackPreferencesStore(
        GenesisGuiNextPlaybackPreferences loaded) : IGenesisGuiNextPlaybackPreferencesStore
    {
        private GenesisGuiNextPlaybackPreferences _current = loaded;

        public GenesisGuiNextPlaybackPreferences? Saved { get; private set; }

        public bool FailSaves { get; set; }

        public ValueTask<GenesisGuiNextPlaybackPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_current);
        }

        public ValueTask SaveAsync(
            GenesisGuiNextPlaybackPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSaves)
            {
                throw new IOException("Playback preference save failed.");
            }

            _current = preferences;
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

        public string CreateExecutableFile(string directoryName)
        {
            var filePath = System.IO.Path.Combine(
                System.IO.Path.Combine(Path, directoryName),
                PcrPlayerExternalPlayerLauncher.ExecutableFileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, []);
            return filePath;
        }

        public string CreateExecutableDirectory(string directoryName)
        {
            var directoryPath = System.IO.Path.Combine(
                Path,
                directoryName,
                PcrPlayerExternalPlayerLauncher.ExecutableFileName);
            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private const uint ButtonMessageClick = 0x00F5;
    private const uint WindowMessageGetFont = 0x0031;
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
            controls.All.Add(child);
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

    private static void AssertNativeControlsShareWindowFont(List<nint> controls)
    {
        Assert.Equal(8, controls.Count);
        var fonts = controls
            .Select(control => SendMessageW(control, WindowMessageGetFont, 0, 0))
            .ToArray();
        Assert.All(fonts, font => Assert.NotEqual(0, font));
        Assert.Single(fonts.Distinct());
        Assert.True(new SystemUiFontNativeApi().HasJapaneseGlyphs(fonts[0]));
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
        public List<nint> All { get; } = new();

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
