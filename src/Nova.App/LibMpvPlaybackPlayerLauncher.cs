using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal interface ILibMpvPlaybackWindow : IAsyncDisposable
{
    uint WindowId { get; }
    Task Closed { get; }
}

internal sealed class LibMpvPlaybackPlayerLauncher : IDailyPlaybackPlayerLauncher
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);
    private readonly string _libraryPath;
    private readonly Func<string, ILibMpvPlaybackClient> _openClient;
    private readonly Func<CancellationToken, ValueTask<ILibMpvPlaybackWindow>> _openWindow;

    public LibMpvPlaybackPlayerLauncher(string libraryPath)
        : this(libraryPath, LibMpvPlaybackClient.Open, OpenWindowAsync)
    {
    }

    internal LibMpvPlaybackPlayerLauncher(
        string libraryPath,
        Func<string, ILibMpvPlaybackClient> openClient,
        Func<CancellationToken, ValueTask<ILibMpvPlaybackWindow>> openWindow)
    {
        _libraryPath = libraryPath;
        _openClient = openClient;
        _openWindow = openWindow;
    }

    internal static bool IsLibraryPath(string path) =>
        Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);

    public void ValidateConfiguration()
    {
        if (!IsLibraryPath(_libraryPath) || !File.Exists(_libraryPath))
        {
            throw new LibMpvPlaybackException(
                "Specify an existing absolute libmpv DLL path in Playback Settings or with --libmpv-library.");
        }
    }

    public async ValueTask<IDailyPlaybackPlayerLifetime> LaunchAsync(
        DailyPlaybackPlayerSource source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConfiguration();
        if (!IsLoopbackFlvStream(source.StreamUrl))
        {
            throw new LibMpvPlaybackException("Embedded playback requires Nova's loopback HTTP-FLV stream URL.");
        }

        var window = await _openWindow(cancellationToken).ConfigureAwait(false);
        var lifetime = new Lifetime(window, () => _openClient(_libraryPath), source.StreamUrl);
        try
        {
            await lifetime.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
            return lifetime;
        }
        catch
        {
            await lifetime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsLoopbackFlvStream(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttp &&
        uri.Host == "127.0.0.1" &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath.StartsWith("/stream/", StringComparison.Ordinal) &&
        Guid.TryParseExact(uri.AbsolutePath.AsSpan(8), "N", out var channelId) &&
        channelId != Guid.Empty;

    private static async ValueTask<ILibMpvPlaybackWindow> OpenWindowAsync(CancellationToken cancellationToken)
    {
        var host = new PlaybackWindowsPhysicalRunnerHost(new()
        {
            Title = "PeerCast Nova - FLV Playback",
            ClientWidth = 960,
            ClientHeight = 540,
        });
        try
        {
            var started = await host.StartAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Win32's wid option expects the unsigned 32-bit value of the HWND.
            var windowId = unchecked((uint)host.Snapshot.WindowToken);
            if (started != PlaybackWindowsPhysicalRunnerResult.Started || windowId == 0)
            {
                throw new LibMpvPlaybackException("The embedded playback window could not be opened.");
            }
            return new PlaybackWindow(host, windowId);
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class PlaybackWindow(PlaybackWindowsPhysicalRunnerHost host, uint windowId)
        : ILibMpvPlaybackWindow
    {
        public uint WindowId { get; } = windowId;
        public Task Closed { get; } = WaitForCloseAsync(host);

        public ValueTask DisposeAsync() => host.DisposeAsync();

        private static async Task WaitForCloseAsync(PlaybackWindowsPhysicalRunnerHost host)
        {
            if (await host.WaitForExitAsync().ConfigureAwait(false) != PlaybackWindowsPhysicalRunnerResult.Stopped)
            {
                throw new LibMpvPlaybackException("The embedded playback window ended unexpectedly.");
            }
        }
    }

    private sealed class Lifetime : IDailyPlaybackPlayerLifetime
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _fileLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public Lifetime(ILibMpvPlaybackWindow window, Func<ILibMpvPlaybackClient> openClient, string streamUrl)
        {
            Completion = RunAsync(window, openClient, streamUrl);
        }

        public Task Completion { get; }

        public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.WhenAny(_fileLoaded.Task, Completion)
                    .WaitAsync(OpenTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new LibMpvPlaybackException("libmpv timed out opening the HTTP-FLV stream.");
            }

            if (Completion.IsCompleted)
            {
                await Completion.ConfigureAwait(false);
                throw new LibMpvPlaybackException("Embedded playback ended before the HTTP-FLV stream opened.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            var ownsStop = Interlocked.Exchange(ref _disposed, 1) == 0;
            if (ownsStop)
            {
                _stop.Cancel();
            }
            try
            {
                await Completion.ConfigureAwait(false);
            }
            catch (LibMpvPlaybackException)
            {
                // Startup and playback observers receive this failure from Completion.
            }
            finally
            {
                if (ownsStop)
                {
                    _stop.Dispose();
                }
            }
        }

        private async Task RunAsync(
            ILibMpvPlaybackWindow window, Func<ILibMpvPlaybackClient> openClient, string streamUrl)
        {
            try
            {
                var player = Task.Factory.StartNew(() => RunPlayer(window, openClient, streamUrl),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                if (await Task.WhenAny(player, window.Closed).ConfigureAwait(false) == window.Closed)
                {
                    _stop.Cancel();
                }
                await player.ConfigureAwait(false);
            }
            finally
            {
                await window.DisposeAsync().ConfigureAwait(false);
            }

            await window.Closed.ConfigureAwait(false);
        }

        private void RunPlayer(
            ILibMpvPlaybackWindow window, Func<ILibMpvPlaybackClient> openClient, string streamUrl)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            using var client = openClient();
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            client.LoadFile(streamUrl, window.WindowId);
            // Unregister and join any in-flight wakeup before releasing the mpv handle.
            using var wakeup = _stop.Token.UnsafeRegister(
                static state => ((ILibMpvPlaybackClient)state!).Wakeup(), client);
            while (!_stop.IsCancellationRequested)
            {
                var notification = client.WaitForEvent();
                if (notification == LibMpvPlaybackEvent.Ended)
                {
                    return;
                }
                if (notification == LibMpvPlaybackEvent.FileLoaded)
                {
                    _fileLoaded.TrySetResult();
                }
            }
        }
    }
}
