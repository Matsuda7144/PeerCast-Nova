using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App;

/// <summary>
/// Prevents duplicate recording commands for one channel while allowing
/// independent channels and playback commands to proceed separately.
/// </summary>
internal sealed class GenesisManualRecordingCommandLanes
{
    private readonly object _gate = new();
    private readonly HashSet<ChannelId> _activeChannels = [];

    public bool TryEnter(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        lock (_gate)
        {
            return _activeChannels.Add(channelId);
        }
    }

    public void Exit(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        lock (_gate)
        {
            _ = _activeChannels.Remove(channelId);
        }
    }

    public bool IsActive(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        lock (_gate)
        {
            return _activeChannels.Contains(channelId);
        }
    }
}
