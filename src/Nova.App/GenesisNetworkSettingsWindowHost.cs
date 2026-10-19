namespace PeerCast.Nova.App;

/// <summary>
/// Owns one modeless Network / Relay settings window for a Genesis main-window
/// lifetime. The view model remains application-owned and can later receive a real
/// shared-listener controller without changing the native navigation contract.
/// </summary>
internal sealed class GenesisNetworkSettingsWindowHost : IAsyncDisposable
{
    private readonly nint _owner;
    private readonly GenesisNetworkSettingsViewModel _viewModel;
    private GenesisNetworkSettingsWindow? _window;
    private int _disposed;

    public GenesisNetworkSettingsWindowHost(
        nint owner,
        IGenesisNetworkSettingsController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _owner = owner;
        _viewModel = new GenesisNetworkSettingsViewModel(controller);
    }

    public async ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        _viewModel.RefreshStatus();
        var current = _window;
        if (current is not null)
        {
            // This native surface is an immutable snapshot of runtime status.
            // Recreate it after RefreshStatus so repeated navigation never leaves
            // stale listener/relay information visible from an earlier snapshot.
            await current.DisposeAsync().ConfigureAwait(false);
            _window = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _window = GenesisNetworkSettingsWindow.TryCreate(_owner, _viewModel);
        SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            _owner,
            _window is not null,
            GenesisGuiNextSettingsWindowKind.NetworkRelay);
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
