namespace PeerCast.Nova.Network.PortMapping;

/// <summary>
/// Read-only discovery selects one protocol before any mutation. A submitted
/// mapping pins that choice until confirmed removal, including across epochs.
/// All calls run in the manager's existing device serialization lane.
/// </summary>
public sealed class AutomaticNatMappingBackend : INatMappingBackend
{
    private readonly Func<NatNetworkIdentity, CancellationToken, Task<INatMappingBackend?>> discover;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(NatDeviceIdentity Device, int Port), Selection> selections = new();

    public AutomaticNatMappingBackend() : this(DiscoverAsync) { }

    internal AutomaticNatMappingBackend(
        Func<NatNetworkIdentity, CancellationToken, Task<INatMappingBackend?>> discover) => this.discover = discover;

    public bool MayCreatePermanentLease(NatMappingIdentity identity) =>
        !selections.TryGetValue((identity.Network.Device, identity.InternalPort), out var selection) ||
        selection.Backend.MayCreatePermanentLease(identity);

    public string GetDisplayName(NatMappingIdentity identity) =>
        selections.TryGetValue((identity.Network.Device, identity.InternalPort), out var selection)
            ? selection.Backend.GetDisplayName(identity) : "UPnP / NAT-PMP（検出中）";

    public async Task<NatBackendResult> ExecuteAsync(NatMappingRequest request, CancellationToken cancellationToken)
    {
        var key = (request.Identity.Network.Device, request.Identity.InternalPort);
        if (!selections.TryGetValue(key, out var selected))
        {
            if (request.Operation == NatMappingOperation.Unmap)
                return new(NatBackendOutcome.NotSent, DetailCode: "OwnershipUnconfirmed", Backend: "未選択（UPnP / NAT-PMP）");
            INatMappingBackend? backend;
            try { backend = await discover(request.Identity.Network, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException)
            { return new(NatBackendOutcome.NotSent, DetailCode: "DiscoveryTimeout", Backend: "未選択（UPnP / NAT-PMP）"); }
            if (backend is null) return new(NatBackendOutcome.NotSent, DetailCode: "RouterNotFound", Backend: "未選択（UPnP / NAT-PMP）");
            selected = new(request.Identity.Network, backend);
            selections.TryAdd(key, selected);
        }

        if (selected.Network != request.Identity.Network)
            return new(NatBackendOutcome.ResultUnknown, DetailCode: "PreviousNetworkMappingUnconfirmed",
                Backend: selected.Backend.GetDisplayName(request.Identity), LeaseKind: NatLeaseKind.Unknown);

        var result = await selected.Backend.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Operation == NatMappingOperation.Unmap && result.Outcome == NatBackendOutcome.Success ||
            request.Operation == NatMappingOperation.Map && result.Outcome is NatBackendOutcome.NotSent or NatBackendOutcome.Rejected)
            selections.TryRemove(key, out _);
        return result;
    }

    private static async Task<INatMappingBackend?> DiscoverAsync(NatNetworkIdentity network, CancellationToken cancellationToken)
    {
        var natPmp = await NatPmpMappingBackend.DiscoverExternalAddressAsync(network, cancellationToken).ConfigureAwait(false);
        if (natPmp.Supported)
            return new NatPmpMappingBackend(natPmp.ExternalAddress);
        return await UpnpIgdMappingBackend.DiscoverAsync(network, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<INatMappingBackend?> DiscoverAsync(NatNetworkIdentity network,
        Func<NatNetworkIdentity, CancellationToken, Task<bool>> probeNatPmp,
        Func<NatNetworkIdentity, CancellationToken, Task<INatMappingBackend?>> discoverUpnp,
        CancellationToken cancellationToken)
    {
        // Public-address probing cannot create a NAT-PMP mapping, so its timeout
        // permits UPnP discovery. Mapping timeouts never trigger a protocol switch.
        if (await probeNatPmp(network, cancellationToken).ConfigureAwait(false))
            return new NatPmpMappingBackend();
        return await discoverUpnp(network, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Selection(NatNetworkIdentity Network, INatMappingBackend Backend);
}
