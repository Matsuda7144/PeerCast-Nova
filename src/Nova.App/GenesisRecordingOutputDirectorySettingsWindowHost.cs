namespace PeerCast.Nova.App;

/// <summary>
/// Owns the modeless recording destination settings window for one Genesis main-window
/// lifetime. The application settings object remains the owner of persistence.
/// </summary>
internal sealed class GenesisRecordingOutputDirectorySettingsWindowHost : IAsyncDisposable
{
    private readonly nint _owner;
    private readonly GenesisRecordingOutputDirectorySettings _settings;
    private GenesisGuiNextPlaybackSettingsWindow? _window;
    private int _disposed;

    public GenesisRecordingOutputDirectorySettingsWindowHost(
        nint owner,
        GenesisRecordingOutputDirectorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _owner = owner;
        _settings = settings;
    }

    public async ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var current = _window;
        if (current is not null)
        {
            if (current.IsOpen)
            {
                current.Activate();
                return;
            }

            await current.DisposeAsync().ConfigureAwait(false);
            _window = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _window = GenesisGuiNextPlaybackSettingsWindow.TryCreate(_owner, _settings);
        SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            _owner,
            _window is not null,
            GenesisGuiNextSettingsWindowKind.Recording);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var current = _window;
        _window = null;
        if (current is not null)
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
    }
}
