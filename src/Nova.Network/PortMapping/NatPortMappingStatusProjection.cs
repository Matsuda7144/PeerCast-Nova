namespace PeerCast.Nova.Network.PortMapping;

public enum NatDisplayState
{
    Disabled,
    Discovering,
    Idle,
    Mapping,
    Mapped,
    Renewing,
    Unmapping,
    ResultUnknown,
    UnmapUnconfirmed,
    Rejected,
    RouterNotFound,
    PortConflict,
}

public enum NatLeaseEvidenceKind
{
    None,
    RequestedEstimate,
    RouterConfirmedWithEstimatedExpiry,
    RouterConfirmedPermanent,
    Unknown,
}

public enum NatDisplayReachability
{
    NotChecked,
    Reachable,
    Unreachable,
    Unknown,
}

public sealed record NatDisplayDiagnostic(DateTimeOffset Timestamp, string Code, bool Historical = false);

public sealed record NatPortMappingDisplay(
    bool Enabled,
    long Generation,
    string Backend,
    NatNetworkIdentity? Network,
    NatMappingProtocol Protocol,
    IReadOnlyList<int> RegisteredInternalPorts,
    NatDisplayState State,
    NatDisplayReachability ExternalReachability,
    string? ExternalAddress,
    int? ExternalPort,
    uint RequestedLifetimeSeconds,
    NatLeaseEvidence? Lease,
    NatLeaseEvidenceKind LeaseEvidence,
    DateTimeOffset CapturedAt,
    DateTimeOffset? LastObservation,
    IReadOnlyList<NatDisplayDiagnostic> Diagnostics)
{
    // Identity of the selected current-network entry, not an assumed match to every registered port.
    public int? InternalPort { get; init; }
}

/// <summary>
/// Pure projection of one coherent capture. Historical results remain diagnostics;
/// only the exact current network identity can supply current endpoint/lease data.
/// </summary>
public static class NatPortMappingStatusProjection
{
    public static NatPortMappingDisplay Create(NatPortMappingReadState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var current = source.Mappings
            .Where(item => item.Identity.Network == source.Network)
            .OrderByDescending(item => item.OperationPending || item.RouterMayContainMapping)
            .ThenBy(item => item.Identity.InternalPort)
            .FirstOrDefault();
        var state = current?.Status switch
        {
            NatMappingStatus.Mapping when current.Backend == "UPnP / NAT-PMP（検出中）" => NatDisplayState.Discovering,
            NatMappingStatus.Mapping when current.PendingOperation == NatMappingOperation.Renew => NatDisplayState.Renewing,
            NatMappingStatus.Mapping => NatDisplayState.Mapping,
            NatMappingStatus.Mapped => NatDisplayState.Mapped,
            NatMappingStatus.Unmapping => NatDisplayState.Unmapping,
            NatMappingStatus.ResultUnknown => NatDisplayState.ResultUnknown,
            NatMappingStatus.UnmapUnconfirmed => NatDisplayState.UnmapUnconfirmed,
            NatMappingStatus.Rejected when current.DetailCode == "RouterNotFound" => NatDisplayState.RouterNotFound,
            NatMappingStatus.Rejected when current.DetailCode == "PortConflict" => NatDisplayState.PortConflict,
            NatMappingStatus.Rejected => NatDisplayState.Rejected,
            _ => source.Network is null && source.Enabled ? NatDisplayState.Discovering : NatDisplayState.Idle,
        };
        // OFF does not hide outstanding cleanup or a possibly submitted Map.
        if (!source.Enabled && current is not { RouterMayContainMapping: true } &&
            current is not { OperationPending: true })
        {
            state = NatDisplayState.Disabled;
        }

        var diagnostics = new List<NatDisplayDiagnostic>();
        foreach (var item in source.Diagnostics)
        {
            if (item.Event == "NetworkChanged")
            {
                Add(item, "NetworkChanged");
            }
            if (item.Event == "SettingChanged")
            {
                Add(item, "SettingChanged");
            }
            if (item.Request is { } request)
            {
                // Recompute against this capture, rather than the identity that
                // was current when an old diagnostic was originally recorded.
                if (request.Identity.Network != source.Network || request.Generation != source.Generation)
                {
                    Add(item, "StaleObservation");
                }
                if (item.Event == "OperationDispatched")
                {
                    Add(item, request.Operation + "InProgress");
                }
                if (item.Event is "OperationObserved" or "OperationWaitEnded")
                {
                    if (item.Result?.DetailCode is { } detail) Add(item, detail);
                    if (item.Result?.Outcome == NatBackendOutcome.ResultUnknown)
                    {
                        Add(item, request.Operation == NatMappingOperation.Unmap
                            ? "UnmapUnconfirmed" : request.Operation + "ResultUnknown");
                        if (item.Result.DetailCode == nameof(TimeoutException))
                        {
                            Add(item, "ResponseTimeout");
                        }
                    }
                    else if (item.Result?.Outcome == NatBackendOutcome.Rejected)
                    {
                        Add(item, "RouterRejected");
                    }
                    else if (item.Result?.Outcome == NatBackendOutcome.Success)
                    {
                        Add(item, request.Operation == NatMappingOperation.Unmap ? "UnmapObserved" : "MappingObserved");
                        if (request.Operation != NatMappingOperation.Unmap)
                        {
                            if (item.Result.ExternalPort != request.SuggestedExternalPort)
                            {
                                Add(item, "ExternalPortRemapped");
                            }
                            if (item.Result.LeaseKind == NatLeaseKind.Finite && item.Result.LifetimeSeconds < request.RequestedLifetimeSeconds)
                            {
                                Add(item, "LifetimeShortened");
                            }
                        }
                    }
                }
                if (item.Event == "MappingAdopted" && request.Identity.Network == source.Network)
                {
                    Add(item, "MappingEstablished");
                }
            }
            // These codes can be projected from a host observation, but the
            // current local-route source does not claim protocol discovery.
            if (item.Event == "DiscoveryTimeout")
            {
                Add(item, "DiscoveryTimeout");
            }
        }
        var lastChangeAt = source.Diagnostics.Count == 0 ? source.CapturedAt : source.Diagnostics[^1].Timestamp;
        if (!source.Enabled)
        {
            diagnostics.Add(new(lastChangeAt, "Disabled"));
        }
        else if (source.Network is null)
        {
            diagnostics.Add(new(lastChangeAt, "RouterNotFound"));
        }
        var lastObservation = source.Diagnostics.LastOrDefault(item => item.Event == "OperationObserved")?.Timestamp;
        var exposeResult = current is { RouterMayContainMapping: true };
        var lease = exposeResult ? current?.Lease : null;
        var displayReachability = current?.ExternalReachability switch
        {
            NatExternalReachability.Reachable => NatDisplayReachability.Reachable,
            NatExternalReachability.Unreachable => NatDisplayReachability.Unreachable,
            _ => NatDisplayReachability.NotChecked,
        };
        return new NatPortMappingDisplay(source.Enabled, source.Generation, current?.Backend ?? "UPnP / NAT-PMP", source.Network,
            NatMappingProtocol.Tcp, source.RegisteredInternalPorts, state, displayReachability,
            exposeResult ? current?.ExternalAddress : null, exposeResult ? current?.ReturnedExternalPort : null,
            source.RequestedLifetimeSeconds, lease,
            lease is null ? NatLeaseEvidenceKind.None
                : lease.Kind == NatLeaseKind.Permanent ? NatLeaseEvidenceKind.RouterConfirmedPermanent
                : lease.Kind == NatLeaseKind.Unknown ? NatLeaseEvidenceKind.Unknown
                : lease.ConfirmedLifetimeSeconds is null ? NatLeaseEvidenceKind.RequestedEstimate
                : NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry,
            source.CapturedAt, lastObservation, diagnostics.TakeLast(32).ToArray())
        { InternalPort = current?.Identity.InternalPort };

        void Add(NatMappingDiagnostic item, string code) => diagnostics.Add(new(item.Timestamp, code,
            item.Request is { } request &&
            (request.Identity.Network != source.Network || request.Generation != source.Generation)));
    }
}
