using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.PortMapping;

// The device key deliberately excludes network epoch, client address and port:
// old and new operations against the same gateway share the same serialization lane.
public sealed record NatDeviceIdentity(string GatewayAddress, string InterfaceId);

public sealed record NatNetworkIdentity(
    NatDeviceIdentity Device,
    string InternalAddress,
    long NetworkEpoch)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Device);
        ArgumentException.ThrowIfNullOrWhiteSpace(Device.InterfaceId);
        ValidateAddress(Device.GatewayAddress);
        ValidateAddress(InternalAddress);
        ArgumentOutOfRangeException.ThrowIfNegative(NetworkEpoch);
    }

    private static void ValidateAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var parsed) ||
            parsed.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(parsed) || parsed.Equals(IPAddress.Any) ||
            parsed.Equals(IPAddress.Broadcast))
        {
            throw new ArgumentException("A concrete, non-loopback IPv4 address is required.", nameof(address));
        }
    }
}

public enum NatMappingProtocol
{
    Tcp = 6,
}

public sealed record NatMappingIdentity(
    NatNetworkIdentity Network,
    NatMappingProtocol Protocol,
    int InternalPort);

public enum NatMappingOperation
{
    Map,
    Renew,
    Unmap,
}

public enum NatBackendOutcome
{
    Success,
    Rejected,
    ResultUnknown,
    NotSent,
}

public sealed record NatMappingRequest(
    Guid OperationId,
    long Generation,
    NatMappingIdentity Identity,
    NatMappingOperation Operation,
    int SuggestedExternalPort,
    uint RequestedLifetimeSeconds,
    int OwnerCount);

public sealed record NatBackendResult(
    NatBackendOutcome Outcome,
    int? ExternalPort = null,
    uint? LifetimeSeconds = null,
    uint? RouterEpochSeconds = null,
    string? DetailCode = null,
    string Backend = "NAT-PMP",
    NatLeaseKind LeaseKind = NatLeaseKind.Finite,
    string? ExternalAddress = null);

public enum NatLeaseKind
{
    Finite,
    Permanent,
    Unknown,
}

/// <summary>
/// A backend's task must encompass all of its sends and retries. Cancellation
/// requests that it stop sending; it never proves that an earlier send had no effect.
/// </summary>
public interface INatMappingBackend
{
    bool MayCreatePermanentLease(NatMappingIdentity identity) => false;

    string GetDisplayName(NatMappingIdentity identity) => "NAT-PMP";

    Task<NatBackendResult> ExecuteAsync(
        NatMappingRequest request,
        CancellationToken cancellationToken);
}

public enum NatMappingStatus
{
    Unmapped,
    Mapping,
    Mapped,
    Unmapping,
    ResultUnknown,
    UnmapUnconfirmed,
    Rejected,
}

public enum NatExternalReachability
{
    Unknown,
    Reachable,
    Unreachable,
}

public sealed record NatLeaseEvidence(
    uint RequestedLifetimeSeconds,
    uint? ConfirmedLifetimeSeconds,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? EstimatedExpiresAt,
    NatLeaseKind Kind = NatLeaseKind.Finite);

public sealed record NatMappingSnapshot(
    NatMappingIdentity Identity,
    int OwnerCount,
    NatMappingStatus Status,
    NatExternalReachability ExternalReachability,
    int? ReturnedExternalPort,
    NatLeaseEvidence? Lease,
    bool OperationPending,
    bool RouterMayContainMapping,
    NatMappingOperation? PendingOperation = null,
    string Backend = "NAT-PMP",
    string? DetailCode = null,
    string? ExternalAddress = null);

/// <summary>A coherent, read-only capture; reading never runs the reconciler.</summary>
public sealed record NatPortMappingReadState(
    bool Enabled,
    long Generation,
    NatNetworkIdentity? Network,
    uint RequestedLifetimeSeconds,
    DateTimeOffset CapturedAt,
    IReadOnlyList<int> RegisteredInternalPorts,
    IReadOnlyList<NatMappingSnapshot> Mappings,
    IReadOnlyList<NatMappingDiagnostic> Diagnostics);

public sealed record NatMappingDiagnostic(
    DateTimeOffset Timestamp,
    string Event,
    long Generation,
    NatMappingRequest? Request,
    NatBackendResult? Result,
    bool StaleGeneration,
    bool CurrentNetwork,
    int CurrentOwnerCount,
    NatNetworkIdentity? CurrentNetworkIdentity);

public enum NatShutdownOutcome
{
    Completed,
    CleanupUnconfirmed,
}

public sealed record NatPortMappingOptions
{
    public bool Enabled { get; init; }

    public uint RequestedLifetimeSeconds { get; init; } = 120;

    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (RequestedLifetimeSeconds is < 1 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestedLifetimeSeconds));
        }

        ValidateTimeout(OperationTimeout, nameof(OperationTimeout));
        ValidateTimeout(ShutdownTimeout, nameof(ShutdownTimeout));
        ValidateTimeout(RetryInterval, nameof(RetryInterval));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
