namespace PeerCast.Nova.App;

internal sealed class GenesisInteractionCommandLanes
{
    private Task _playbackCommand = Task.CompletedTask;
    private Task _stopCommand = Task.CompletedTask;
    private Task _refreshCommand = Task.CompletedTask;

    public bool CanStartPlayback => _playbackCommand.IsCompleted;

    public bool CanStartStop => _stopCommand.IsCompleted;

    public bool CanStartRefresh => _refreshCommand.IsCompleted;

    public void TrackPlayback(Task command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _playbackCommand = command;
    }

    public void TrackStop(Task command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _stopCommand = command;
    }

    public void TrackRefresh(Task command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _refreshCommand = command;
    }
}
