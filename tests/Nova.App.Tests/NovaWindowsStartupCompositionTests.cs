using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaWindowsStartupCompositionTests
{
    [Fact]
    public async Task RunUsesOneSharedHostAndCleansUpAfterNativeClose()
    {
        var events = new List<string>();
        var host = new FakeHost(events);
        var factory = new FakeFactory(host, events);
        var composition = new NovaWindowsStartupComposition(new(), factory);

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Completed, result);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(["Create", "Start", "Wait", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task StartupFailureStillStopsAndDisposesPartialHost()
    {
        var events = new List<string>();
        var host = new FakeHost(
            events,
            startResult: PlaybackWindowsPhysicalRunnerResult.Faulted);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.Faulted, result);
        Assert.Equal(["Create", "Start", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task CancellationEndsRunAndPerformsFiniteShutdown()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var host = new FakeHost(
            events,
            wait: async token =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return PlaybackWindowsPhysicalRunnerResult.Stopped;
            });
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync(cancellation.Token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NovaWindowsStartupResult.Cancelled, result);
        Assert.Equal(["Create", "Start", "Wait", "Stop", "Dispose"], events);
    }

    [Fact]
    public async Task UnsupportedPlatformFailsClosedWithoutRunningApplicationBody()
    {
        var events = new List<string>();
        var host = new FakeHost(
            events,
            startResult: PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported);
        var composition = new NovaWindowsStartupComposition(
            new(),
            new FakeFactory(host, events));

        var result = await composition.RunAsync();

        Assert.Equal(NovaWindowsStartupResult.PlatformUnsupported, result);
        Assert.Equal(0, host.WaitCount);
        Assert.Equal(["Create", "Start", "Stop", "Dispose"], events);
    }

    [Fact]
    public void DefaultApplicationGraphHasNoPhysicalCommandOrFfmpegBinaryReference()
    {
        var project = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        var program = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "Program.cs"));

        Assert.Contains(@"..\Nova.Windows\Nova.Windows.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova.Cli", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova.Playback", project, StringComparison.Ordinal);
        Assert.DoesNotContain("FFmpeg\\**\\*", project, StringComparison.Ordinal);
        Assert.DoesNotContain(".dll", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".exe", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".zip", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Physical", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Readiness", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Probe", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostic", program, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }

    private sealed class FakeFactory : INovaWindowsStartupHostFactory
    {
        private readonly FakeHost _host;
        private readonly List<string> _events;

        public FakeFactory(FakeHost host, List<string> events)
        {
            _host = host;
            _events = events;
        }

        public int CreateCount { get; private set; }

        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options)
        {
            Assert.Equal("PeerCast Nova", options.Title);
            CreateCount++;
            _events.Add("Create");
            return _host;
        }
    }

    private sealed class FakeHost : INovaWindowsStartupHost
    {
        private readonly List<string> _events;
        private readonly PlaybackWindowsPhysicalRunnerResult _startResult;
        private readonly Func<CancellationToken, ValueTask<PlaybackWindowsPhysicalRunnerResult>>
            _wait;

        public FakeHost(
            List<string> events,
            PlaybackWindowsPhysicalRunnerResult startResult =
                PlaybackWindowsPhysicalRunnerResult.Started,
            Func<CancellationToken, ValueTask<PlaybackWindowsPhysicalRunnerResult>>? wait = null)
        {
            _events = events;
            _startResult = startResult;
            _wait = wait ?? (_ => ValueTask.FromResult(
                PlaybackWindowsPhysicalRunnerResult.Stopped));
        }

        public int WaitCount { get; private set; }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Start");
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_startResult);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Wait");
            WaitCount++;
            return _wait(cancellationToken);
        }

        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("Stop");
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }

        public ValueTask DisposeAsync()
        {
            _events.Add("Dispose");
            return ValueTask.CompletedTask;
        }
    }
}
