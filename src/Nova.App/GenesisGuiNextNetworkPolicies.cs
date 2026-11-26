using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App;

/// <summary>
/// Application-owned policy foundation initialized from Startup and updated live
/// when connection preferences change. LAN Publishing and activated viewing relay
/// share Relays through DownstreamRelays.
/// </summary>
internal sealed class GenesisGuiNextNetworkPolicies : IDisposable
{
    private GenesisGuiNextNetworkPolicies(GenesisGuiNextConnectionPreferences startup, bool pacing)
    {
        Bandwidth = new(CreateBandwidthLimits(startup));
        Relays = new(CreateAdmissionLimits(startup), Bandwidth,
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

    internal void ApplyLive(GenesisGuiNextConnectionPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Relays.UpdateLimits(CreateAdmissionLimits(preferences), CreateBandwidthLimits(preferences));
    }

    private static UpstreamBandwidthLimits CreateBandwidthLimits(GenesisGuiNextConnectionPreferences preferences) =>
        new(preferences.TotalUpstreamIpv4Kbps, preferences.TotalUpstreamIpv6Kbps);

    private static RelayAdmissionLimits CreateAdmissionLimits(GenesisGuiNextConnectionPreferences preferences) =>
        new(preferences.MaxRelaysTotal, preferences.MaxRelaysPerBroadcastChannel, preferences.MaxRelaysPerViewingChannel);

    public void Dispose()
    {
        Relays.Dispose();
        GC.SuppressFinalize(this);
    }
}
