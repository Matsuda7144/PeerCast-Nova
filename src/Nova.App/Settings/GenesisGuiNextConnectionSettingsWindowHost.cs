namespace PeerCast.Nova.App;

internal interface IGenesisGuiNextConnectionSettingsWindow : IAsyncDisposable
{
    bool IsOpen { get; }
    void Activate();
}

/// <summary>Owns only the connection editor; the existing Network host retains NAT/runtime ownership.</summary>
internal sealed class GenesisGuiNextConnectionSettingsWindowHost : IAsyncDisposable
{
    private readonly nint _owner;
    private readonly GenesisGuiNextConnectionSettings _settings;
    private readonly Func<CancellationToken, ValueTask> _showRuntime;
    private readonly Func<IGenesisGuiNextConnectionSettingsWindow?>? _createWindow;
    private readonly Action<GenesisNetworkSettingsWindowFailure>? _feedback;
    private readonly object _disposalGate = new();
    private IAsyncDisposable? _window;
    private Task? _disposal;
    private int _disposed;

    internal GenesisGuiNextConnectionSettingsWindowHost(nint owner, GenesisGuiNextConnectionSettings settings,
        Func<CancellationToken, ValueTask> showRuntime,
        Func<IGenesisGuiNextConnectionSettingsWindow?>? createWindow = null,
        Action<GenesisNetworkSettingsWindowFailure>? feedback = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showRuntime);
        _owner = owner;
        _settings = settings;
        _showRuntime = showRuntime;
        _createWindow = createWindow;
        _feedback = feedback;
    }

    internal async ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var ownerThread = Environment.CurrentManagedThreadId;
        var current = _window;
        if (current is IGenesisGuiNextConnectionSettingsWindow { IsOpen: true } open)
        {
            open.Activate(); // Preserve unsaved input on repeated navigation.
            return;
        }
        if (current is not null)
        {
            try { await current.DisposeAsync().ConfigureAwait(false); }
            catch { ReportFailure(GenesisNetworkSettingsWindowFailure.CleanupFailed); return; }
            _window = null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            ReportFailure(GenesisNetworkSettingsWindowFailure.OwnerThreadUnavailable);
            return;
        }
        try
        {
            _window = _createWindow is not null ? _createWindow() :
                GenesisGuiNextConnectionSettingsWindow.TryCreate(_owner, _settings, () => _ = ShowRuntimeStatusAsync());
        }
        catch (GenesisNetworkSettingsWindowCleanupException exception)
        {
            _window = exception.RetainedResource;
            ReportFailure(GenesisNetworkSettingsWindowFailure.CleanupFailed);
            return;
        }
        catch { ReportFailure(GenesisNetworkSettingsWindowFailure.CreationFailed); return; }
        if (_window is null) { ReportFailure(GenesisNetworkSettingsWindowFailure.CreationFailed); return; }
        if (Volatile.Read(ref _disposed) != 0)
        {
            try { await _window.DisposeAsync().ConfigureAwait(false); }
            catch
            {
                lock (_disposalGate)
                    _disposal = Task.FromException(new GenesisNetworkSettingsWindowCleanupException(_window));
                ReportFailure(GenesisNetworkSettingsWindowFailure.CleanupFailed);
                return;
            }
            _window = null;
        }
    }

    internal async Task ShowRuntimeStatusAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { await _showRuntime(CancellationToken.None).ConfigureAwait(false); }
        catch { ReportFailure(GenesisNetworkSettingsWindowFailure.CreationFailed); }
    }

    private void ReportFailure(GenesisNetworkSettingsWindowFailure failure)
    {
        if (_feedback is null)
            SystemGenesisGuiNextSettingsWindowFeedback.ReportIfFailed(_owner, false, GenesisGuiNextSettingsWindowKind.Connection);
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
        if (_window is not null) await _window.DisposeAsync().ConfigureAwait(false);
        _window = null;
    }
}
