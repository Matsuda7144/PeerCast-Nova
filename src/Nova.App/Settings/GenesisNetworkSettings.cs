using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App;

internal enum GenesisNetworkReachabilityState
{
    Unknown,
    Checking,
    Open,
    Closed,
    Failed,
}

internal enum GenesisNetworkReachabilityOutcome
{
    Open,
    Closed,
    Failed,
    Cancelled,
}

/// <summary>
/// Immutable application-facing listener settings used by GUI Next. The native
/// settings surface edits a draft copy and production networking applies validated
/// settings behind a separate controller boundary.
/// </summary>
internal sealed class GenesisNetworkListenerSettings
{
    internal const int MaximumIdCharacters = 64;

    public GenesisNetworkListenerSettings(
        string id,
        AddressFamily addressFamily,
        int port,
        IPAddress? bindAddress = null,
        bool isEnabled = true,
        bool allowLocalRelay = true,
        bool allowGlobalRelay = true,
        bool allowLocalDirectPlay = true,
        bool allowGlobalDirectPlay = false)
    {
        Id = NormalizeId(id);
        ValidateAddressFamily(addressFamily);
        ValidatePort(port, nameof(port));
        ValidateAddress(bindAddress, addressFamily, nameof(bindAddress));

        AddressFamily = addressFamily;
        Port = port;
        BindAddress = bindAddress;
        IsEnabled = isEnabled;
        AllowLocalRelay = allowLocalRelay;
        AllowGlobalRelay = allowGlobalRelay;
        AllowLocalDirectPlay = allowLocalDirectPlay;
        AllowGlobalDirectPlay = allowGlobalDirectPlay;
    }

    public string Id { get; }

    public AddressFamily AddressFamily { get; }

    public int Port { get; }

    public IPAddress? BindAddress { get; }

    public bool IsEnabled { get; }

    public bool AllowLocalRelay { get; }

    public bool AllowGlobalRelay { get; }

    public bool AllowLocalDirectPlay { get; }

    public bool AllowGlobalDirectPlay { get; }

    private static string NormalizeId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var normalized = id.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Network listener id is required.", nameof(id));
        }

        if (normalized.Length > MaximumIdCharacters)
        {
            throw new ArgumentException("Network listener id is too long.", nameof(id));
        }

        return normalized;
    }

    internal static void ValidateAddressFamily(AddressFamily addressFamily)
    {
        if (addressFamily != AddressFamily.InterNetwork &&
            addressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(addressFamily),
                addressFamily,
                "Only IPv4 and IPv6 listener families are supported.");
        }
    }

    internal static void ValidatePort(int port, string parameterName)
    {
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                port,
                "Network port must be between 0 and 65535.");
        }

        if (port == 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                port,
                "Network port zero is not a persistent listener setting.");
        }
    }

    internal static void ValidateAddress(
        IPAddress? address,
        AddressFamily addressFamily,
        string parameterName)
    {
        if (address is not null && address.AddressFamily != addressFamily)
        {
            throw new ArgumentException(
                "Network address family must match the listener family.",
                parameterName);
        }
    }
}

/// <summary>
/// Finite external reachability request. Port ordering is normalized so fake and
/// production checkers receive deterministic input.
/// </summary>
internal sealed class GenesisNetworkReachabilityRequest
{
    public GenesisNetworkReachabilityRequest(
        AddressFamily addressFamily,
        IEnumerable<int> ports,
        IPAddress? localAddress = null)
    {
        GenesisNetworkListenerSettings.ValidateAddressFamily(addressFamily);
        ArgumentNullException.ThrowIfNull(ports);
        GenesisNetworkListenerSettings.ValidateAddress(
            localAddress,
            addressFamily,
            nameof(localAddress));

        var normalizedPorts = ports
            .Distinct()
            .OrderBy(static port => port)
            .ToArray();
        if (normalizedPorts.Length == 0)
        {
            throw new ArgumentException(
                "At least one candidate listening port is required.",
                nameof(ports));
        }

        foreach (var port in normalizedPorts)
        {
            GenesisNetworkListenerSettings.ValidatePort(port, nameof(ports));
        }

        AddressFamily = addressFamily;
        LocalAddress = localAddress;
        Ports = Array.AsReadOnly(normalizedPorts);
    }

    public AddressFamily AddressFamily { get; }

    public IPAddress? LocalAddress { get; }

    public IReadOnlyList<int> Ports { get; }

    internal bool ContainsPort(int port)
    {
        for (var index = 0; index < Ports.Count; index++)
        {
            if (Ports[index] == port)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Completed external reachability evidence. Open means an external checker
/// confirmed at least one requested port; local bind or mapping success alone
/// cannot construct an Open result.
/// </summary>
internal sealed class GenesisNetworkReachabilityResult
{
    internal const int MaximumDiagnosticCodeCharacters = 128;

    private GenesisNetworkReachabilityResult(
        GenesisNetworkReachabilityRequest request,
        GenesisNetworkReachabilityOutcome outcome,
        IPAddress? externalAddress,
        IReadOnlyList<int> reachablePorts,
        TimeSpan elapsed,
        DateTimeOffset checkedAt,
        string? diagnosticCode)
    {
        Request = request;
        Outcome = outcome;
        ExternalAddress = externalAddress;
        ReachablePorts = reachablePorts;
        Elapsed = elapsed;
        CheckedAt = checkedAt;
        DiagnosticCode = diagnosticCode;
    }

    public GenesisNetworkReachabilityRequest Request { get; }

    public GenesisNetworkReachabilityOutcome Outcome { get; }

    public IPAddress? ExternalAddress { get; }

    public IReadOnlyList<int> ReachablePorts { get; }

    public TimeSpan Elapsed { get; }

    public DateTimeOffset CheckedAt { get; }

    public string? DiagnosticCode { get; }

    public bool IsOpen => Outcome == GenesisNetworkReachabilityOutcome.Open;

    public static GenesisNetworkReachabilityResult Open(
        GenesisNetworkReachabilityRequest request,
        IEnumerable<int> reachablePorts,
        TimeSpan elapsed,
        DateTimeOffset checkedAt,
        IPAddress? externalAddress = null)
    {
        var normalizedPorts = NormalizeReachablePorts(request, reachablePorts);
        if (normalizedPorts.Count == 0)
        {
            throw new ArgumentException(
                "An Open reachability result requires at least one confirmed port.",
                nameof(reachablePorts));
        }

        ValidateCommon(request, externalAddress, elapsed);
        return new GenesisNetworkReachabilityResult(
            request,
            GenesisNetworkReachabilityOutcome.Open,
            externalAddress,
            normalizedPorts,
            elapsed,
            checkedAt,
            diagnosticCode: null);
    }

    public static GenesisNetworkReachabilityResult Closed(
        GenesisNetworkReachabilityRequest request,
        TimeSpan elapsed,
        DateTimeOffset checkedAt,
        IPAddress? externalAddress = null)
    {
        ValidateCommon(request, externalAddress, elapsed);
        return new GenesisNetworkReachabilityResult(
            request,
            GenesisNetworkReachabilityOutcome.Closed,
            externalAddress,
            Array.Empty<int>(),
            elapsed,
            checkedAt,
            diagnosticCode: null);
    }

    public static GenesisNetworkReachabilityResult Failed(
        GenesisNetworkReachabilityRequest request,
        TimeSpan elapsed,
        DateTimeOffset checkedAt,
        string? diagnosticCode = null)
    {
        ValidateCommon(request, externalAddress: null, elapsed);
        return new GenesisNetworkReachabilityResult(
            request,
            GenesisNetworkReachabilityOutcome.Failed,
            externalAddress: null,
            Array.Empty<int>(),
            elapsed,
            checkedAt,
            NormalizeDiagnosticCode(diagnosticCode));
    }

    public static GenesisNetworkReachabilityResult Cancelled(
        GenesisNetworkReachabilityRequest request,
        TimeSpan elapsed,
        DateTimeOffset checkedAt)
    {
        ValidateCommon(request, externalAddress: null, elapsed);
        return new GenesisNetworkReachabilityResult(
            request,
            GenesisNetworkReachabilityOutcome.Cancelled,
            externalAddress: null,
            Array.Empty<int>(),
            elapsed,
            checkedAt,
            diagnosticCode: null);
    }

    private static ReadOnlyCollection<int> NormalizeReachablePorts(
        GenesisNetworkReachabilityRequest request,
        IEnumerable<int> reachablePorts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reachablePorts);

        var normalized = reachablePorts
            .Distinct()
            .OrderBy(static port => port)
            .ToArray();
        foreach (var port in normalized)
        {
            GenesisNetworkListenerSettings.ValidatePort(port, nameof(reachablePorts));
            if (!request.ContainsPort(port))
            {
                throw new ArgumentException(
                    "A reachable port must have been present in the request.",
                    nameof(reachablePorts));
            }
        }

        return Array.AsReadOnly(normalized);
    }

    private static void ValidateCommon(
        GenesisNetworkReachabilityRequest request,
        IPAddress? externalAddress,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        GenesisNetworkListenerSettings.ValidateAddress(
            externalAddress,
            request.AddressFamily,
            nameof(externalAddress));
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Reachability elapsed time cannot be negative.");
        }
    }

    private static string? NormalizeDiagnosticCode(string? diagnosticCode)
    {
        var normalized = diagnosticCode?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > MaximumDiagnosticCodeCharacters)
        {
            throw new ArgumentException(
                "Reachability diagnostic code is too long.",
                nameof(diagnosticCode));
        }

        return normalized;
    }
}

internal interface IGenesisNetworkReachabilityChecker
{
    ValueTask<GenesisNetworkReachabilityResult> CheckAsync(
        GenesisNetworkReachabilityRequest request,
        CancellationToken cancellationToken = default);
}
