using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal interface INovaDailyPlaybackWindow : IAsyncDisposable
{
    event Action? RefreshRequested;

    event Action<int>? PlayRequested;

    event Action? StopRequested;

    ValueTask<NovaDailyPlaybackWindowResult> StartAsync(CancellationToken cancellationToken);

    Task<NovaDailyPlaybackWindowResult> WaitForExitAsync(CancellationToken cancellationToken);

    void SetChannels(IReadOnlyList<string> channelNames);

    void SetStatus(string status);

    void ShowError(string message);
}

internal interface INovaDailyPlaybackWindowFactory
{
    INovaDailyPlaybackWindow Create(string title);
}

internal sealed class SystemNovaDailyPlaybackWindowFactory : INovaDailyPlaybackWindowFactory
{
    public static SystemNovaDailyPlaybackWindowFactory Instance { get; } = new();

    private SystemNovaDailyPlaybackWindowFactory()
    {
    }

    public INovaDailyPlaybackWindow Create(string title) =>
        new SystemNovaDailyPlaybackWindow(new NovaDailyPlaybackWindowHost(title));
}

internal sealed class SystemNovaDailyPlaybackWindow(NovaDailyPlaybackWindowHost host) :
    INovaDailyPlaybackWindow
{
    public event Action? RefreshRequested
    {
        add => host.RefreshRequested += value;
        remove => host.RefreshRequested -= value;
    }

    public event Action<int>? PlayRequested
    {
        add => host.PlayRequested += value;
        remove => host.PlayRequested -= value;
    }

    public event Action? StopRequested
    {
        add => host.StopRequested += value;
        remove => host.StopRequested -= value;
    }

    public ValueTask<NovaDailyPlaybackWindowResult> StartAsync(CancellationToken cancellationToken) =>
        host.StartAsync(cancellationToken);

    public Task<NovaDailyPlaybackWindowResult> WaitForExitAsync(CancellationToken cancellationToken) =>
        host.WaitForExitAsync(cancellationToken).AsTask();

    public void SetChannels(IReadOnlyList<string> channelNames) => host.SetChannels(channelNames);

    public void SetStatus(string status) => host.SetStatus(status);

    public void ShowError(string message) => host.ShowError(message);

    public ValueTask DisposeAsync() => host.DisposeAsync();
}
