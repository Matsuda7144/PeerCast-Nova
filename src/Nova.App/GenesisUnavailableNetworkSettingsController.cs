namespace PeerCast.Nova.App;

/// <summary>
/// Truthful production placeholder used until Nova owns an application-wide shared
/// listener. The settings surface can therefore expose its location without claiming
/// that a per-channel relay endpoint is the application's listening port.
/// </summary>
internal sealed class GenesisUnavailableNetworkSettingsController :
    IGenesisNetworkSettingsController
{
    private static readonly GenesisNetworkSettingsAppliedSnapshot UnavailableSnapshot = new(
        new GenesisNetworkBasicSettings(
            port: 7144,
            ipv6Enabled: false,
            automaticPortMappingEnabled: false,
            allowLocalRelay: false,
            allowGlobalRelay: false),
        new GenesisRelayNetworkStatusSnapshot(
            GenesisRelayNetworkCapabilities.None,
            GenesisRelayListenerRuntimeState.Unavailable,
            activeRelayConnections: 0));

    public static GenesisUnavailableNetworkSettingsController Instance { get; } = new();

    private GenesisUnavailableNetworkSettingsController()
    {
    }

    public GenesisNetworkSettingsAppliedSnapshot Snapshot => UnavailableSnapshot;

    public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
        GenesisNetworkBasicSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
    }
}
