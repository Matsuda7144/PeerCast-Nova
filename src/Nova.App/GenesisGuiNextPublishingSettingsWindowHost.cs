namespace PeerCast.Nova.App;

internal interface IGenesisPublishingSettingsWindow : IDisposable
{
    bool IsOpen { get; }
    void Activate();
}

/// <summary>One modeless child for this main-window lifetime; borrows root-owned settings.</summary>
internal sealed class GenesisGuiNextPublishingSettingsWindowHost(
    nint owner, PublishingSettingsController settings, Func<bool> isPublishing,
    Func<nint, PublishingSettingsController, Func<bool>, IGenesisPublishingSettingsWindow?>? create = null) : IDisposable
{
    private IGenesisPublishingSettingsWindow? _window;
    private bool _disposed;

    internal bool Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window?.IsOpen == true) { _window.Activate(); return true; }
        _window?.Dispose();
        _window = (create ?? GenesisGuiNextPublishingSettingsWindow.TryCreate)(owner, settings, isPublishing);
        return _window is not null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window?.Dispose();
        _window = null;
    }
}
