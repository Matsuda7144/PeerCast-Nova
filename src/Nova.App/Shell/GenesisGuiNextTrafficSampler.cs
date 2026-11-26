using System.Globalization;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextTrafficValues(GenesisGuiNextTopStatusValue Connections, GenesisGuiNextTopStatusValue Up, GenesisGuiNextTopStatusValue Down)
{
    internal static GenesisGuiNextTrafficValues Unknown { get; } = new(GenesisGuiNextTopStatusPresentation.Unknown,
        GenesisGuiNextTopStatusPresentation.Unknown, GenesisGuiNextTopStatusPresentation.Unknown);
}

// Per-window sampling only. There is no timer, network activity, or retained connection here.
internal sealed class GenesisGuiNextTrafficSampler(TimeProvider clock)
{
    private PcpTransportSnapshot? baseline;
    private GenesisGuiNextTrafficValues last = GenesisGuiNextTrafficValues.Unknown;
    internal GenesisGuiNextTrafficValues Sample(PcpTransportSnapshot? current)
    {
        if (current is null || !current.IsValid || current.Connections < 0 || current.SentBytes < 0 || current.ReceivedBytes < 0 ||
            current.TimestampFrequency <= 0 || current.TimestampFrequency != clock.TimestampFrequency)
            return Reset();
        var age = ((decimal)clock.GetTimestamp() - current.Timestamp) / current.TimestampFrequency;
        if (age < 0 || age > 2) return Reset(); // Cached/failed observations must not stay current indefinitely.
        var count = Text(current.Connections.ToString(CultureInfo.InvariantCulture));
        var previous = baseline;
        if (previous is null || current.Scope != previous.Scope || current.Generation != previous.Generation ||
            current.SentBytes < previous.SentBytes || current.ReceivedBytes < previous.ReceivedBytes)
        {
            baseline = current;
            return last = new(count, GenesisGuiNextTopStatusPresentation.Unknown, GenesisGuiNextTopStatusPresentation.Unknown);
        }
        if (current.Timestamp == previous.Timestamp) return last; // Never recalculate the same observation.
        var seconds = (double)(((decimal)current.Timestamp - previous.Timestamp) / current.TimestampFrequency);
        if (seconds <= 0) return Reset();
        if (seconds < 0.5) return last; // Keep one baseline until there is a useful observation window.
        var up = (current.SentBytes - previous.SentBytes) * 8d / seconds;
        var down = (current.ReceivedBytes - previous.ReceivedBytes) * 8d / seconds;
        baseline = current;
        if (!double.IsFinite(up) || !double.IsFinite(down)) return Reset();
        return last = new(count, Rate(up), Rate(down));
    }
    private GenesisGuiNextTrafficValues Reset()
    { baseline = null; return last = GenesisGuiNextTrafficValues.Unknown; }
    private static GenesisGuiNextTopStatusValue Text(string text, string? compact = null) =>
        new(text, compact ?? text, GenesisGuiNextDesign.Text);
    private static GenesisGuiNextTopStatusValue Rate(double bits)
    {
        if (bits == 0) return Text("0 kbps", "0k");
        if (bits < 100) return Text("<0.1 kbps", "<0.1k");
        var units = new[] { (1000d, "kbps", "k"), (1e6, "Mbps", "M"), (1e9, "Gbps", "G"),
            (1e12, "Tbps", "T"), (1e15, "Pbps", "P"), (1e18, "Ebps", "E") };
        var unit = units.Last(u => bits >= u.Item1 || u.Item1 == 1000);
        var number = (bits / unit.Item1).ToString("0.#", CultureInfo.InvariantCulture);
        var compact = (bits / unit.Item1).ToString(bits / unit.Item1 >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return Text(number + " " + unit.Item2, compact + unit.Item3);
    }
}
