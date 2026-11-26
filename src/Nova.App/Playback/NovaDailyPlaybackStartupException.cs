using System.Globalization;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

// Only fixed classifications and numeric protocol codes may reach the GUI.
internal sealed class NovaDailyPlaybackStartupException : IOException
{
    private NovaDailyPlaybackStartupException(string message) : base(message) { }

    internal static NovaDailyPlaybackStartupException HeaderTimeout() =>
        new("FLV header was not received within 30 seconds.");

    internal static NovaDailyPlaybackStartupException FromResult(
        PcpRelayCoordinatorResult result, ChannelFaultSnapshot? fault)
    {
        var message = "PCP startup failed: " + (result.Upstream?.EndReason.ToString() ?? result.EndReason.ToString());
        if (fault is not null) message += $" ({fault.Category}/{fault.Stage})";
        if (result.Upstream?.HttpStatusCode is { } http) message += "; HTTP " + http.ToString(CultureInfo.InvariantCulture);
        if (result.Upstream?.Quit is { } quit) message += "; QUIT " + quit.Code.ToString(CultureInfo.InvariantCulture);
        return new(message + ".");
    }
}
