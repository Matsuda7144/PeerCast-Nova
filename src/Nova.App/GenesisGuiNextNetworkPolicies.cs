using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App;

/// <summary>
/// Application-owned policy foundation captured from Connection.Startup once.
/// These models are not attached to a live listener or relay controller.
/// Saving next-start preferences cannot mutate this instance.
/// </summary>
internal sealed class GenesisGuiNextNetworkPolicies : IDisposable
{
    private GenesisGuiNextNetworkPolicies(GenesisGuiNextConnectionPreferences startup)
    {
        Relays = new(new(
            startup.MaxRelaysTotal,
            startup.MaxRelaysPerBroadcastChannel,
            startup.MaxRelaysPerViewingChannel));
        Bandwidth = new(new(startup.TotalUpstreamIpv4Kbps, startup.TotalUpstreamIpv6Kbps));
    }

    public RelayAdmissionPolicy Relays { get; }
    public UpstreamBandwidthPolicy Bandwidth { get; }

    public static GenesisGuiNextNetworkPolicies Create(GenesisGuiNextConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.Startup);
    }

    public void Dispose()
    {
        Relays.Dispose();
        GC.SuppressFinalize(this);
    }
}
