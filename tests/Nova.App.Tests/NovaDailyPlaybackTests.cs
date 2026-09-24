using System.Diagnostics;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaDailyPlaybackTests
{
    [Fact]
    public void PcrPlayerExecutableResolution_UsesExplicitThenEnvironmentThenPath()
    {
        using var directory = new TemporaryDirectory();
        var explicitDirectory = directory.CreateSubdirectory("explicit");
        var environmentDirectory = directory.CreateSubdirectory("environment");
        var pathDirectory = directory.CreateSubdirectory("path");
        var explicitPath = TemporaryDirectory.CreateFile(explicitDirectory, "PCRPlayer64.exe");
        var environmentPath = TemporaryDirectory.CreateFile(environmentDirectory, "PCRPlayer64.exe");
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
    }

    [Fact]
    public void PcrPlayerExecutableResolution_InvalidExplicitPathDoesNotFallback()
    {
        using var directory = new TemporaryDirectory();
        var environmentPath = TemporaryDirectory.CreateFile(
            directory.CreateSubdirectory("environment"),
            "PCRPlayer64.exe");

        var exception = Assert.Throws<PcrPlayerExecutableResolutionException>(() =>
            PcrPlayerExternalPlayerLauncher.ResolveExecutablePath(
                Path.Combine(directory.Path, "missing", "PCRPlayer64.exe"),
                environmentPath,
                null));

        Assert.Contains("no fallback was attempted", exception.Message, StringComparison.Ordinal);
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
        var composition = new NovaDailyPlaybackStartupComposition(
            explicitPlayerPath: null,
            new FakeWindowFactory(window),
            new FakeControllerFactory(controller),
            new FakeCatalog(channel));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(NovaDailyPlaybackStartupComposition.WindowTitle, window.Title);
        Assert.Equal(["test channel"], window.ChannelNames);
        Assert.Equal(source, controller.PlayedSource);
        Assert.True(controller.StopCalled);
        Assert.False(controller.ManagedWindowRequested);
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

    private sealed class FakeWindow : INovaDailyPlaybackWindow
    {
        public string Title { get; set; } = string.Empty;

        public IReadOnlyList<string> ChannelNames { get; private set; } = [];

        public event Action? RefreshRequested;

        public event Action<int>? PlayRequested;

        public event Action? StopRequested;

        public ValueTask<NovaDailyPlaybackWindowResult> StartAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NovaDailyPlaybackWindowResult.Started);

        public Task<NovaDailyPlaybackWindowResult> WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            RefreshRequested?.Invoke();
            SetChannels(["test channel"]);
            PlayRequested?.Invoke(0);
            StopRequested?.Invoke();
            return Task.FromResult(NovaDailyPlaybackWindowResult.Stopped);
        }

        public void SetChannels(IReadOnlyList<string> channelNames) => ChannelNames = channelNames;

        public void SetStatus(string status)
        {
        }

        public void ShowError(string message) => throw new InvalidOperationException(message);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeController : INovaDailyPlaybackController
    {
        public string? PlayedSource { get; private set; }

        public bool StopCalled { get; private set; }

        public bool ManagedWindowRequested { get; private set; }

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
