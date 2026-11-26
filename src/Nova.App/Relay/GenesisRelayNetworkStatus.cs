using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App;

internal enum GenesisRelayListenerRuntimeState
{
    Unavailable,
    Stopped,
    Starting,
    Listening,
    Stopping,
    Failed,
}

/// <summary>
/// Application-facing capability flags. The native settings surface uses these
/// flags to hide or disable controls that do not yet have a truthful backend.
/// </summary>
internal sealed record GenesisRelayNetworkCapabilities(
    bool SupportsSharedListener,
    bool SupportsLocalRelayPolicy,
    bool SupportsGlobalRelayPolicy,
    bool SupportsDirectPlay,
    bool SupportsAutomaticPortMapping,
    bool SupportsIpv6)
{
    public static GenesisRelayNetworkCapabilities None { get; } = new(
        SupportsSharedListener: false,
        SupportsLocalRelayPolicy: false,
        SupportsGlobalRelayPolicy: false,
        SupportsDirectPlay: false,
        SupportsAutomaticPortMapping: false,
        SupportsIpv6: false);
}

/// <summary>
/// Immutable applied/runtime projection for GUI Next. Values describe the real
/// application state and are intentionally separate from editable draft settings.
/// PCP implementation types do not cross this boundary.
/// </summary>
internal sealed class GenesisRelayNetworkStatusSnapshot
{
    public GenesisRelayNetworkStatusSnapshot(
        GenesisRelayNetworkCapabilities capabilities,
        GenesisRelayListenerRuntimeState listenerState,
        int activeRelayConnections,
        int? relayConnectionLimit = null,
        AddressFamily? addressFamily = null,
        IPAddress? boundAddress = null,
        int? boundPort = null,
        bool allowsLocalRelay = false,
        bool allowsGlobalRelay = false,
        int? activeDirectListeners = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (activeRelayConnections < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeRelayConnections),
                activeRelayConnections,
                "Active relay connection count cannot be negative.");
        }

        if (relayConnectionLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relayConnectionLimit),
                relayConnectionLimit,
                "Relay connection limit cannot be negative.");
        }

        if (addressFamily.HasValue)
        {
            GenesisNetworkListenerSettings.ValidateAddressFamily(
                addressFamily.Value);
        }

        if (boundAddress is not null)
        {
            if (!addressFamily.HasValue)
            {
                throw new ArgumentException(
                    "A bound address requires an address family.",
                    nameof(boundAddress));
            }

            GenesisNetworkListenerSettings.ValidateAddress(
                boundAddress,
                addressFamily.Value,
                nameof(boundAddress));
        }

        if (boundPort.HasValue)
        {
            GenesisNetworkListenerSettings.ValidatePort(
                boundPort.Value,
                nameof(boundPort));
        }

        if (listenerState == GenesisRelayListenerRuntimeState.Listening &&
            (!addressFamily.HasValue || !boundPort.HasValue))
        {
            throw new ArgumentException(
                "A listening relay endpoint requires an address family and port.",
                nameof(listenerState));
        }

        if (activeDirectListeners < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeDirectListeners),
                activeDirectListeners,
                "Active direct-listener count cannot be negative.");
        }

        if (!capabilities.SupportsDirectPlay && activeDirectListeners.HasValue)
        {
            throw new ArgumentException(
                "Direct-listener status cannot be supplied when Direct Play is unavailable.",
                nameof(activeDirectListeners));
        }

        Capabilities = capabilities;
        ListenerState = listenerState;
        ActiveRelayConnections = activeRelayConnections;
        RelayConnectionLimit = relayConnectionLimit;
        AddressFamily = addressFamily;
        BoundAddress = boundAddress;
        BoundPort = boundPort;
        AllowsLocalRelay = allowsLocalRelay;
        AllowsGlobalRelay = allowsGlobalRelay;
        ActiveDirectListeners = activeDirectListeners;
    }

    public GenesisRelayNetworkCapabilities Capabilities { get; }

    public GenesisRelayListenerRuntimeState ListenerState { get; }

    public int ActiveRelayConnections { get; }

    public int? RelayConnectionLimit { get; }

    public AddressFamily? AddressFamily { get; }

    public IPAddress? BoundAddress { get; }

    public int? BoundPort { get; }

    public bool AllowsLocalRelay { get; }

    public bool AllowsGlobalRelay { get; }

    public int? ActiveDirectListeners { get; }

    public bool IsListening =>
        ListenerState == GenesisRelayListenerRuntimeState.Listening;
}

internal interface IGenesisRelayNetworkStatusSource
{
    GenesisRelayNetworkStatusSnapshot GetSnapshot();
}
