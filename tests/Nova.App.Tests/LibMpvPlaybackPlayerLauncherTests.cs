using System.Threading.Channels;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Playback;
using PeerCast.Nova.Playback.Bbs;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class LibMpvPlaybackPlayerLauncherTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly DailyPlaybackPlayerSource Source = new(
        "http://127.0.0.1:45913/stream/531dc8dfc7fb42928ac2c0a626517a87",
        "http://127.0.0.1:45913/pls/531dc8dfc7fb42928ac2c0a626517a87.pls",
        "FLV test channel",
        null);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WaitsForWindowAndFileLoadedThenCleansUpOnStopOrWindowClose(bool closeByUser, bool lowLatency)
    {
        using var dll = new TestLibrary();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var client = new ControlledClient();
        var window = new ControlledWindow(() => Assert.Equal(1, client.DisposeCount));
        var windowReady = new TaskCompletionSource<ILibMpvPlaybackWindow>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var selectedDll = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path,
            path =>
            {
                selectedDll.SetResult(path);
                return client;
            },
            token => new(windowReady.Task.WaitAsync(token)),
            lowLatency: lowLatency);

        var launching = launcher.LaunchAsync(Source, timeout.Token).AsTask();
        var loadedBeforeWindow = selectedDll.Task.IsCompleted;
        windowReady.SetResult(window);
        var load = await client.Loaded.Task.WaitAsync(timeout.Token);
        var completedBeforeFileLoaded = launching.IsCompleted;
        client.Signal(LibMpvPlaybackEvent.FileLoaded);

        await using var player = await launching.WaitAsync(timeout.Token);
        Assert.False(loadedBeforeWindow);
        Assert.False(completedBeforeFileLoaded);
        Assert.Equal(dll.Path, await selectedDll.Task.WaitAsync(timeout.Token));
        Assert.Equal((Source.StreamUrl, PlaybackMediaContainer.FlashVideo, window.WindowId, lowLatency), load);

        if (closeByUser)
        {
            window.Close();
            await player.Completion.WaitAsync(timeout.Token);
        }
        else
        {
            await player.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        }

        Assert.Equal(1, client.DisposeCount);
        Assert.Equal(1, window.DisposeCount);
    }

    [Fact]
    public async Task CancellationDuringStreamOpenWakesClientAndReleasesWindow()
    {
        using var dll = new TestLibrary();
        using var cancellation = new CancellationTokenSource();
        var client = new ControlledClient();
        var window = new ControlledWindow(() => Assert.Equal(1, client.DisposeCount));
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => client, _ => new(window));

        var launching = launcher.LaunchAsync(Source, cancellation.Token).AsTask();
        try
        {
            await client.Loaded.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            cancellation.Cancel();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launching.WaitAsync(TestTimeout));
        Assert.Equal(1, client.DisposeCount);
        Assert.Equal(1, window.DisposeCount);
    }

    [Fact]
    public async Task NativeLoadFailurePropagatesAndClosesWindow()
    {
        using var dll = new TestLibrary();
        var window = new ControlledWindow(static () => { });
        var failure = new LibMpvPlaybackException("libmpv initialization failed.");
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => throw failure, _ => new(window));

        var error = await Assert.ThrowsAsync<LibMpvPlaybackException>(
            () => launcher.LaunchAsync(Source, CancellationToken.None).AsTask().WaitAsync(TestTimeout));

        Assert.Same(failure, error);
        Assert.Equal(1, window.DisposeCount);
    }

    [Theory]
    [InlineData("http://example.test:45913/stream/531dc8dfc7fb42928ac2c0a626517a87", PlaybackMediaContainer.FlashVideo)]
    [InlineData("http://127.0.0.1:45913/pls/531dc8dfc7fb42928ac2c0a626517a87.pls", PlaybackMediaContainer.FlashVideo)]
    [InlineData("http://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87", PlaybackMediaContainer.FlashVideo)]
    [InlineData("http://127.0.0.1:45913/stream/531dc8dfc7fb42928ac2c0a626517a87", PlaybackMediaContainer.FragmentedMp4)]
    [InlineData("http://127.0.0.1:45913/fmp4/00000000000000000000000000000000", PlaybackMediaContainer.FragmentedMp4)]
    [InlineData("http://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87?start=0", PlaybackMediaContainer.FragmentedMp4)]
    [InlineData("http://127.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87", PlaybackMediaContainer.FragmentedMp4)]
    [InlineData("http://127.0.0.1:45913/stream/../fmp4/531dc8dfc7fb42928ac2c0a626517a87", PlaybackMediaContainer.FragmentedMp4)]
    [InlineData("http://127.0.0.1:45913/fmp4/531dc8dfc7fb42928ac2c0a626517a87", PlaybackMediaContainer.Unknown)]
    public async Task OnlyMatchingLoopbackStreamUrlsReachTheEmbeddedClient(string url, PlaybackMediaContainer container)
    {
        using var dll = new TestLibrary();
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path,
            _ => throw new InvalidOperationException("The runtime must remain unopened."),
            _ => throw new InvalidOperationException("The window must remain unopened."));

        var error = await Assert.ThrowsAsync<LibMpvPlaybackException>(() =>
            launcher.LaunchAsync(Source with { StreamUrl = url, Container = container }, CancellationToken.None).AsTask());
        Assert.Contains("loopback HTTP stream URL", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlaybackMediaContainer.FlashVideo, "flv", false)]
    [InlineData(PlaybackMediaContainer.FlashVideo, "flv", true)]
    [InlineData(PlaybackMediaContainer.FragmentedMp4, "mov", false)]
    [InlineData(PlaybackMediaContainer.FragmentedMp4, "mov", true)]
    public void StartupOptionsApplyBufferSettingsOnlyInLowLatencyMode(
        PlaybackMediaContainer container, string demuxer, bool lowLatency)
    {
        var applied = new List<(string Name, string Value)>();
        LibMpvPlaybackClient.SetStartupOptions(
            (name, value) => applied.Add((name, value)), container, 42, new(75, Muted: true), lowLatency);

        var expected = new List<(string Name, string Value)>
        {
            ("config", "no"),
            ("load-scripts", "no"),
            ("input-default-bindings", "no"),
            ("wid", "42"),
            ("hwdec", "auto-safe"),
            ("demuxer", "lavf"),
            ("demuxer-lavf-format", demuxer),
            ("volume", "75"),
            ("mute", "yes"),
        };
        if (lowLatency)
        {
            expected.AddRange(
            [
                ("demuxer-readahead-secs", "0.3"),
                ("cache-secs", "0.3"),
                ("cache-pause-wait", "0.15"),
                ("cache-pause-initial", "no"),
                ("demuxer-lavf-o", "fflags=+nobuffer"),
                ("demuxer-lavf-analyzeduration", "0.25"),
                ("stream-buffer-size", "4k"),
                ("audio-buffer", "0.05"),
                ("video-latency-hacks", "yes"),
                ("vd-lavc-threads", "1"),
            ]);
        }
        Assert.Equal(expected, applied);
    }

    [Fact]
    public async Task RejectedLowLatencyOptionExplainsTheFallbackAndReleasesTheClientAndWindow()
    {
        using var dll = new TestLibrary();
        var nativeFailure = new LibMpvPlaybackException("libmpv rejected cache-secs.");
        var client = new ControlledClient((container, windowId, audio, lowLatency) =>
            LibMpvPlaybackClient.SetStartupOptions((name, _) =>
            {
                if (name == "cache-secs")
                {
                    throw nativeFailure;
                }
            }, container, windowId, audio, lowLatency));
        var window = new ControlledWindow(() => Assert.Equal(1, client.DisposeCount));
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => client, _ => new(window), lowLatency: true);

        var error = await Assert.ThrowsAsync<LibMpvPlaybackException>(() =>
            launcher.LaunchAsync(Source, CancellationToken.None).AsTask().WaitAsync(TestTimeout));

        Assert.Contains("標準に切り替えて", error.Message, StringComparison.Ordinal);
        Assert.Same(nativeFailure, error.InnerException);
        Assert.False(client.Loaded.Task.IsCompleted);
        Assert.Equal(1, client.DisposeCount);
        Assert.Equal(1, window.DisposeCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Fmp4OptionPassesTheHostUrlAndCleansUpOnEndOrCancellation(bool cancel, bool lowLatency)
    {
        using var dll = new TestLibrary();
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var error = new StringWriter();
        await using var host = new PlaybackFmp4HttpStreamHost(
            new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()));
        var client = new ControlledClient();
        var window = new ControlledWindow(() => Assert.Equal(1, client.DisposeCount));
        var url = host.StreamUri.AbsoluteUri;
        string[] args = ["--play-fmp4", url, "--libmpv-library", dll.Path];
        if (lowLatency)
        {
            args = [.. args, "--low-latency"];
        }

        var app = NovaApplicationEntrypoint.RunAsync(
            args,
            TextWriter.Null,
            error,
            (options, token) =>
            {
                Assert.Equal(new NovaPlaybackLaunchOptions(null, dll.Path, url, lowLatency), options);
                var launcher = new LibMpvPlaybackPlayerLauncher(
                    options.LibMpvLibraryPath!, _ => client, _ => new(window), lowLatency: options.LowLatency);
                return NovaApplicationEntrypoint.PlayFmp4Async(options.Fmp4StreamUrl!, launcher, error, token);
            },
            cancellation.Token).AsTask();

        try
        {
            var load = await client.Loaded.Task.WaitAsync(timeout.Token);
            Assert.Equal((url, PlaybackMediaContainer.FragmentedMp4, window.WindowId, lowLatency), load);
            Assert.False(app.IsCompleted);
            client.Signal(LibMpvPlaybackEvent.FileLoaded);
            if (cancel)
            {
                cancellation.Cancel();
            }
            else
            {
                client.Signal(LibMpvPlaybackEvent.Ended);
            }

            Assert.Equal(0, await app.WaitAsync(timeout.Token));
            Assert.Empty(error.ToString());
            Assert.Equal(1, client.DisposeCount);
            Assert.Equal(1, window.DisposeCount);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    [Fact]
    public void MissingLibraryIsRejectedAtConfigurationValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "libmpv-2.dll");
        var launcher = new LibMpvPlaybackPlayerLauncher(path);
        var error = Assert.Throws<LibMpvPlaybackException>(launcher.ValidateConfiguration);
        Assert.Contains("--libmpv-library", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioControllerAdjustsVolumeFromMouseWheelAndClampsBounds()
    {
        var controller = new LibMpvPlaybackAudioController(new LibMpvPlaybackAudioState(50, Muted: false));
        var observed = new List<LibMpvPlaybackAudioState>();
        controller.StateChanged += observed.Add;

        // Fractional wheel deltas accumulate until one 120-unit notch is reached.
        Assert.False(controller.ApplyWheelDelta(60));
        Assert.True(controller.ApplyWheelDelta(60));
        Assert.Equal(55, controller.Current.Volume);

        // Reversing wheel direction discards prior remainder and steps downward.
        Assert.False(controller.ApplyWheelDelta(60));
        Assert.True(controller.ApplyWheelDelta(-120));
        Assert.Equal(50, controller.Current.Volume);

        // Clamping at 0 and 100 never emits out-of-range values.
        Assert.True(controller.ApplyWheelDelta(-120 * 20));
        Assert.Equal(0, controller.Current.Volume);
        Assert.False(controller.ApplyWheelDelta(-120));

        Assert.True(controller.ApplyWheelDelta(120 * 30));
        Assert.Equal(100, controller.Current.Volume);
        Assert.False(controller.ApplyWheelDelta(120));

        Assert.Equal(4, observed.Count);
    }

    [Fact]
    public void AudioControllerUnmutesAutomaticallyWhenRaisingVolume()
    {
        var controller = new LibMpvPlaybackAudioController(new LibMpvPlaybackAudioState(40, Muted: false));
        Assert.True(controller.ToggleMute());
        Assert.True(controller.Current.Muted);
        Assert.Equal("Unmute", controller.Current.FormatMuteButtonText());
        Assert.Equal("Muted", controller.Current.FormatVolumeLabel());

        // Lowering volume while muted keeps mute active.
        Assert.True(controller.ApplyWheelDelta(-120));
        Assert.Equal(35, controller.Current.Volume);
        Assert.True(controller.Current.Muted);

        // Raising volume via wheel unmutes automatically.
        Assert.True(controller.ApplyWheelDelta(120));
        Assert.Equal(40, controller.Current.Volume);
        Assert.False(controller.Current.Muted);
        Assert.Equal("Mute", controller.Current.FormatMuteButtonText());
        Assert.Equal("40%", controller.Current.FormatVolumeLabel());
    }

    [Fact]
    public async Task VolumeChangesPropagateToClientAndPersistAcrossSessions()
    {
        using var dll = new TestLibrary();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var audioStore = new LibMpvPlaybackAudioSessionStore(new LibMpvPlaybackAudioState(75, Muted: false));

        var firstClient = new ControlledClient();
        var firstWindow = new ControlledWindow(static () => { }, new LibMpvPlaybackAudioState(75, Muted: false));
        var firstInitialAudio = default(LibMpvPlaybackAudioState);
        var firstLauncher = new LibMpvPlaybackPlayerLauncher(
            dll.Path,
            _ => firstClient,
            (_, initialAudio, _) =>
            {
                firstInitialAudio = initialAudio;
                return new(firstWindow);
            },
            audioStore);

        var firstLaunching = firstLauncher.LaunchAsync(Source, timeout.Token).AsTask();
        await firstClient.Loaded.Task.WaitAsync(timeout.Token);
        firstClient.Signal(LibMpvPlaybackEvent.FileLoaded);
        await using (var firstPlayer = await firstLaunching.WaitAsync(timeout.Token))
        {
            Assert.Equal(new LibMpvPlaybackAudioState(75, Muted: false), firstInitialAudio);
            Assert.Equal(new LibMpvPlaybackAudioState(75, Muted: false), firstClient.InitialAudio);

            firstWindow.RaiseAudioChanged(new LibMpvPlaybackAudioState(60, Muted: false));
            firstWindow.RaiseAudioChanged(new LibMpvPlaybackAudioState(60, Muted: true));

            Assert.Equal([60, 60], firstClient.Volumes);
            Assert.Equal([false, true], firstClient.Mutes);
            await firstPlayer.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        }

        // Next playback session receives the last audio state from the shared session store.
        var secondClient = new ControlledClient();
        var secondInitialAudio = default(LibMpvPlaybackAudioState);
        var secondWindow = new ControlledWindow(static () => { }, audioStore.Current);
        var secondLauncher = new LibMpvPlaybackPlayerLauncher(
            dll.Path,
            _ => secondClient,
            (_, initialAudio, _) =>
            {
                secondInitialAudio = initialAudio;
                return new(secondWindow);
            },
            audioStore);

        var secondLaunching = secondLauncher.LaunchAsync(Source, timeout.Token).AsTask();
        await secondClient.Loaded.Task.WaitAsync(timeout.Token);
        secondClient.Signal(LibMpvPlaybackEvent.FileLoaded);
        await using (await secondLaunching.WaitAsync(timeout.Token))
        {
            Assert.Equal(new LibMpvPlaybackAudioState(60, Muted: true), secondInitialAudio);
            Assert.Equal(new LibMpvPlaybackAudioState(60, Muted: true), secondClient.InitialAudio);
        }
    }

    [Fact]
    public void FormatLatestPostOsdAssEscapesAssBracesAndFormatsBottomLeftOverlay()
    {
        var post = new BbsPost(901, "名無し", "sage", "2026/11/22 20:35:06", "ID1", "初心者大歓迎 {テスト}\n2行目");
        var osd = LibMpvPlaybackClient.FormatLatestPostOsdAss(post);

        Assert.StartsWith("${osd-ass-cc/0}{\\an1", osd, StringComparison.Ordinal);
        Assert.Contains(">>901 初心者大歓迎 \\{テスト\\} 2行目", osd, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowSnapClampsNearWorkAreaEdgesAndLeavesCenterUnchanged()
    {
        // Snaps top-left within 12px.
        Assert.Equal((0, 0), LibMpvPlaybackWindowState.SnapToWorkArea(9, 10, 640, 360, 0, 0, 1920, 1040));

        // Snaps bottom-right within 12px.
        Assert.Equal((1280, 680), LibMpvPlaybackWindowState.SnapToWorkArea(1271, 688, 640, 360, 0, 0, 1920, 1040));

        // Leaves middle positions untouched.
        Assert.Equal((300, 200), LibMpvPlaybackWindowState.SnapToWorkArea(300, 200, 640, 360, 0, 0, 1920, 1040));
    }

    [Fact]
    public void WindowStateStorePersistsAndReloadsNormalizedState()
    {
        var tempDir = Directory.CreateTempSubdirectory("nova-winstate-");
        try
        {
            var path = Path.Combine(tempDir.FullName, "embedded-player-window.json");
            var store = new LibMpvPlaybackWindowStateStore(path);
            store.Update(new LibMpvPlaybackWindowState(
                Left: 120,
                Top: 80,
                VideoWidth: 1280,
                VideoHeight: 720,
                Volume: 45,
                Muted: true,
                AlwaysOnTop: true,
                Borderless: true,
                BbsUseSage: false));

            var reloaded = new LibMpvPlaybackWindowStateStore(path);
            Assert.Equal(120, reloaded.Current.Left);
            Assert.Equal(80, reloaded.Current.Top);
            Assert.Equal(1280, reloaded.Current.VideoWidth);
            Assert.Equal(720, reloaded.Current.VideoHeight);
            Assert.Equal(45, reloaded.Current.Volume);
            Assert.True(reloaded.Current.Muted);
            Assert.True(reloaded.Current.AlwaysOnTop);
            Assert.True(reloaded.Current.Borderless);
            Assert.False(reloaded.Current.BbsUseSage);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void PlaybackStatsAndVideoAdjustmentFormatAndClampCleanly()
    {
        var stats = new LibMpvPlaybackStats(1280, 720, 59.94, 2_381_000, 0.25);
        Assert.Equal("1280x720 2381 kbps (60)", stats.FormatCompact());

        var partial = new LibMpvPlaybackStats(0, 0, null, null, null);
        Assert.Equal(string.Empty, partial.FormatCompact());

        var clamped = new LibMpvVideoAdjustment(Brightness: 150, Contrast: -140, Saturation: 20, Gamma: 0).Clamped();
        Assert.Equal(new LibMpvVideoAdjustment(100, -100, 20, 0), clamped);
        Assert.False(clamped.IsDefault);
        Assert.True(LibMpvVideoAdjustment.Default.IsDefault);

        var file = LibMpvScreenshotPaths.CreateFilePath(@"C:\Shots", new DateTimeOffset(2026, 11, 22, 22, 30, 5, 123, TimeSpan.Zero));
        Assert.EndsWith("Nova_20261122_223005_123.png", file, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptionOverlayHitTestsMinimizeMaximizeCloseRegions()
    {
        Assert.Equal(LibMpvCaptionOverlayButton.Minimize, LibMpvPlaybackCaptionOverlay.HitTestButton(5, 10));
        Assert.Equal(LibMpvCaptionOverlayButton.Maximize, LibMpvPlaybackCaptionOverlay.HitTestButton(35, 10));
        Assert.Equal(LibMpvCaptionOverlayButton.Close, LibMpvPlaybackCaptionOverlay.HitTestButton(75, 10));
        Assert.Equal(LibMpvCaptionOverlayButton.None, LibMpvPlaybackCaptionOverlay.HitTestButton(-1, 10));
        Assert.Equal(LibMpvCaptionOverlayButton.None, LibMpvPlaybackCaptionOverlay.HitTestButton(95, 10));
        Assert.Equal(LibMpvCaptionOverlayButton.None, LibMpvPlaybackCaptionOverlay.HitTestButton(10, 30));
    }

    [Fact]
    public void ScaleVideoSizeComputesPresetsAndAutoResizePersists()
    {
        Assert.Equal((320, 180), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 25));
        Assert.Equal((640, 360), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 50));
        Assert.Equal((960, 540), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 75));
        Assert.Equal((1280, 720), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 100));
        Assert.Equal((1600, 900), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 125));
        Assert.Equal((1920, 1080), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 150));
        Assert.Equal((2560, 1440), LibMpvPlaybackWindowState.ScaleVideoSize(1280, 720, 200));
        Assert.Equal((160, 90), LibMpvPlaybackWindowState.ScaleVideoSize(320, 180, 25));

        var tempDir = Directory.CreateTempSubdirectory("nova-scale-");
        try
        {
            var path = Path.Combine(tempDir.FullName, "embedded-player-window.json");
            var store = new LibMpvPlaybackWindowStateStore(path);
            Assert.False(store.Current.AutoResizeTo100PercentOnStart);

            store.Update(store.Current with { AutoResizeTo100PercentOnStart = true });
            var reloaded = new LibMpvPlaybackWindowStateStore(path);
            Assert.True(reloaded.Current.AutoResizeTo100PercentOnStart);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PauseAndSpeedChangesPropagateToClientAndDisplayOsd()
    {
        using var dll = new TestLibrary();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var client = new ControlledClient();
        var window = new ControlledWindow(static () => { });
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => client, _ => new(window));

        var launching = launcher.LaunchAsync(Source, timeout.Token).AsTask();
        await client.Loaded.Task.WaitAsync(timeout.Token);
        client.Signal(LibMpvPlaybackEvent.FileLoaded);
        await using var player = await launching.WaitAsync(timeout.Token);

        window.RaisePauseChanged(true);
        window.RaisePauseChanged(false);
        window.RaiseSpeedChanged(1.25);
        window.RaiseSpeedChanged(5.0);
        window.RaiseSpeedChanged(1.0);

        Assert.Equal([true, false], client.Pauses);
        Assert.Equal([1.25, 4.0, 1.0], client.Speeds);
        Assert.Contains(client.OsdMessages, message => message.Text == "一時停止");
        Assert.Contains(client.OsdMessages, message => message.Text == "再生再開");
        Assert.Contains(client.OsdMessages, message => message.Text == "再生速度: 1.25x");
        Assert.Contains(client.OsdMessages, message => message.Text == "再生速度: 4x");
        Assert.Contains(client.OsdMessages, message => message.Text == "再生速度: 1x");
    }

    [Fact]
    public void SpeedClampingAndPresetSteppingStayWithinBounds()
    {
        Assert.Equal(1.0, LibMpvPlaybackClient.ClampSpeed(double.NaN));
        Assert.Equal(1.0, LibMpvPlaybackClient.ClampSpeed(double.PositiveInfinity));
        Assert.Equal(LibMpvPlaybackClient.MinSpeed, LibMpvPlaybackClient.ClampSpeed(0.1));
        Assert.Equal(LibMpvPlaybackClient.MaxSpeed, LibMpvPlaybackClient.ClampSpeed(8.0));

        Assert.Equal(1.25, LibMpvPlaybackClient.StepSpeed(1.0, +1));
        Assert.Equal(1.5, LibMpvPlaybackClient.StepSpeed(1.25, +1));
        Assert.Equal(2.0, LibMpvPlaybackClient.StepSpeed(2.0, +1));
        Assert.Equal(0.75, LibMpvPlaybackClient.StepSpeed(1.0, -1));
        Assert.Equal(0.5, LibMpvPlaybackClient.StepSpeed(0.5, -1));
        Assert.Equal("1.25x", LibMpvPlaybackClient.FormatSpeedLabel(1.25));
        Assert.Equal("1x", LibMpvPlaybackClient.FormatSpeedLabel(1.0));
    }

    [Fact]
    public async Task WindowReloadRequest_ResetsPauseAndSpeedReloadsClientAndShowsOsd()
    {
        using var dll = new TestLibrary();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var client = new ControlledClient();
        var window = new ControlledWindow(static () => { });
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => client, _ => new(window));

        var launching = launcher.LaunchAsync(Source, timeout.Token).AsTask();
        await client.Loaded.Task.WaitAsync(timeout.Token);
        client.Signal(LibMpvPlaybackEvent.FileLoaded);
        await using var player = await launching.WaitAsync(timeout.Token);

        window.RaisePauseChanged(true);
        window.RaiseSpeedChanged(1.5);
        window.RaiseReloadRequested();

        Assert.Equal([true, false], client.Pauses);
        Assert.Equal([1.5, 1.0], client.Speeds);
        Assert.Equal(1, client.ReloadCount);
        Assert.Contains(client.OsdMessages, message => message.Text == "ストリームを再読込しました");
    }

    [Fact]
    public void LibMpvPlaybackClient_ReloadEndFileSuppression_HandlesStopEofQuitAndError()
    {
        var pending = 1;
        Assert.True(LibMpvPlaybackClient.ShouldSuppressEndFileForReload(LibMpvPlaybackClient.EndFileStop, ref pending));
        Assert.Equal(0, pending);
        Assert.False(LibMpvPlaybackClient.ShouldSuppressEndFileForReload(LibMpvPlaybackClient.EndFileStop, ref pending));

        pending = 1;
        Assert.True(LibMpvPlaybackClient.ShouldSuppressEndFileForReload(0, ref pending));
        Assert.Equal(0, pending);

        pending = 1;
        Assert.False(LibMpvPlaybackClient.ShouldSuppressEndFileForReload(LibMpvPlaybackClient.EndFileQuit, ref pending));
        Assert.Equal(1, pending);

        Assert.False(LibMpvPlaybackClient.ShouldSuppressEndFileForReload(LibMpvPlaybackClient.EndFileError, ref pending));
        Assert.Equal(1, pending);
    }

    [Fact]
    public async Task WindowSeekRequest_ClampsPropagatesToClientAndShowsOsd()
    {
        using var dll = new TestLibrary();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var client = new ControlledClient();
        var window = new ControlledWindow(static () => { });
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => client, _ => new(window));

        var launching = launcher.LaunchAsync(Source, timeout.Token).AsTask();
        await client.Loaded.Task.WaitAsync(timeout.Token);
        client.Signal(LibMpvPlaybackEvent.FileLoaded);
        await using var player = await launching.WaitAsync(timeout.Token);

        window.RaiseSeekRequested(-5);
        window.RaiseSeekRequested(30);
        window.RaiseSeekRequested(999);
        window.RaiseSeekRequested(0);
        window.RaiseSeekRequested(double.NaN);

        Assert.Equal([-5.0, 30.0, 300.0], client.Seeks);
        Assert.Contains(client.OsdMessages, message => message.Text == "シーク: -5秒");
        Assert.Contains(client.OsdMessages, message => message.Text == "シーク: +30秒");
        Assert.Contains(client.OsdMessages, message => message.Text == "シーク: +300秒");
    }

    [Fact]
    public void LibMpvPlaybackClient_UserConfigParserAndLoader_AppliesSafeOptionsAndSkipsReservedOptions()
    {
        const string config = """
            # 先頭コメント
            [high-quality]
            hwdec=d3d11va
            --scale=ewa_lanczossharp # 行末コメント
            profile="gpu-hq"
            deband
            no-audio-display
            wid=12345
            demuxer=lavf
            load-scripts=yes
            include=C:\evil\extra.conf
            keep-open=always
            """;

        var parsed = LibMpvPlaybackClient.ParseUserConfig(config);
        Assert.Equal(
        [
            ("hwdec", "d3d11va"),
            ("scale", "ewa_lanczossharp"),
            ("profile", "gpu-hq"),
            ("deband", "yes"),
            ("audio-display", "no"),
        ], parsed);

        var tempDir = Directory.CreateTempSubdirectory("nova-mpv-conf-");
        try
        {
            var missingPath = Path.Combine(tempDir.FullName, "missing.conf");
            var applied = new List<(string Name, string Value)>();
            Assert.Equal(0, LibMpvPlaybackClient.ApplyUserConfigIfPresent((k, v) => applied.Add((k, v)), missingPath));
            Assert.Empty(applied);

            var confPath = Path.Combine(tempDir.FullName, "mpv.conf");
            File.WriteAllText(confPath, config);
            Assert.Equal(5, LibMpvPlaybackClient.ApplyUserConfigIfPresent((k, v) => applied.Add((k, v)), confPath));
            Assert.Equal(parsed, applied);
            Assert.EndsWith(Path.Combine("PeerCast Nova", "mpv.conf"), LibMpvPlaybackClient.ResolveUserConfigPath(tempDir.FullName), StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LibMpvPlaybackClient_AudioDeviceListParser_ParsesDeduplicatesAndHandlesInvalidJson()
    {
        const string json = """
            [
              {"name": "auto", "description": "Autoselect device"},
              {"name": "wasapi/{1111}", "description": "Speakers (Realtek)"},
              {"name": "wasapi/{1111}", "description": "Duplicate"},
              {"name": "wasapi/{2222}", "description": ""}
            ]
            """;

        var devices = LibMpvPlaybackClient.ParseAudioDeviceList(json);
        Assert.Equal(
        [
            new LibMpvAudioDevice("auto", "Autoselect device"),
            new LibMpvAudioDevice("wasapi/{1111}", "Speakers (Realtek)"),
            new LibMpvAudioDevice("wasapi/{2222}", "wasapi/{2222}"),
        ], devices);

        Assert.Empty(LibMpvPlaybackClient.ParseAudioDeviceList(null));
        Assert.Empty(LibMpvPlaybackClient.ParseAudioDeviceList("not json"));
        Assert.Empty(LibMpvPlaybackClient.ParseAudioDeviceList("{}"));
    }

    [Fact]
    public async Task WindowAudioDeviceSelection_PropagatesToClientAndShowsOsd()
    {
        using var dll = new TestLibrary();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var client = new ControlledClient
        {
            AudioDevices =
            [
                new LibMpvAudioDevice("auto", "Autoselect device"),
                new LibMpvAudioDevice("wasapi/{1111}", "ヘッドホン (USB DAC)"),
            ],
            CurrentAudioDevice = "auto",
        };
        var window = new ControlledWindow(static () => { });
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path, _ => client, _ => new(window));

        var launching = launcher.LaunchAsync(Source, timeout.Token).AsTask();
        await client.Loaded.Task.WaitAsync(timeout.Token);
        client.Signal(LibMpvPlaybackEvent.FileLoaded);
        await using var player = await launching.WaitAsync(timeout.Token);

        Assert.NotNull(window.AudioDevicesProvider);
        var offered = window.AudioDevicesProvider!();
        Assert.Equal(2, offered.Devices.Count);
        Assert.Equal("auto", offered.CurrentDevice);

        window.RaiseAudioDeviceSelected("wasapi/{1111}");
        window.RaiseAudioDeviceSelected("auto");

        Assert.Equal(["wasapi/{1111}", "auto"], client.SelectedAudioDevices);
        Assert.Equal("auto", client.CurrentAudioDevice);
        Assert.Contains(client.OsdMessages, message => message.Text == "音声出力: ヘッドホン (USB DAC)");
        Assert.Contains(client.OsdMessages, message => message.Text == "音声出力: 自動選択");
    }

    private sealed class ControlledClient(
        Action<PlaybackMediaContainer, uint, LibMpvPlaybackAudioState, bool>? setStartupOptions = null) : ILibMpvPlaybackClient
    {
        private readonly Channel<LibMpvPlaybackEvent> _events = Channel.CreateUnbounded<LibMpvPlaybackEvent>();
        public TaskCompletionSource<(string StreamUrl, PlaybackMediaContainer Container, uint WindowId, bool LowLatency)> Loaded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LibMpvPlaybackAudioState InitialAudio { get; private set; }
        public List<int> Volumes { get; } = [];
        public List<bool> Mutes { get; } = [];
        public List<bool> Pauses { get; } = [];
        public List<double> Speeds { get; } = [];
        public List<(string Text, int DurationMs)> OsdMessages { get; } = [];
        public List<double> Seeks { get; } = [];
        public List<string> SelectedAudioDevices { get; } = [];
        public IReadOnlyList<LibMpvAudioDevice> AudioDevices { get; init; } = [];
        public string? CurrentAudioDevice { get; set; }
        public int ReloadCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void LoadFile(
            string streamUrl, PlaybackMediaContainer container, uint windowId, LibMpvPlaybackAudioState initialAudio, bool lowLatency)
        {
            setStartupOptions?.Invoke(container, windowId, initialAudio, lowLatency);
            InitialAudio = initialAudio;
            Loaded.SetResult((streamUrl, container, windowId, lowLatency));
        }

        public void SetVolume(int volume) => Volumes.Add(volume);
        public void SetMuted(bool muted) => Mutes.Add(muted);
        public void SetPaused(bool paused) => Pauses.Add(paused);
        public void SetSpeed(double speed) => Speeds.Add(speed);
        public void Reload() => ReloadCount++;
        public void SeekRelative(double seconds) => Seeks.Add(seconds);
        public IReadOnlyList<LibMpvAudioDevice> GetAudioDevices() => AudioDevices;
        public string? GetCurrentAudioDevice() => CurrentAudioDevice;
        public void SetAudioDevice(string deviceName)
        {
            CurrentAudioDevice = deviceName;
            SelectedAudioDevices.Add(deviceName);
        }
        public void ShowOsdText(string text, int durationMs) => OsdMessages.Add((text, durationMs));
        public bool SaveScreenshot(string filePath) => true;
        public void SetVideoAdjustment(LibMpvVideoAdjustment adjustment) { }
        public LibMpvPlaybackStats? TryReadStats() => null;
        public LibMpvPlaybackEvent WaitForEvent() => _events.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        public void Wakeup() => Signal(LibMpvPlaybackEvent.Other);
        public void Dispose() => DisposeCount++;

        public void Signal(LibMpvPlaybackEvent notification) => _events.Writer.TryWrite(notification);
    }

    private sealed class ControlledWindow(
        Action verifyShutdownOrder,
        LibMpvPlaybackAudioState? initialAudio = null) : ILibMpvPlaybackWindow
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private LibMpvPlaybackAudioState _audioState = (initialAudio ?? LibMpvPlaybackAudioState.Default).Normalized();
        public uint WindowId => 0x8001_0223;
        public LibMpvPlaybackAudioState AudioState => _audioState;
        public event Action<LibMpvPlaybackAudioState>? AudioStateChanged;
        public event Action<bool>? PauseChanged;
        public event Action<double>? SpeedChanged;
        public event Action? ReloadRequested;
        public event Action<double>? SeekRequested;
        public Func<(IReadOnlyList<LibMpvAudioDevice> Devices, string? CurrentDevice)>? AudioDevicesProvider { get; set; }
        public event Action<string>? AudioDeviceSelected;
        public Task Closed => _closed.Task;
        public int DisposeCount { get; private set; }

        public void RaiseAudioChanged(LibMpvPlaybackAudioState state)
        {
            _audioState = state.Normalized();
            AudioStateChanged?.Invoke(_audioState);
        }

        public void RaisePauseChanged(bool paused) => PauseChanged?.Invoke(paused);

        public void RaiseSpeedChanged(double speed) => SpeedChanged?.Invoke(speed);

        public void RaiseReloadRequested() => ReloadRequested?.Invoke();

        public void RaiseSeekRequested(double seconds) => SeekRequested?.Invoke(seconds);

        public void RaiseAudioDeviceSelected(string deviceName) => AudioDeviceSelected?.Invoke(deviceName);

        public void Close() => _closed.TrySetResult();

        public ValueTask DisposeAsync()
        {
            verifyShutdownOrder();
            DisposeCount++;
            Close();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestLibrary : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("nova-libmpv-");

        public TestLibrary()
        {
            Path = System.IO.Path.Combine(_directory.FullName, "libmpv-2.dll");
            File.WriteAllBytes(Path, []);
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
            _directory.Delete();
        }
    }
}
