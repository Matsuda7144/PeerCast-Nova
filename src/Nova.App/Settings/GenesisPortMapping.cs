using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App;

internal enum GenesisPortMappingState
{
    Disabled,
    Discovering,
    Ready,
    RouterNotFound,
    Mapping,
    Mapped,
    Unmapping,
    Failed,
}

internal enum GenesisPortMappingMethod
{
    UpnpIgd,
    NatPmp,
}

internal enum GenesisPortMappingDiscoveryOutcome
{
    Found,
    RouterNotFound,
    Failed,
    Cancelled,
}

internal enum GenesisPortMappingOutcome
{
    Mapped,
    RouterNotFound,
    Failed,
    Cancelled,
}

internal enum GenesisPortUnmappingOutcome
{
    Unmapped,
    Failed,
    Cancelled,
}

/// <summary>
/// One IPv4 TCP listener mapping request. Initial automatic mapping is limited
/// to the NAT-oriented UPnP IGD / NAT-PMP scope; IPv6 reachability remains a
/// separate listener/reachability concern rather than being presented as NAT
/// port mapping.
/// </summary>
internal sealed class GenesisPortMappingRequest
{
    public GenesisPortMappingRequest(
        string listenerId,
        int internalPort,
        int? requestedExternalPort = null,
        IPAddress? localAddress = null)
    {
        ListenerId = NormalizeListenerId(listenerId);
        GenesisNetworkListenerSettings.ValidatePort(
            internalPort,
            nameof(internalPort));

        var normalizedExternalPort = requestedExternalPort ?? internalPort;
        GenesisNetworkListenerSettings.ValidatePort(
            normalizedExternalPort,
            nameof(requestedExternalPort));
        GenesisNetworkListenerSettings.ValidateAddress(
            localAddress,
            AddressFamily.InterNetwork,
            nameof(localAddress));

        InternalPort = internalPort;
        RequestedExternalPort = normalizedExternalPort;
        LocalAddress = localAddress;
    }

    public string ListenerId { get; }

    public int InternalPort { get; }

    public int RequestedExternalPort { get; }

    public IPAddress? LocalAddress { get; }

    private static string NormalizeListenerId(string listenerId)
    {
        ArgumentNullException.ThrowIfNull(listenerId);
        var normalized = listenerId.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Port mapping listener id is required.",
                nameof(listenerId));
        }

        if (normalized.Length > GenesisNetworkListenerSettings.MaximumIdCharacters)
        {
            throw new ArgumentException(
                "Port mapping listener id is too long.",
                nameof(listenerId));
        }

        return normalized;
    }
}

/// <summary>
/// A router-confirmed mapping assignment. This is mapping evidence only: it does
/// not claim that an external peer can actually reach the mapped port.
/// </summary>
internal sealed class GenesisPortMappingAssignment
{
    public GenesisPortMappingAssignment(
        GenesisPortMappingRequest request,
        GenesisPortMappingMethod method,
        int externalPort,
        DateTimeOffset mappedAt,
        IPAddress? externalAddress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMethod(method);
        GenesisNetworkListenerSettings.ValidatePort(
            externalPort,
            nameof(externalPort));
        GenesisNetworkListenerSettings.ValidateAddress(
            externalAddress,
            AddressFamily.InterNetwork,
            nameof(externalAddress));

        Request = request;
        Method = method;
        ExternalPort = externalPort;
        MappedAt = mappedAt;
        ExternalAddress = externalAddress;
    }

    public GenesisPortMappingRequest Request { get; }

    public GenesisPortMappingMethod Method { get; }

    public int ExternalPort { get; }

    public DateTimeOffset MappedAt { get; }

    public IPAddress? ExternalAddress { get; }

    internal static void ValidateMethod(GenesisPortMappingMethod method)
    {
        if (method is not GenesisPortMappingMethod.UpnpIgd and
            not GenesisPortMappingMethod.NatPmp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(method),
                method,
                "Unknown automatic port mapping method.");
        }
    }
}

internal sealed class GenesisPortMappingDiscoveryResult
{
    private GenesisPortMappingDiscoveryResult(
        GenesisPortMappingDiscoveryOutcome outcome,
        GenesisPortMappingMethod? method,
        string? diagnosticCode)
    {
        Outcome = outcome;
        Method = method;
        DiagnosticCode = diagnosticCode;
    }

    public GenesisPortMappingDiscoveryOutcome Outcome { get; }

    public GenesisPortMappingMethod? Method { get; }

    public string? DiagnosticCode { get; }

    public static GenesisPortMappingDiscoveryResult Found(
        GenesisPortMappingMethod method)
    {
        GenesisPortMappingAssignment.ValidateMethod(method);
        return new GenesisPortMappingDiscoveryResult(
            GenesisPortMappingDiscoveryOutcome.Found,
            method,
            diagnosticCode: null);
    }

    public static GenesisPortMappingDiscoveryResult RouterNotFound()
    {
        return new GenesisPortMappingDiscoveryResult(
            GenesisPortMappingDiscoveryOutcome.RouterNotFound,
            method: null,
            diagnosticCode: null);
    }

    public static GenesisPortMappingDiscoveryResult Failed(
        string? diagnosticCode = null)
    {
        return new GenesisPortMappingDiscoveryResult(
            GenesisPortMappingDiscoveryOutcome.Failed,
            method: null,
            GenesisPortMappingDiagnostic.Normalize(diagnosticCode));
    }

    public static GenesisPortMappingDiscoveryResult Cancelled()
    {
        return new GenesisPortMappingDiscoveryResult(
            GenesisPortMappingDiscoveryOutcome.Cancelled,
            method: null,
            diagnosticCode: null);
    }
}

internal sealed class GenesisPortMappingResult
{
    private GenesisPortMappingResult(
        GenesisPortMappingRequest request,
        GenesisPortMappingOutcome outcome,
        GenesisPortMappingAssignment? assignment,
        string? diagnosticCode)
    {
        Request = request;
        Outcome = outcome;
        Assignment = assignment;
        DiagnosticCode = diagnosticCode;
    }

    public GenesisPortMappingRequest Request { get; }

    public GenesisPortMappingOutcome Outcome { get; }

    public GenesisPortMappingAssignment? Assignment { get; }

    public string? DiagnosticCode { get; }

    public static GenesisPortMappingResult Mapped(
        GenesisPortMappingAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new GenesisPortMappingResult(
            assignment.Request,
            GenesisPortMappingOutcome.Mapped,
            assignment,
            diagnosticCode: null);
    }

    public static GenesisPortMappingResult RouterNotFound(
        GenesisPortMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new GenesisPortMappingResult(
            request,
            GenesisPortMappingOutcome.RouterNotFound,
            assignment: null,
            diagnosticCode: null);
    }

    public static GenesisPortMappingResult Failed(
        GenesisPortMappingRequest request,
        string? diagnosticCode = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new GenesisPortMappingResult(
            request,
            GenesisPortMappingOutcome.Failed,
            assignment: null,
            GenesisPortMappingDiagnostic.Normalize(diagnosticCode));
    }

    public static GenesisPortMappingResult Cancelled(
        GenesisPortMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new GenesisPortMappingResult(
            request,
            GenesisPortMappingOutcome.Cancelled,
            assignment: null,
            diagnosticCode: null);
    }
}

internal sealed class GenesisPortUnmappingResult
{
    private GenesisPortUnmappingResult(
        GenesisPortMappingAssignment assignment,
        GenesisPortUnmappingOutcome outcome,
        string? diagnosticCode)
    {
        Assignment = assignment;
        Outcome = outcome;
        DiagnosticCode = diagnosticCode;
    }

    public GenesisPortMappingAssignment Assignment { get; }

    public GenesisPortUnmappingOutcome Outcome { get; }

    public string? DiagnosticCode { get; }

    public static GenesisPortUnmappingResult Unmapped(
        GenesisPortMappingAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new GenesisPortUnmappingResult(
            assignment,
            GenesisPortUnmappingOutcome.Unmapped,
            diagnosticCode: null);
    }

    public static GenesisPortUnmappingResult Failed(
        GenesisPortMappingAssignment assignment,
        string? diagnosticCode = null)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new GenesisPortUnmappingResult(
            assignment,
            GenesisPortUnmappingOutcome.Failed,
            GenesisPortMappingDiagnostic.Normalize(diagnosticCode));
    }

    public static GenesisPortUnmappingResult Cancelled(
        GenesisPortMappingAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new GenesisPortUnmappingResult(
            assignment,
            GenesisPortUnmappingOutcome.Cancelled,
            diagnosticCode: null);
    }
}

/// <summary>
/// Replaceable automatic port-mapping boundary. Implementations own router
/// discovery and protocol details; callers must still run the independent
/// reachability checker before displaying an Internet-reachable/Open state.
/// </summary>
internal interface IGenesisPortMappingService
{
    GenesisPortMappingState CurrentState { get; }

    GenesisPortMappingAssignment? CurrentMapping { get; }

    ValueTask<GenesisPortMappingDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default);

    ValueTask<GenesisPortMappingResult> MapAsync(
        GenesisPortMappingRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GenesisPortUnmappingResult> UnmapAsync(
        GenesisPortMappingAssignment assignment,
        CancellationToken cancellationToken = default);
}

internal static class GenesisPortMappingDiagnostic
{
    public const int MaximumCodeCharacters = 128;

    public static string? Normalize(string? diagnosticCode)
    {
        var normalized = diagnosticCode?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > MaximumCodeCharacters)
        {
            throw new ArgumentException(
                "Port mapping diagnostic code is too long.",
                nameof(diagnosticCode));
        }

        return normalized;
    }
}
