using System.Globalization;
using PeerCast.Nova.Playback;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal interface ILibMpvPlaybackWindow : IAsyncDisposable
{
    uint WindowId { get; }
    LibMpvPlaybackAudioState AudioState { get; }
    event Action<LibMpvPlaybackAudioState>? AudioStateChanged;
    Task Closed { get; }
}

internal sealed class LibMpvPlaybackAudioSessionStore
{
    private readonly object _sync = new();
    private LibMpvPlaybackAudioState _state;

    public LibMpvPlaybackAudioSessionStore(LibMpvPlaybackAudioState? initialState = null)
    {
        _state = (initialState ?? LibMpvPlaybackAudioState.Default).Normalized();
    }

    public LibMpvPlaybackAudioState Current
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public void Update(LibMpvPlaybackAudioState state)
    {
        var normalized = state.Normalized();
        lock (_sync)
        {
            _state = normalized;
        }
    }
}

internal sealed class LibMpvPlaybackPlayerLauncher : IDailyPlaybackPlayerLauncher
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);
    private readonly string _libraryPath;
    private readonly LibMpvPlaybackAudioSessionStore _audioStore;
    private readonly bool _lowLatency;
    private readonly Func<string, ILibMpvPlaybackClient> _openClient;
    private readonly Func<DailyPlaybackPlayerSource, LibMpvPlaybackAudioState, CancellationToken, ValueTask<ILibMpvPlaybackWindow>> _openWindow;

    public LibMpvPlaybackPlayerLauncher(
        string libraryPath,
        LibMpvPlaybackAudioSessionStore? audioStore = null,
        bool lowLatency = false)
        : this(
            libraryPath,
            LibMpvPlaybackClient.Open,
            OpenWindowAsync,
            audioStore,
            lowLatency)
    {
    }

    internal LibMpvPlaybackPlayerLauncher(
        string libraryPath,
        Func<string, ILibMpvPlaybackClient> openClient,
        Func<CancellationToken, ValueTask<ILibMpvPlaybackWindow>> openWindow,
        LibMpvPlaybackAudioSessionStore? audioStore = null,
        bool lowLatency = false)
        : this(
            libraryPath,
            openClient,
            (_, _, cancellationToken) => openWindow(cancellationToken),
            audioStore,
            lowLatency)
    {
    }

    internal LibMpvPlaybackPlayerLauncher(
        string libraryPath,
        Func<string, ILibMpvPlaybackClient> openClient,
        Func<DailyPlaybackPlayerSource, LibMpvPlaybackAudioState, CancellationToken, ValueTask<ILibMpvPlaybackWindow>> openWindow,
        LibMpvPlaybackAudioSessionStore? audioStore = null,
        bool lowLatency = false)
    {
        _libraryPath = libraryPath;
        _audioStore = audioStore ?? new();
        _lowLatency = lowLatency;
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
        if (!IsLoopbackStream(source.StreamUrl, source.Container))
        {
            throw new LibMpvPlaybackException("Embedded playback requires Nova's loopback HTTP stream URL for the selected format.");
        }

        var window = await _openWindow(source, _audioStore.Current, cancellationToken).ConfigureAwait(false);
        var lifetime = new Lifetime(window, () => _openClient(_libraryPath), source, _audioStore, _lowLatency);
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

    internal static bool IsLoopbackStream(string url, PlaybackMediaContainer container)
    {
        var pathPrefix = container switch
        {
            PlaybackMediaContainer.FlashVideo => "/stream/",
            PlaybackMediaContainer.FragmentedMp4 => "/fmp4/",
            _ => null,
        };

        if (pathPrefix is null ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            uri.Host != "127.0.0.1" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(uri.AbsolutePath.AsSpan(pathPrefix.Length), "N", out var channelId) ||
            channelId == Guid.Empty)
        {
            return false;
        }

        // The preview accepts the exact authority and path emitted by Nova.
        // Do not pass URI aliases or a rewritten path to libmpv.
        return container != PlaybackMediaContainer.FragmentedMp4 ||
            (uri.Port > 0 && url == string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{uri.Port}{uri.AbsolutePath}"));
    }

    private static async ValueTask<ILibMpvPlaybackWindow> OpenWindowAsync(
        DailyPlaybackPlayerSource source,
        LibMpvPlaybackAudioState initialAudio,
        CancellationToken cancellationToken)
    {
        var format = source.Container == PlaybackMediaContainer.FragmentedMp4 ? "fMP4" : "FLV";
        var title = string.IsNullOrWhiteSpace(source.ChannelName)
            ? $"PeerCast Nova - {format} Playback"
            : $"PeerCast Nova - {source.ChannelName.Trim()}";
        var host = new LibMpvPlaybackWindowHost(new()
        {
            Title = title,
            VideoWidth = 960,
            VideoHeight = 540,
            InitialAudioState = initialAudio,
        });
        try
        {
            var windowId = await host.StartAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (windowId == 0)
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

    private sealed class PlaybackWindow : ILibMpvPlaybackWindow
    {
        private readonly LibMpvPlaybackWindowHost _host;

        public PlaybackWindow(LibMpvPlaybackWindowHost host, uint windowId)
        {
            _host = host;
            WindowId = windowId;
            _host.AudioStateChanged += OnAudioStateChanged;
        }

        public uint WindowId { get; }
        public LibMpvPlaybackAudioState AudioState => _host.AudioState;
        public event Action<LibMpvPlaybackAudioState>? AudioStateChanged;
        public Task Closed => _host.Closed;

        public ValueTask DisposeAsync()
        {
            _host.AudioStateChanged -= OnAudioStateChanged;
            return _host.DisposeAsync();
        }

        private void OnAudioStateChanged(LibMpvPlaybackAudioState state) =>
            AudioStateChanged?.Invoke(state);
    }

    private sealed class Lifetime : IDailyPlaybackPlayerLifetime
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _fileLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LibMpvPlaybackAudioSessionStore _audioStore;
        private readonly bool _lowLatency;
        private int _disposed;

        public Lifetime(
            ILibMpvPlaybackWindow window,
            Func<ILibMpvPlaybackClient> openClient,
            DailyPlaybackPlayerSource source,
            LibMpvPlaybackAudioSessionStore audioStore,
            bool lowLatency)
        {
            _audioStore = audioStore;
            _lowLatency = lowLatency;
            Completion = RunAsync(window, openClient, source);
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
                throw new LibMpvPlaybackException("libmpv timed out opening the loopback HTTP stream.");
            }

            if (Completion.IsCompleted)
            {
                await Completion.ConfigureAwait(false);
                if (!_fileLoaded.Task.IsCompletedSuccessfully)
                {
                    throw new LibMpvPlaybackException("Embedded playback ended before the loopback HTTP stream opened.");
                }
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
            ILibMpvPlaybackWindow window, Func<ILibMpvPlaybackClient> openClient, DailyPlaybackPlayerSource source)
        {
            try
            {
                var player = Task.Factory.StartNew(() => RunPlayer(window, openClient, source),
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
            ILibMpvPlaybackWindow window, Func<ILibMpvPlaybackClient> openClient, DailyPlaybackPlayerSource source)
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

            void OnAudioStateChanged(LibMpvPlaybackAudioState state)
            {
                var normalized = state.Normalized();
                _audioStore.Update(normalized);
                client.SetVolume(normalized.Volume);
                client.SetMuted(normalized.Muted);
            }

            window.AudioStateChanged += OnAudioStateChanged;
            try
            {
                var initialAudio = window.AudioState.Normalized();
                _audioStore.Update(initialAudio);
                client.LoadFile(source.StreamUrl, source.Container, window.WindowId, initialAudio, _lowLatency);
                var currentAudio = window.AudioState.Normalized();
                if (currentAudio != initialAudio)
                {
                    OnAudioStateChanged(currentAudio);
                }

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
            finally
            {
                window.AudioStateChanged -= OnAudioStateChanged;
            }
        }
    }
}
