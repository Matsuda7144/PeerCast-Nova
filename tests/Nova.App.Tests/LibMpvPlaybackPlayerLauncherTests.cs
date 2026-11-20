using System.Threading.Channels;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitsForWindowAndFileLoadedThenCleansUpOnStopOrWindowClose(bool closeByUser)
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
            token => new(windowReady.Task.WaitAsync(token)));

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
        Assert.Equal((Source.StreamUrl, window.WindowId), load);

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
    [InlineData("http://example.test:45913/stream/531dc8dfc7fb42928ac2c0a626517a87")]
    [InlineData("http://127.0.0.1:45913/pls/531dc8dfc7fb42928ac2c0a626517a87.pls")]
    public async Task OnlyLoopbackStreamUrlsReachTheEmbeddedClient(string url)
    {
        using var dll = new TestLibrary();
        var launcher = new LibMpvPlaybackPlayerLauncher(dll.Path,
            _ => throw new InvalidOperationException("The runtime must remain unopened."),
            _ => throw new InvalidOperationException("The window must remain unopened."));

        var error = await Assert.ThrowsAsync<LibMpvPlaybackException>(() =>
            launcher.LaunchAsync(Source with { StreamUrl = url }, CancellationToken.None).AsTask());
        Assert.Contains("loopback HTTP-FLV stream URL", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingLibraryIsRejectedAtConfigurationValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "libmpv-2.dll");
        var launcher = new LibMpvPlaybackPlayerLauncher(path);
        var error = Assert.Throws<LibMpvPlaybackException>(launcher.ValidateConfiguration);
        Assert.Contains("--libmpv-library", error.Message, StringComparison.Ordinal);
    }

    private sealed class ControlledClient : ILibMpvPlaybackClient
    {
        private readonly Channel<LibMpvPlaybackEvent> _events = Channel.CreateUnbounded<LibMpvPlaybackEvent>();
        public TaskCompletionSource<(string StreamUrl, uint WindowId)> Loaded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }

        public void LoadFile(string streamUrl, uint windowId) => Loaded.SetResult((streamUrl, windowId));
        public LibMpvPlaybackEvent WaitForEvent() => _events.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        public void Wakeup() => Signal(LibMpvPlaybackEvent.Other);
        public void Dispose() => DisposeCount++;

        public void Signal(LibMpvPlaybackEvent notification) => _events.Writer.TryWrite(notification);
    }

    private sealed class ControlledWindow(Action verifyShutdownOrder) : ILibMpvPlaybackWindow
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public uint WindowId => 0x8001_0223;
        public Task Closed => _closed.Task;
        public int DisposeCount { get; private set; }

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
