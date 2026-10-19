using System.Net.Sockets;

namespace PeerCast.Nova.App;

/// <summary>
/// Immutable text/status projection consumed by the native Network / Relay surface.
/// The projection only formats application-owned evidence and never upgrades a local
/// bind, port mapping, or unsupported capability into a stronger user-visible state.
/// </summary>
internal sealed record GenesisNetworkSettingsPresentationSnapshot(
    string ListenerStateText,
    string BoundEndpointText,
    string RelayConnectionsText,
    string DirectListenersText,
    string Ipv4ReachabilityText,
    string Ipv6ReachabilityText,
    string ExternalIpv4Text,
    string ExternalIpv6Text);

internal static class GenesisNetworkSettingsPresentation
{
    private const string UnknownText = "Unknown";
    private const string RefreshFailedText = "Unknown (refresh failed)";
    private const string UnavailableText = "Unavailable";
    private const string EmptyText = "—";

    public static GenesisNetworkSettingsPresentationSnapshot Create(
        GenesisNetworkSettingsViewModel viewModel,
        GenesisNetworkReachabilityState ipv4State = GenesisNetworkReachabilityState.Unknown,
        GenesisNetworkReachabilityResult? ipv4Result = null,
        GenesisNetworkReachabilityState ipv6State = GenesisNetworkReachabilityState.Unknown,
        GenesisNetworkReachabilityResult? ipv6Result = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ValidateReachabilityResult(ipv4Result, AddressFamily.InterNetwork, nameof(ipv4Result));
        ValidateReachabilityResult(ipv6Result, AddressFamily.InterNetworkV6, nameof(ipv6Result));

        if (!viewModel.IsRuntimeStatusCurrent)
        {
            return new GenesisNetworkSettingsPresentationSnapshot(
                ListenerStateText: RefreshFailedText,
                BoundEndpointText: EmptyText,
                RelayConnectionsText: UnknownText,
                DirectListenersText: UnknownText,
                Ipv4ReachabilityText: UnknownText,
                Ipv6ReachabilityText: UnknownText,
                ExternalIpv4Text: EmptyText,
                ExternalIpv6Text: EmptyText);
        }

        var runtime = viewModel.RuntimeStatus;
        var supportsSharedListener = runtime.Capabilities.SupportsSharedListener;
        return new GenesisNetworkSettingsPresentationSnapshot(
            ListenerStateText: supportsSharedListener
                ? FormatListenerState(runtime.ListenerState)
                : UnavailableText,
            BoundEndpointText: supportsSharedListener
                ? FormatBoundEndpoint(runtime)
                : EmptyText,
            RelayConnectionsText: FormatRelayConnections(runtime),
            DirectListenersText: FormatDirectListeners(runtime),
            Ipv4ReachabilityText: FormatReachability(ipv4State, ipv4Result),
            Ipv6ReachabilityText: runtime.Capabilities.SupportsIpv6
                ? FormatReachability(ipv6State, ipv6Result)
                : UnavailableText,
            ExternalIpv4Text: FormatExternalAddress(ipv4Result),
            ExternalIpv6Text: runtime.Capabilities.SupportsIpv6
                ? FormatExternalAddress(ipv6Result)
                : UnavailableText);
    }

    internal static string FormatListenerState(GenesisRelayListenerRuntimeState state) =>
        state switch
        {
            GenesisRelayListenerRuntimeState.Unavailable => UnavailableText,
            GenesisRelayListenerRuntimeState.Stopped => "Stopped",
            GenesisRelayListenerRuntimeState.Starting => "Starting",
            GenesisRelayListenerRuntimeState.Listening => "Listening",
            GenesisRelayListenerRuntimeState.Stopping => "Stopping",
            GenesisRelayListenerRuntimeState.Failed => "Failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown relay listener runtime state."),
        };

    private static string FormatBoundEndpoint(GenesisRelayNetworkStatusSnapshot runtime)
    {
        if (!runtime.IsListening ||
            runtime.BoundAddress is null ||
            !runtime.BoundPort.HasValue)
        {
            return EmptyText;
        }

        return runtime.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{runtime.BoundAddress}]:{runtime.BoundPort.Value}"
            : $"{runtime.BoundAddress}:{runtime.BoundPort.Value}";
    }

    private static string FormatRelayConnections(GenesisRelayNetworkStatusSnapshot runtime) =>
        runtime.RelayConnectionLimit.HasValue
            ? $"{runtime.ActiveRelayConnections} / {runtime.RelayConnectionLimit.Value}"
            : runtime.ActiveRelayConnections.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatDirectListeners(GenesisRelayNetworkStatusSnapshot runtime)
    {
        if (!runtime.Capabilities.SupportsDirectPlay)
        {
            return UnavailableText;
        }

        return runtime.ActiveDirectListeners.HasValue
            ? runtime.ActiveDirectListeners.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : UnknownText;
    }

    private static string FormatReachability(
        GenesisNetworkReachabilityState state,
        GenesisNetworkReachabilityResult? result)
    {
        return state switch
        {
            GenesisNetworkReachabilityState.Unknown => UnknownText,
            GenesisNetworkReachabilityState.Checking => "Checking…",
            GenesisNetworkReachabilityState.Open => FormatOpenReachability(result),
            GenesisNetworkReachabilityState.Closed => "Closed",
            GenesisNetworkReachabilityState.Failed => "Failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown network reachability state."),
        };
    }

    private static string FormatOpenReachability(GenesisNetworkReachabilityResult? result)
    {
        if (result is null ||
            result.Outcome != GenesisNetworkReachabilityOutcome.Open ||
            result.ReachablePorts.Count == 0)
        {
            return "Open";
        }

        return $"Open ({string.Join(", ", result.ReachablePorts)})";
    }

    private static string FormatExternalAddress(GenesisNetworkReachabilityResult? result) =>
        result?.ExternalAddress?.ToString() ?? EmptyText;

    private static void ValidateReachabilityResult(
        GenesisNetworkReachabilityResult? result,
        AddressFamily expectedFamily,
        string parameterName)
    {
        if (result is not null && result.Request.AddressFamily != expectedFamily)
        {
            throw new ArgumentException(
                "Reachability evidence does not match the projected address family.",
                parameterName);
        }
    }
}
