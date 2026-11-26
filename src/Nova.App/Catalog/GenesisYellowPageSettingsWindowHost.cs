namespace PeerCast.Nova.App;

/// <summary>
/// Owns the modeless Yellow Page settings window for one Genesis main-window
/// lifetime. Repeated commands activate the existing window; a user-closed native
/// window is disposed before a new one is created so its temporary class is also
/// released.
/// </summary>
internal sealed class GenesisYellowPageSettingsWindowHost : IAsyncDisposable
{
    private readonly nint _owner;
    private readonly GenesisYellowPageSettingsModel _model;
    private GenesisYellowPageSettingsWindow? _window;
    private int _disposed;

    public GenesisYellowPageSettingsWindowHost(
        nint owner,
        GenesisYellowPageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _owner = owner;
        _model = new GenesisYellowPageSettingsModel(configuration);
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
        _window = GenesisYellowPageSettingsWindow.TryCreate(_owner, _model);
        SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            _owner,
            _window is not null,
            GenesisGuiNextSettingsWindowKind.YellowPages);
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
