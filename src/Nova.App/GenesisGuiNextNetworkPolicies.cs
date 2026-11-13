using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App;

/// <summary>
/// Application-owned policy foundation captured from Connection.Startup once.
/// LAN Publishing and activated viewing relay share Relays through DownstreamRelays.
/// IPv4 admission shares reservations; optional pacing shares a separate budget.
/// Saving preferences cannot mutate this instance.
/// </summary>
internal sealed class GenesisGuiNextNetworkPolicies : IDisposable
{
    private GenesisGuiNextNetworkPolicies(GenesisGuiNextConnectionPreferences startup, bool pacing)
    {
        Bandwidth = new(new(startup.TotalUpstreamIpv4Kbps, startup.TotalUpstreamIpv6Kbps));
        Relays = new(new(
            startup.MaxRelaysTotal,
            startup.MaxRelaysPerBroadcastChannel,
            startup.MaxRelaysPerViewingChannel), Bandwidth,
            pacing ? new PcpDownstreamPacer(startup.TotalUpstreamIpv4Kbps) : null);
    }

    public RelayAdmissionPolicy Relays { get; }
    public UpstreamBandwidthPolicy Bandwidth { get; }

    public static GenesisGuiNextNetworkPolicies Create(GenesisGuiNextConnectionSettings settings,
        Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.Startup, (readEnvironment ?? Environment.GetEnvironmentVariable)("NOVA_SMART_RELAY_PACING") == "1");
    }

    public void Dispose()
    {
        Relays.Dispose();
        GC.SuppressFinalize(this);
    }
}
