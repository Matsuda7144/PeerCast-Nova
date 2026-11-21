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
        var storePath = Path.Combine(directory.Path, "playback-preferences.json");
        var store = new JsonGenesisGuiNextPlaybackPreferencesStore(storePath);
        const string path = "C:\\Player Tools\\配信\\PCRPlayer64.exe";

        await store.SaveAsync(GenesisGuiNextPlaybackPreferences.Default);
        Assert.Equal(
            GenesisGuiNextPlaybackPreferences.Default,
            await store.LoadAsync());

        await File.WriteAllTextAsync(storePath, """{"PcrPlayerExecutablePath":"C:\\Player Tools\\配信\\PCRPlayer64.exe"}""");
        Assert.Equal(new(path, GenesisPlaybackPlayerMode.External), await store.LoadAsync());

        await store.SaveAsync(new(null));
        Assert.Null((await store.LoadAsync()).PcrPlayerExecutablePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybackPreferencesSurviveApplicationStateRestart(bool lowLatency)
    {
        using var directory = new TemporaryDirectory();
        var externalPath = directory.CreateExecutableFile("External");
        var libmpvPath = directory.CreateExecutableFile("Internal", "libmpv-2.dll");
        var storePath = Path.Combine(directory.Path, "playback-preferences.json");
        var saved = new GenesisGuiNextPlaybackPreferences(
            externalPath, GenesisPlaybackPlayerMode.Internal, libmpvPath, LowLatencyInternalPlayback: lowLatency);
        var source = new GenesisGuiNextPlaybackPreferencesSource();

        await using (var state = await LoadAsync(source))
        {
            Assert.Equal(GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
                state.Playback.Save(state.Playback.Preferences, saved).Result);
            Assert.Equal(saved, source.Current);
        }

        var restartedSource = new GenesisGuiNextPlaybackPreferencesSource();
        await using var restarted = await LoadAsync(restartedSource);
        Assert.Equal(saved, restarted.Playback.Preferences);
        Assert.Equal(saved, restartedSource.Current);

        ValueTask<GenesisGuiNextApplicationState> LoadAsync(GenesisGuiNextPlaybackPreferencesSource preferences) =>
            GenesisGuiNextApplicationState.LoadAsync(
                new EmptyFavoriteStore(), new EmptyNotificationStore(),
                DefaultGenesisYellowPageConfigurationStore.Instance,
                new JsonGenesisGuiNextPlaybackPreferencesStore(storePath), new SilentSoundPlayer(), preferences);
    }

    [Fact]
    public async Task LoadedAndUpdatedPathFlowsThroughApplicationStateToPlaybackSource()
    {
        using var directory = new TemporaryDirectory();
        var initialPath = directory.CreateExecutableFile("Initial");
        var updatedPath = directory.CreateExecutableFile("Updated", "PCRBrowser.exe");
        var environmentPath = directory.CreateExecutableFile("Environment");
        var pathPlayer = directory.CreateExecutableFile("Path");
        var storePath = Path.Combine(directory.Path, "playback-preferences.json");
        var store = new JsonGenesisGuiNextPlaybackPreferencesStore(storePath);
        await store.SaveAsync(new(initialPath));
        var pathSource = new GenesisGuiNextPlaybackPreferencesSource();
        await using var state = await GenesisGuiNextApplicationState.LoadAsync(
            new EmptyFavoriteStore(),
            new EmptyNotificationStore(),
            DefaultGenesisYellowPageConfigurationStore.Instance,
            store,
            new SilentSoundPlayer(),
            pathSource);

        Assert.Equal(initialPath, state.Playback.PcrPlayerExecutablePath);
        Assert.Equal(initialPath, pathSource.Current.PcrPlayerExecutablePath);
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            state.Playback.SetPcrPlayerExecutablePath($" \"{updatedPath}\" "));
        Assert.Equal(updatedPath, state.Playback.PcrPlayerExecutablePath);
        Assert.Equal(updatedPath, pathSource.Current.PcrPlayerExecutablePath);
        Assert.Equal(updatedPath, (await store.LoadAsync()).PcrPlayerExecutablePath);

        var restartedPathSource = new GenesisGuiNextPlaybackPreferencesSource();
        await using var restartedState = await GenesisGuiNextApplicationState.LoadAsync(
            new EmptyFavoriteStore(),
            new EmptyNotificationStore(),
            DefaultGenesisYellowPageConfigurationStore.Instance,
            new JsonGenesisGuiNextPlaybackPreferencesStore(storePath),
            new SilentSoundPlayer(),
            restartedPathSource);
        Assert.Equal(updatedPath, restartedState.Playback.PcrPlayerExecutablePath);
        Assert.Equal(updatedPath, restartedPathSource.Current.PcrPlayerExecutablePath);
        Assert.Equal(
            updatedPath,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(restartedPathSource.Current.PcrPlayerExecutablePath, environmentPath, null));

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            restartedState.Playback.SetPcrPlayerExecutablePath(" "));
        Assert.Null(restartedState.Playback.PcrPlayerExecutablePath);
        Assert.Null(restartedPathSource.Current.PcrPlayerExecutablePath);
        Assert.Null((await store.LoadAsync()).PcrPlayerExecutablePath);
        Assert.Equal(
            environmentPath,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                restartedPathSource.Current.PcrPlayerExecutablePath,
                environmentPath,
                System.IO.Path.GetDirectoryName(pathPlayer)));
        Assert.Equal(
            pathPlayer,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                restartedPathSource.Current.PcrPlayerExecutablePath,
                null,
                System.IO.Path.GetDirectoryName(pathPlayer)));
    }

    [Fact]
    public void FailedPersistenceDoesNotChangeConfiguredPath()
    {
        const string currentPath = "C:\\Player\\PCRPlayer64.exe";
        var source = new GenesisGuiNextPlaybackPreferencesSource();
        var settings = new GenesisGuiNextPlaybackSettings(new(currentPath), source, static _ => null);
        settings.BindPersistence(static _ => false);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed,
            settings.SetPcrPlayerExecutablePath("D:\\Missing Save\\PCRPlayer64.exe"));
        Assert.Equal(currentPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(currentPath, source.Current.PcrPlayerExecutablePath);
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
    public void OpenFileNameLayoutMatchesWin32PointerAndWordAlignment()
    {
        Assert.Equal(IntPtr.Size == 8 ? 152 : 88,
            Marshal.SizeOf<GenesisGuiNextPlaybackSettingsWindow.OpenFileName>());
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.Owner), 8, 4);
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.File), 48, 28);
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.MaximumFile), 56, 32);
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.FileOffset), 100, 56);
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.DefaultExtension), 104, 60);
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.Reserved), 136, 76);
        AssertOffset(nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.ExtendedFlags), 148, 84);

        static void AssertOffset(string field, int x64, int x86) =>
            Assert.Equal((nint)(IntPtr.Size == 8 ? x64 : x86),
                Marshal.OffsetOf<GenesisGuiNextPlaybackSettingsWindow.OpenFileName>(field));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowseMarshalsRealStructureAndReadsUnicodeSelectionWithoutOpeningDialog(bool library)
    {
        var initial = library ? @"C:\Player Tools\配信\libmpv-2.dll" : @"C:\Player Tools\配信\PCRPlayer64.exe";
        var selected = library ? @"D:\新しい Player\libmpv-2.dll" : @"D:\新しい Player\PCRBrowser.exe";
        var harness = new BrowseHarness();
        harness.ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName dialog) =>
        {
            Assert.Equal((nint)123, dialog.Owner);
            Assert.Equal((uint)Marshal.SizeOf<GenesisGuiNextPlaybackSettingsWindow.OpenFileName>(), dialog.StructSize);
            Assert.Equal((uint)GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity, dialog.MaximumFile);
            Assert.Equal(0x1808u, dialog.Flags);
            Assert.Equal(library ? "dll" : "exe", dialog.DefaultExtension);
            Assert.Equal(0, dialog.InitialDirectory);
            Assert.Equal(initial, Marshal.PtrToStringUni(dialog.File));
            Assert.Equal(0, Marshal.ReadInt16(dialog.File, initial.Length * sizeof(char)));
            Assert.Equal(0, Marshal.ReadInt16(dialog.File,
                (GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity - 1) * sizeof(char)));

            var native = Marshal.AllocHGlobal((int)dialog.StructSize);
            try
            {
                Marshal.StructureToPtr(dialog, native, false);
                try
                {
                    var copy = Marshal.PtrToStructure<GenesisGuiNextPlaybackSettingsWindow.OpenFileName>(native);
                    Assert.Equal(dialog.File, copy.File);
                    Assert.Equal(dialog.Owner, copy.Owner);
                    Assert.Equal(dialog.MaximumFile, copy.MaximumFile);
                    Assert.Equal(dialog.Title, copy.Title);
                    Assert.Equal(dialog.DefaultExtension, copy.DefaultExtension);
                    var filter = new char[dialog.Filter!.Length];
                    var offset = Marshal.OffsetOf<GenesisGuiNextPlaybackSettingsWindow.OpenFileName>(
                        nameof(GenesisGuiNextPlaybackSettingsWindow.OpenFileName.Filter));
                    Marshal.Copy(Marshal.ReadIntPtr(native, (int)offset), filter, 0, filter.Length);
                    Assert.Equal(library ? "libmpv DLL (*.dll)\0*.dll\0\0" :
                        "Executable files (*.exe)\0*.exe\0All files (*.*)\0*.*\0\0", new string(filter));
                }
                finally
                {
                    Marshal.DestroyStructure<GenesisGuiNextPlaybackSettingsWindow.OpenFileName>(native);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(native);
            }

            WriteSelection(dialog.File, selected);
            // A changed return pointer must not redirect our bounded buffer read/free.
            dialog.File = 0;
            return true;
        };
        harness.GetExtendedError = () => throw new InvalidOperationException("Not called on success.");

        var result = harness.Browse($"  \"{initial}\"  ", library);

        Assert.Equal(selected, result.SelectedPath);
        Assert.False(result.Failed);
        Assert.Equal(1, harness.FreeCount);
        Assert.Empty(harness.Diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("C:\\bad\0name\\PCRPlayer64.exe")]
    [InlineData("C:\\bad\nname\\PCRPlayer64.exe")]
    [InlineData("C:\\bad*name\\PCRPlayer64.exe")]
    [InlineData("C:\\bad\"name\\PCRPlayer64.exe")]
    public void BrowseLeavesUnsafeInitialNamesEmpty(string? initial)
    {
        var harness = new BrowseHarness
        {
            ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName dialog) =>
            {
                Assert.Equal(0, Marshal.ReadInt16(dialog.File));
                return false;
            },
        };

        var result = harness.Browse(initial);

        Assert.Null(result.SelectedPath);
        Assert.False(result.Failed);
        Assert.Equal(1, harness.FreeCount);
        Assert.Empty(harness.Diagnostics);
    }

    [Theory]
    [InlineData(32767, true)]
    [InlineData(32768, false)]
    [InlineData(32769, false)]
    public void BrowseInitialNameMustLeaveRoomForTerminator(int length, bool seeded)
    {
        var initial = @"C:\" + new string('p', length - 19) + @"\PCRPlayer64.exe";
        Assert.Equal(length, initial.Length);
        var harness = new BrowseHarness
        {
            ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName dialog) =>
            {
                Assert.Equal(seeded ? initial : string.Empty, Marshal.PtrToStringUni(dialog.File));
                Assert.Equal(0, Marshal.ReadInt16(dialog.File,
                    (GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity - 1) * sizeof(char)));
                return false;
            },
        };

        Assert.False(harness.Browse(initial).Failed);
        Assert.Equal(1, harness.FreeCount);
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(0x3003u, true)]
    public void BrowseCancellationAndFailureReturnNoReplacementAndReadErrorBeforeFree(uint error, bool failed)
    {
        var harness = new BrowseHarness();
        var errorReads = 0;
        harness.ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName dialog) =>
        {
            WriteSelection(dialog.File, @"D:\discarded\PCRPlayer64.exe");
            return false;
        };
        harness.GetExtendedError = () =>
        {
            Assert.Equal(0, harness.FreeCount);
            errorReads++;
            return error;
        };

        var result = harness.Browse(@"C:\private\PCRPlayer64.exe");

        Assert.Null(result.SelectedPath);
        Assert.Equal(failed, result.Failed);
        Assert.Equal(1, errorReads);
        Assert.Equal(1, harness.FreeCount);
        if (failed)
            Assert.Equal("External player browse failed: CommonDialogError=0x00003003.", Assert.Single(harness.Diagnostics));
        else
            Assert.Empty(harness.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowseRejectsEmptyOrUnterminatedSuccessBuffer(bool unterminated)
    {
        var harness = new BrowseHarness
        {
            ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName dialog) =>
            {
                var characters = new char[GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity];
                if (unterminated) Array.Fill(characters, 'x');
                Marshal.Copy(characters, 0, dialog.File, characters.Length);
                return true;
            },
        };

        var result = harness.Browse(null);

        Assert.Null(result.SelectedPath);
        Assert.True(result.Failed);
        Assert.Equal(1, harness.FreeCount);
        Assert.Equal("External player browse failed: InvalidSelectionBuffer.", Assert.Single(harness.Diagnostics));
    }

    [Fact]
    public void BrowseReadsTerminatorAtLastCharacterWithinCapacity()
    {
        var selected = new string('x', GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity - 1);
        var harness = new BrowseHarness
        {
            ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName dialog) =>
            {
                WriteSelection(dialog.File, selected);
                return true;
            },
        };

        Assert.Equal(selected, harness.Browse(null).SelectedPath);
        Assert.Equal(1, harness.FreeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowseContainsRecoverableInteropExceptionsWithoutLoggingPrivateDetails(bool marshalFailure)
    {
        var harness = new BrowseHarness
        {
            ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName _) =>
                throw (marshalFailure ? new MarshalDirectiveException(@"C:\private\path") :
                    new ArgumentException(@"C:\private\path")),
        };

        var result = harness.Browse(@"C:\private\PCRPlayer64.exe");

        Assert.Null(result.SelectedPath);
        Assert.True(result.Failed);
        Assert.Equal(1, harness.FreeCount);
        Assert.Equal($"External player browse failed: {(marshalFailure ? nameof(MarshalDirectiveException) : nameof(ArgumentException))}.",
            Assert.Single(harness.Diagnostics));
    }

    [Fact]
    public void BrowseDoesNotHideFatalFailuresAndStillFreesBuffer()
    {
        var harness = new BrowseHarness
        {
#pragma warning disable CA2201 // Simulate a fatal runtime failure without exhausting process memory.
            ShowDialog = (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName _) => throw new OutOfMemoryException(),
#pragma warning restore CA2201
        };

        Assert.Throws<OutOfMemoryException>(() => harness.Browse(null));
        Assert.Equal(1, harness.FreeCount);
        Assert.Empty(harness.Diagnostics);
    }

    [Fact]
    public void BrowseFailureSurvivesClosedDiagnosticStream()
    {
        var harness = new BrowseHarness
        {
            GetExtendedError = () => 1,
            ReportFailure = _ => throw new IOException("Closed stderr."),
        };

        Assert.True(harness.Browse(null).Failed);
        Assert.Equal(1, harness.FreeCount);
    }

    private static void WriteSelection(nint buffer, string selected)
    {
        var characters = (selected + '\0').ToCharArray();
        Assert.True(characters.Length <= GenesisGuiNextPlaybackSettingsWindow.BrowseFileCapacity);
        Marshal.Copy(characters, 0, buffer, characters.Length);
    }

    private sealed class BrowseHarness
    {
        public GenesisGuiNextPlaybackSettingsWindow.ShowOpenFileDialog ShowDialog { get; set; } =
            (ref GenesisGuiNextPlaybackSettingsWindow.OpenFileName _) => false;
        public Func<uint> GetExtendedError { get; set; } = () => 0;
        public Action<string>? ReportFailure { get; set; }
        public List<string> Diagnostics { get; } = [];
        public int FreeCount { get; private set; }

        public GenesisGuiNextPlaybackSettingsWindow.BrowseResult Browse(string? currentPath, bool library = false) =>
            GenesisGuiNextPlaybackSettingsWindow.BrowseForPlayerFile(123, currentPath,
                ShowDialog, GetExtendedError, buffer =>
                {
                    Assert.NotEqual(0, buffer);
                    FreeCount++;
                    Marshal.FreeHGlobal(buffer);
                }, ReportFailure ?? Diagnostics.Add, library);
    }

    [Theory]
    [InlineData("PCRPlayer64.exe")]
    [InlineData("PCRBrowser.exe")]
    [InlineData("OtherPlayer.exe")]
    [InlineData("OtherPlayer.EXE")]
    public void SavesExistingExeRegardlessOfName(string fileName)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateExecutableFile("Player", fileName);
        var settings = new GenesisGuiNextPlaybackSettings(GenesisGuiNextPlaybackPreferences.Default);

        Assert.Equal(GenesisGuiNextPlaybackSettingsUpdateResult.Saved, settings.SetPcrPlayerExecutablePath(path));
        Assert.Equal(path, settings.PcrPlayerExecutablePath);
    }

    [Theory]
    [InlineData("OtherPlayer.bat")]
    [InlineData("OtherPlayer.cmd")]
    [InlineData("OtherPlayer.ps1")]
    [InlineData("OtherPlayer.lnk")]
    [InlineData("OtherPlayer.exe.txt")]
    public void ExistingNonExeFileIsRejected(string fileName)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateExecutableFile("Player", fileName);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.UnsupportedFileExtension,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(path));
    }

    [Fact]
    public void ExecutablePathValidationRejectsMissingFilesDirectoriesAndInvalidPaths()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = Path.Combine(directory.Path, "Missing", "OtherPlayer.exe");
        var directoryPath = directory.CreateExecutableDirectory("Directory.exe");
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.FileNotFound,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(missingPath));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.DirectorySelected,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(directoryPath));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(Path.Combine(directory.Path, "invalid\0.exe")));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.InvalidPath,
            GenesisGuiNextPlaybackSettings.ValidateExecutablePath(Path.Combine("relative", "OtherPlayer.exe")));
    }

    [Fact]
    public void WindowsPathValidationCanUseManagedFileSystemBoundary()
    {
        const string windowsPath = @"C:\Player Tools\OtherPlayer.exe";
        static bool IsWindowsAbsolute(string path) =>
            path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        static string GetWindowsFileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

        Assert.Null(GenesisGuiNextPlaybackSettings.ValidatePlayerPath(
            windowsPath, ".exe", IsWindowsAbsolute, GetWindowsFileName, static _ => FileAttributes.Normal, static _ => { }));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.AccessDenied,
            GenesisGuiNextPlaybackSettings.ValidatePlayerPath(
                windowsPath, ".exe", IsWindowsAbsolute, GetWindowsFileName,
                static _ => FileAttributes.Normal, static _ => throw new UnauthorizedAccessException()));
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PathCheckFailed,
            GenesisGuiNextPlaybackSettings.ValidatePlayerPath(
                windowsPath, ".exe", IsWindowsAbsolute, GetWindowsFileName,
                static _ => FileAttributes.Normal, static _ => throw new IOException()));
    }

    [Fact]
    public async Task InvalidPathKeepsConfiguredSourceAndPersistedValueUntilCorrected()
    {
        using var directory = new TemporaryDirectory();
        var oldPath = directory.CreateExecutableFile("Old");
        var newPath = Path.Combine(directory.Path, "New", "OtherPlayer.exe");
        var source = new GenesisGuiNextPlaybackPreferencesSource();
        var store = new RecordingPlaybackPreferencesStore(new(oldPath));
        var settings = new GenesisGuiNextPlaybackSettings(new(oldPath), source);
        settings.BindPersistence(preferences =>
        {
            try
            {
                store.SaveAsync(preferences).AsTask().GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                return false;
            }
        });

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.FileNotFound,
            settings.SetPcrPlayerExecutablePath(newPath));
        Assert.Equal(oldPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(oldPath, source.Current.PcrPlayerExecutablePath);
        Assert.Equal(oldPath, (await store.LoadAsync()).PcrPlayerExecutablePath);
        Assert.Null(store.Saved);

        _ = directory.CreateExecutableFile("New", "OtherPlayer.exe");
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.Saved,
            settings.SetPcrPlayerExecutablePath(newPath));
        Assert.Equal(newPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(newPath, source.Current.PcrPlayerExecutablePath);
        Assert.Equal(newPath, store.Saved?.PcrPlayerExecutablePath);

        var failedSavePath = directory.CreateExecutableFile("FailedSave");
        store.FailSaves = true;
        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed,
            settings.SetPcrPlayerExecutablePath(failedSavePath));
        Assert.Equal(newPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(newPath, source.Current.PcrPlayerExecutablePath);
        Assert.Equal(newPath, (await store.LoadAsync()).PcrPlayerExecutablePath);
    }

    [Fact]
    public void ValidationAndPersistenceFailuresPreserveCurrentSettings()
    {
        const string currentPath = "C:\\Player\\PCRPlayer64.exe";
        const string newPath = "D:\\Player\\PCRPlayer64.exe";
        var source = new GenesisGuiNextPlaybackPreferencesSource();
        var settings = new GenesisGuiNextPlaybackSettings(
            new(currentPath),
            source,
            static _ => null);
        settings.BindPersistence(static _ => false);

        Assert.Equal(
            GenesisGuiNextPlaybackSettingsUpdateResult.PersistenceFailed,
            settings.SetPcrPlayerExecutablePath(newPath));
        Assert.Equal(currentPath, settings.PcrPlayerExecutablePath);
        Assert.Equal(currentPath, source.Current.PcrPlayerExecutablePath);
    }

    [Fact]
    public void SaveFeedbackExplainsValidationFailureAndClearsOnSuccess()
    {
        Assert.Equal(
            "指定した実行ファイルが見つかりません。",
            GenesisGuiNextPlaybackSettingsWindow.GetPlayerPathSaveFeedback(
                GenesisGuiNextPlaybackSettingsUpdateResult.FileNotFound,
                "C:\\Missing\\PCRPlayer64.exe"));
        Assert.Equal(
            "外部プレイヤーの設定を保存しました。次の再生から使用します。",
            GenesisGuiNextPlaybackSettingsWindow.GetPlayerPathSaveFeedback(
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

        using var directory = new TemporaryDirectory();
        var currentPath = directory.CreateExecutableFile("Old Player");
        var invalidPath = Path.Combine(directory.Path, "Missing Player", "OtherPlayer.exe");
        var path = directory.CreateExecutableFile("Player Tools", "OtherPlayer.exe");
        var pathSource = new GenesisGuiNextPlaybackPreferencesSource();
        var settings = new GenesisGuiNextPlaybackSettings(new(currentPath), pathSource);
        string? persistedPath = currentPath;
        settings.BindPersistence(value =>
        {
            persistedPath = value.PcrPlayerExecutablePath;
            return true;
        });

        var owner = CreateNativeWindow();
        Assert.NotEqual(0, owner);
        try
        {
            await using var window = GenesisGuiNextPlaybackSettingsWindow.TryCreate(owner, settings);
            Assert.NotNull(window);

            var settingsWindow = FindOwnedWindow(owner, "Playback / External Player");
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
            Assert.Equal(currentPath, pathSource.Current.PcrPlayerExecutablePath);

            Assert.True(SetWindowTextW(controls.Path, path));
            _ = SendMessageW(controls.Save, ButtonMessageClick, 0, 0);

            Assert.Equal("外部プレイヤーの設定を保存しました。次の再生から使用します。", ReadWindowText(controls.Status));
            Assert.Equal(path, persistedPath);
            Assert.Equal(path, settings.PcrPlayerExecutablePath);
            Assert.Equal(path, pathSource.Current.PcrPlayerExecutablePath);

            await window.DisposeAsync();
            await using var reopened = GenesisGuiNextPlaybackSettingsWindow.TryCreate(owner, settings);
            Assert.NotNull(reopened);
            var reopenedWindow = FindOwnedWindow(owner, "Playback / External Player");
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

        public string CreateExecutableFile(string directoryName, string fileName = PcrPlayerExternalPlayerLauncher.ExecutableFileName)
        {
            var filePath = System.IO.Path.Combine(
                System.IO.Path.Combine(Path, directoryName),
                fileName);
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
