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
    private readonly IGuiNextNetworkSettingsAdapter? _networkAdapter;
    private readonly Func<IAsyncDisposable?>? _createWindow;
    private readonly Action<GenesisNetworkSettingsWindowFailure>? _feedback;
    private IAsyncDisposable? _window;
    private readonly object _disposalGate = new();
    private Task? _disposal;
    private int _disposed;

    public GenesisNetworkSettingsWindowHost(
        nint owner,
        IGenesisNetworkSettingsController controller,
        IGuiNextNetworkSettingsAdapter? networkAdapter = null,
        Func<IAsyncDisposable?>? createWindow = null,
        Action<GenesisNetworkSettingsWindowFailure>? feedback = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _owner = owner;
        _networkAdapter = networkAdapter;
        _createWindow = createWindow;
        _feedback = feedback;
        _viewModel = new GenesisNetworkSettingsViewModel(controller);
    }

    public async ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var ownerThread = Environment.CurrentManagedThreadId;

        _viewModel.RefreshStatus();
        var current = _window;
        if (current is not null)
        {
            // This native surface is an immutable snapshot of runtime status.
            // Recreate it after RefreshStatus so repeated navigation never leaves
            // stale listener/relay information visible from an earlier snapshot.
            try
            {
                await current.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                ReportFailure(GenesisNetworkSettingsWindowFailure.CleanupFailed);
                return; // Retain the resource and never recreate after uncertain cleanup.
            }
            _window = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            ReportFailure(GenesisNetworkSettingsWindowFailure.OwnerThreadUnavailable);
            return; // Native creation must not follow a cross-thread async continuation.
        }
        try
        {
            _window = _createWindow is not null ? _createWindow() : _networkAdapter is null
                ? GenesisNetworkSettingsWindow.TryCreate(_owner, _viewModel)
                : GenesisNetworkSettingsWindow.TryCreate(_owner, _networkAdapter);
        }
        catch (GenesisNetworkSettingsWindowCleanupException exception)
        {
            _window = exception.RetainedResource;
            ReportFailure(GenesisNetworkSettingsWindowFailure.CleanupFailed);
            return;
        }
        catch
        {
            ReportFailure(GenesisNetworkSettingsWindowFailure.CreationFailed);
            return;
        }
        if (_window is null)
        {
            ReportFailure(GenesisNetworkSettingsWindowFailure.CreationFailed);
            return;
        }
        if (Volatile.Read(ref _disposed) != 0)
        {
            // A shutdown during creation still owns the late-created resource.
            try
            {
                await _window.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                lock (_disposalGate)
                    _disposal = Task.FromException(new GenesisNetworkSettingsWindowCleanupException(_window));
                ReportFailure(GenesisNetworkSettingsWindowFailure.CleanupFailed);
                return;
            }
            _window = null;
            return;
        }
        SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
            _owner,
            _window is not null,
            GenesisGuiNextSettingsWindowKind.NetworkRelay);
    }

    private void ReportFailure(GenesisNetworkSettingsWindowFailure failure)
    {
        if (_feedback is null)
            SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(
                _owner, false, GenesisGuiNextSettingsWindowKind.NetworkRelay);
        else
        {
            try { _feedback(failure); }
            catch { /* Auxiliary feedback cannot fault the main GUI. */ }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposalGate)
        {
            Interlocked.Exchange(ref _disposed, 1);
            return new(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var current = _window;
        if (current is not null)
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
        _window = null;
    }
}
