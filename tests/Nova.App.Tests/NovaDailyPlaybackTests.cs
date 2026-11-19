using System.Diagnostics;
using System.Net;
using PeerCast.Nova.Network.Tests;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaDailyPlaybackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DailyGuiPortSetting_UsesDesiredStateControlAndPersists(bool finishOn)
    {
        using var directory = new TemporaryDirectory();
        var store = new NovaSettingsStore(Path.Combine(directory.Path, "settings.json"));
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, store, new(), false);
        var window = new FakeWindow { PortMappingSequence = finishOn ? [true, false, true] : [true, false] };
        var relayController = new FakeRelayController { PortMappingControl = control };
        var composition = new NovaDailyPlaybackStartupComposition(null,
            new FakeWindowFactory(window), new FakeControllerFactory(new FakeController()),
            new FakeCatalog(new GenesisChannelItem("test channel",
                "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87")),
            new FakeRelayControllerFactory(relayController));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.False((await window.FirstPortView.Task).Enabled);
        Assert.Equal(finishOn, test.Manager.Enabled);
        Assert.Equal(finishOn, store.Load().AutoPortMapping);
        Assert.Equal(finishOn, window.LastPortView!.Enabled);
        Assert.Contains("外部接続: 未確認", window.LastPortView.Summary);
        Assert.Contains("[Disabled]", window.DisabledPortView!.Diagnostics);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public void PcrPlayerExecutableResolution_UsesExplicitThenEnvironmentThenPath()
    {
        using var directory = new TemporaryDirectory();
        var explicitDirectory = directory.CreateSubdirectory("explicit");
        var environmentDirectory = directory.CreateSubdirectory("environment");
        var pathDirectory = directory.CreateSubdirectory("path");
        var explicitPath = TemporaryDirectory.CreateFile(explicitDirectory, "PCRBrowser.exe");
        var environmentPath = TemporaryDirectory.CreateFile(environmentDirectory, "OtherPlayer.exe");
        var pathPlayer = TemporaryDirectory.CreateFile(pathDirectory, "PCRPlayer64.exe");

        Assert.Equal(
            explicitPath,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                explicitPath,
                environmentPath,
                pathDirectory));
        Assert.Equal(
            environmentPath,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                null,
                environmentPath,
                pathDirectory));
        Assert.Equal(
            pathPlayer,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                null,
                null,
                pathDirectory));
        Assert.Equal(
            pathPlayer,
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath("PCRPlayer64.exe", environmentPath, pathDirectory));
        Assert.Throws<PcrPlayerExecutableResolutionException>(() =>
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(null, null, environmentDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PcrPlayerExecutableResolution_InvalidConfiguredPathDoesNotFallback(bool useEnvironment)
    {
        using var directory = new TemporaryDirectory();
        var pathDirectory = directory.CreateSubdirectory("path");
        var environmentPath = TemporaryDirectory.CreateFile(
            pathDirectory,
            "PCRPlayer64.exe");
        var missingPath = Path.Combine(directory.Path, "missing", "OtherPlayer.exe");

        var exception = Assert.Throws<PcrPlayerExecutableResolutionException>(() =>
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                useEnvironment ? null : missingPath,
                useEnvironment ? missingPath : environmentPath,
                pathDirectory));

        Assert.Contains("no fallback was attempted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PcrPlayerExecutableResolution_RejectsNonExeAndRelativeOverrides()
    {
        using var directory = new TemporaryDirectory();
        var nonExe = TemporaryDirectory.CreateFile(directory.CreateSubdirectory("player"), "OtherPlayer.cmd");

        Assert.Throws<PcrPlayerExecutableResolutionException>(() =>
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(nonExe, null, null));
        Assert.Throws<PcrPlayerExecutableResolutionException>(() =>
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(null, Path.Combine("tools", "OtherPlayer.exe"), null));
    }

    [Fact]
    public void PcrPlayerStartInfo_PassesOnlyDynamicLoopbackUrlAsArgument()
    {
        const string streamUrl = "http://127.0.0.1:57133/stream/531DC8DFC7FB42928AC2C0A626517A87";

        ProcessStartInfo startInfo = PcrPlayerExternalPlayerLauncher.CreateStartInfo(
            "C:\\tools\\PCRPlayer64.exe",
            streamUrl);

        Assert.Equal("C:\\tools\\PCRPlayer64.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal([streamUrl], startInfo.ArgumentList);
        Assert.Throws<ArgumentException>(() =>
            PcrPlayerExternalPlayerLauncher.CreateStartInfo(
                "PCRPlayer64.exe",
                "http://example.com/stream/channel"));
    }

    [Fact]
    public async Task ChannelCatalog_LoadsOnlyValidChannelSpecificPcpSources()
    {
        var fields = Enumerable.Repeat(string.Empty, 18).ToArray();
        fields[0] = "daily test";
        fields[1] = "531DC8DFC7FB42928AC2C0A626517A87";
        fields[2] = "tracker.example:7144";
        fields[4] = "music";
        fields[5] = "live";
        fields[8] = "192";
        fields[9] = "FLV";
        fields[15] = "01:30";
        var textSource = new FakeIndexTextSource(string.Join("<>", fields));
        var catalog = new HttpPeerCastIndexChannelCatalog(
            new Uri("http://index.example/channels.txt"),
            new Uri("pcp://fallback.example:7146/"),
            textSource);

        var snapshot = await catalog.ReadAsync();

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("daily test", channel.Name);
        Assert.Equal(
            "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87",
            channel.Source);
        Assert.Equal(192, channel.BitrateKbps);
    }

    [Fact]
    public void DailyPcpPlaybackSourceParser_RequiresChannelSpecificPcpUri()
    {
        const string source = "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

        Assert.True(DailyPcpPlaybackSourceParser.TryParse(source, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal("tracker.example", parsed.Host);
        Assert.Equal(7144, parsed.Port);
        Assert.Equal(Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"), parsed.ChannelId.Value);
        Assert.False(DailyPcpPlaybackSourceParser.TryParse(
            "pcp://tracker.example:7144/channel/invalid",
            out _));
    }

    [Fact]
    public async Task DailyGuiPlay_UsesSelectedCatalogChannelAndDailyController()
    {
        const string source = "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";
        var channel = new GenesisChannelItem("test channel", source);
        var window = new FakeWindow();
        var controller = new FakeController();
        var relayController = new FakeRelayController();
        var composition = new NovaDailyPlaybackStartupComposition(
            explicitPlayerPath: null,
            new FakeWindowFactory(window),
            new FakeControllerFactory(controller),
            new FakeCatalog(channel),
            new FakeRelayControllerFactory(relayController));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(NovaDailyPlaybackStartupComposition.WindowTitle, window.Title);
        Assert.Equal(["test channel"], window.ChannelNames);
        Assert.Equal(source, controller.PlayedSource);
        Assert.True(controller.StopCalled);
        Assert.False(controller.ManagedWindowRequested);
    }

    [Fact]
    public async Task DailyGuiRelay_UsesSelectedCatalogChannelAndStopsOnSelectionChange()
    {
        const string source = "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";
        var window = new FakeWindow
        {
            RequestRelayOnExit = true,
            ChangeSelectionAfterRelay = true,
        };
        var relayController = new FakeRelayController();
        var composition = new NovaDailyPlaybackStartupComposition(
            explicitPlayerPath: null,
            new FakeWindowFactory(window),
            new FakeControllerFactory(new FakeController()),
            new FakeCatalog(new GenesisChannelItem("relay channel", source)),
            new FakeRelayControllerFactory(relayController));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(source, relayController.StartedSource);
        Assert.Equal(4, relayController.StopCount);
        Assert.Equal(string.Empty, window.RelayEndpoint);
        Assert.DoesNotContain("managed", relayController.StartedSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DailyGuiShutdown_StopsActiveRelayOwner()
    {
        const string source = "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";
        var window = new FakeWindow
        {
            RequestRelayOnExit = true,
            StopRelayAfterStart = false,
        };
        var relayController = new FakeRelayController();
        var composition = new NovaDailyPlaybackStartupComposition(
            explicitPlayerPath: null,
            new FakeWindowFactory(window),
            new FakeControllerFactory(new FakeController()),
            new FakeCatalog(new GenesisChannelItem("relay channel", source)),
            new FakeRelayControllerFactory(relayController));

        _ = await composition.RunAsync();

        Assert.Equal(source, relayController.StartedSource);
        Assert.Equal(2, relayController.StopCount);
        Assert.Equal(string.Empty, window.RelayEndpoint);
    }

    [Fact]
    public void RelayEndpoint_ContainsChannelAndExcludesLoopbackAndLinkLocalAddresses()
    {
        var channelId = new PeerCast.Nova.Core.Channels.ChannelId(
            Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

        var endpoints = NovaDailyPcpRelayController.CreateLanEndpoints(
            channelId,
            7144,
            [
                IPAddress.Parse("192.168.1.8"),
                IPAddress.Loopback,
                IPAddress.Parse("169.254.1.2"),
                IPAddress.Parse("10.0.0.5"),
            ]);

        Assert.Equal(
            [
                "pcp://10.0.0.5:7144/channel/531DC8DFC7FB42928AC2C0A626517A87",
                "pcp://192.168.1.8:7144/channel/531DC8DFC7FB42928AC2C0A626517A87",
            ],
            endpoints);
    }

    [Fact]
    public async Task DailyCommandLine_ForwardsExplicitExecutablePathToApplication()
    {
        string? receivedPath = null;
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await NovaApplicationEntrypoint.RunDailyAsync(
            ["--pcrplayer-executable", "C:\\tools\\PCRPlayer64.exe"],
            output,
            error,
            (path, _) =>
            {
                receivedPath = path;
                return ValueTask.FromResult(NovaWindowsStartupResult.Completed);
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal("C:\\tools\\PCRPlayer64.exe", receivedPath);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void DailyCommandLine_RejectsMissingOrDuplicateExecutablePath()
    {
        Assert.False(NovaDailyPlaybackCommandLine.TryParse(
            ["--pcrplayer-executable"],
            out _,
            out _));
        Assert.False(NovaDailyPlaybackCommandLine.TryParse(
            ["--pcrplayer-executable", "a.exe", "--pcrplayer-executable", "b.exe"],
            out _,
            out _));
    }

    private sealed class FakeWindowFactory(FakeWindow window) : INovaDailyPlaybackWindowFactory
    {
        public INovaDailyPlaybackWindow Create(string title)
        {
            window.Title = title;
            return window;
        }
    }

    private sealed class FakeControllerFactory(FakeController controller) :
        INovaDailyPlaybackControllerFactory
    {
        public INovaDailyPlaybackController Create(
            string? explicitPlayerPath,
            Action<string> setStatus,
            Action<string> showError) => controller;
    }

    private sealed class FakeRelayControllerFactory(FakeRelayController controller) :
        INovaDailyRelayControllerFactory
    {
        public INovaDailyRelayController Create(
            Action<string> setStatus,
            Action<string> setEndpoint,
            Action<string> showError)
        {
            controller.EndpointCallback = setEndpoint;
            return controller;
        }
    }

    private sealed class FakeCatalog(GenesisChannelItem channel) : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new GenesisChannelCatalogSnapshot([channel]));
        }
    }

    private sealed class FakeIndexTextSource(string text) : IPeerCastIndexTextSource
    {
        public ValueTask<string> ReadAsync(
            Uri indexUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(text);
        }
    }

    private sealed class FakeWindow : INovaDailyPlaybackWindow, INovaDailyPortMappingWindow
    {
        private readonly object portGate = new();
        public bool[] PortMappingSequence { get; init; } = [];
        public TaskCompletionSource<NovaPortMappingView> FirstPortView { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NovaPortMappingView? LastPortView { get; private set; }
        public NovaPortMappingView? DisabledPortView { get; private set; }
        public event Action<bool>? PortMappingEnabledChanged;

        public void SetPortMapping(NovaPortMappingView view)
        {
            lock (portGate)
            {
                if (LastPortView is not null && view.Generation < LastPortView.Generation)
                {
                    return;
                }
                LastPortView = view;
                if (!view.Enabled)
                {
                    DisabledPortView = view;
                }
                FirstPortView.TrySetResult(view);
            }
        }
        public string Title { get; set; } = string.Empty;

        public IReadOnlyList<string> ChannelNames { get; private set; } = [];

        public bool RequestRelayOnExit { get; init; }

        public bool ChangeSelectionAfterRelay { get; init; }

        public bool StopRelayAfterStart { get; init; } = true;

        public string RelayEndpoint { get; private set; } = string.Empty;

        public event Action? RefreshRequested;

        public event Action<int>? PlayRequested;

        public event Action? StopRequested;

        public event Action<int>? RelayRequested;

        public event Action? StopRelayRequested;

        public event Action? ChannelSelectionChanged;

        public ValueTask<NovaDailyPlaybackWindowResult> StartAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NovaDailyPlaybackWindowResult.Started);

        public async Task<NovaDailyPlaybackWindowResult> WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            if (PortMappingSequence.Length > 0)
            {
                await FirstPortView.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                foreach (var enabled in PortMappingSequence)
                {
                    PortMappingEnabledChanged?.Invoke(enabled);
                }
            }
            RefreshRequested?.Invoke();
            SetChannels(["test channel"]);
            PlayRequested?.Invoke(0);
            StopRequested?.Invoke();
            if (RequestRelayOnExit)
            {
                RelayRequested?.Invoke(0);
                if (ChangeSelectionAfterRelay)
                {
                    ChannelSelectionChanged?.Invoke();
                }

                if (StopRelayAfterStart)
                {
                    StopRelayRequested?.Invoke();
                }
            }

            return NovaDailyPlaybackWindowResult.Stopped;
        }

        public void SetChannels(IReadOnlyList<string> channelNames) => ChannelNames = channelNames;

        public void SetStatus(string status)
        {
        }

        public void SetRelayEndpoint(string endpoint) => RelayEndpoint = endpoint;

        public void ShowError(string message) => throw new InvalidOperationException(message);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRelayController : INovaDailyRelayController, INovaDailyPortMappingControl
    {
        public NovaPortMappingControl? PortMappingControl { get; init; }
        public string? StartedSource { get; private set; }

        public int StopCount { get; private set; }

        public Action<string>? EndpointCallback { get; set; }

        public Task StartAsync(GenesisChannelItem channel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedSource = channel.Source;
            EndpointCallback?.Invoke("pcp://192.168.1.2:7144/channel/531DC8DFC7FB42928AC2C0A626517A87");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            EndpointCallback?.Invoke(string.Empty);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeController : INovaDailyPlaybackController
    {
        public string? PlayedSource { get; private set; }

        public bool StopCalled { get; private set; }

        public bool ManagedWindowRequested { get; private set; }

        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded;

        public Task PlayAsync(GenesisChannelItem channel, CancellationToken cancellationToken)
        {
            PlayedSource = channel.Source;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalled = true;
            return Task.CompletedTask;
        }

        public void CompletePlayback(bool failed = false) =>
            PlaybackEnded?.Invoke(new(PlayedSource ?? string.Empty, failed));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"NovaDailyPlayback-{Guid.NewGuid():N}");

        public string CreateSubdirectory(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string CreateFile(string directory, string name)
        {
            var path = System.IO.Path.Combine(directory, name);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
