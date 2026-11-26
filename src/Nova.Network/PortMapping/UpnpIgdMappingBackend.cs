using System.Globalization;
using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace PeerCast.Nova.Network.PortMapping;

/// <summary>
/// IPv4 TCP IGD control within the existing reconciler. Receipts live only in
/// this process: matching an address/port alone never confers ownership.
/// </summary>
public sealed class UpnpIgdMappingBackend : INatMappingBackend
{
    private readonly NatNetworkIdentity network;
    private readonly IUpnpIgdTransport transport;
    private readonly Dictionary<int, Receipt> receipts = [];

    internal UpnpIgdMappingBackend(NatNetworkIdentity network, IUpnpIgdTransport transport)
    {
        this.network = network;
        this.transport = transport;
    }

    public bool MayCreatePermanentLease(NatMappingIdentity identity) => true;

    public string GetDisplayName(NatMappingIdentity identity) => "UPnP IGD";

    internal static async Task<UpnpIgdMappingBackend?> DiscoverAsync(NatNetworkIdentity network, CancellationToken cancellationToken)
    {
        var service = await UpnpIgdProtocol.DiscoverAsync(network, cancellationToken).ConfigureAwait(false);
        return service is null ? null : new(network, new UpnpIgdTransport(network, service));
    }

    public async Task<NatBackendResult> ExecuteAsync(NatMappingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Identity.Network != network || request.Identity.Protocol != NatMappingProtocol.Tcp ||
            request.Identity.InternalPort is < 1 or > 65535 || !Enum.IsDefined(request.Operation) ||
            (request.Operation != NatMappingOperation.Unmap &&
                (request.SuggestedExternalPort is < 1 or > 65535 || request.RequestedLifetimeSeconds == 0)))
            return Result(NatBackendOutcome.NotSent, "InvalidUpnpRequest");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        var port = request.Identity.InternalPort;
        receipts.TryGetValue(port, out var receipt);
        if (receipt is null)
        {
            if (request.Operation == NatMappingOperation.Unmap) return Result(NatBackendOutcome.ResultUnknown, "OwnershipUnconfirmed");
            receipt = new(request.SuggestedExternalPort, "Nova-" + Guid.NewGuid().ToString("N"));
            receipts.Add(port, receipt);
        }
        try
        {
            var entry = await ReadAsync(receipt, budget.Token).ConfigureAwait(false);
            if (entry is not null && (!receipt.Submitted || !Matches(entry, port, receipt)))
                return Result(receipt.Submitted ? NatBackendOutcome.ResultUnknown : NatBackendOutcome.Rejected,
                    "PortConflict", receipt);
            // Do not stack a second Add behind a request whose router-side
            // execution is still unknown. An owned observation resolves it.
            if (entry is null && receipt.UncertainAdd)
                return Result(NatBackendOutcome.ResultUnknown, "OwnershipUnconfirmed", receipt);
            if (entry is not null) receipt.UncertainAdd = false;

            if (request.Operation == NatMappingOperation.Unmap)
            {
                if (entry is not null)
                {
                    // Re-read above immediately before deletion. UPnP has no
                    // conditional delete; no deletion is sent on uncertain proof.
                    var deleted = await transport.InvokeAsync("DeletePortMapping", Key(receipt), budget.Token).ConfigureAwait(false);
                    if (deleted.ErrorCode is not null and not 714) return Result(NatBackendOutcome.ResultUnknown, "UnmapUnconfirmed", receipt);
                    if (await ReadAsync(receipt, budget.Token).ConfigureAwait(false) is not null)
                        return Result(NatBackendOutcome.ResultUnknown, "UnmapUnconfirmed", receipt);
                }
                receipts.Remove(port);
                return new(NatBackendOutcome.Success, 0, 0, Backend: "UPnP IGD");
            }

            // Permanent leases are periodically verified, without issuing
            // unnecessary Add requests. Missing entries are recreated safely.
            if (entry is null || ReadLifetime(entry) != 0)
            {
                var lifetime = receipt.PermanentRequested ? 0 : request.RequestedLifetimeSeconds;
                receipt.Submitted = true; // Retain proof before the first possible send.
                receipt.UncertainAdd = true;
                var added = await AddAsync(request, receipt, lifetime, budget.Token).ConfigureAwait(false);
                receipt.UncertainAdd = false;
                if (added.ErrorCode == 725 && lifetime != 0)
                {
                    // A precise SOAP rejection permits retry with lease=0.
                    // Recheck ownership before that second, potentially permanent send.
                    entry = await ReadAsync(receipt, budget.Token).ConfigureAwait(false);
                    if (entry is not null && !Matches(entry, port, receipt))
                        return Result(NatBackendOutcome.ResultUnknown, "PortConflict", receipt);
                    receipt.PermanentRequested = true;
                    receipt.UncertainAdd = true;
                    added = await AddAsync(request, receipt, 0, budget.Token).ConfigureAwait(false);
                    receipt.UncertainAdd = false;
                }
                if (added.ErrorCode is { } error)
                    return Result(entry is null && request.Operation == NatMappingOperation.Map
                        ? NatBackendOutcome.Rejected : NatBackendOutcome.ResultUnknown,
                        error is 718 or 729 ? "PortConflict" : "UpnpError" + error.ToString(CultureInfo.InvariantCulture), receipt);
                entry = await ReadAsync(receipt, budget.Token).ConfigureAwait(false);
            }
            if (entry is null || !Matches(entry, port, receipt))
                return Result(NatBackendOutcome.ResultUnknown, "OwnershipUnconfirmed", receipt);
            var confirmedLifetime = ReadLifetime(entry);
            string? externalAddress = null;
            try
            {
                var candidate = await transport.TryGetExternalIpAddressAsync(budget.Token).ConfigureAwait(false);
                if (PeerCast.Nova.Network.Publishing.PublishingIpv4.IsPublicAddress(candidate))
                {
                    externalAddress = candidate;
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException or XmlException)
            {
                // 公開 IP 取得に失敗してもポート開放自体の成功は維持する。
            }
            return new(NatBackendOutcome.Success, receipt.ExternalPort, confirmedLifetime,
                Backend: "UPnP IGD", LeaseKind: confirmedLifetime == 0 ? NatLeaseKind.Permanent : NatLeaseKind.Finite,
                ExternalAddress: externalAddress);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException or XmlException or FormatException or OverflowException)
        {
            return Result(receipt.Submitted ? NatBackendOutcome.ResultUnknown : NatBackendOutcome.NotSent,
                exception is OperationCanceledException ? "ResponseTimeout" : "UpnpResponseInvalid", receipt);
        }
    }

    private Task<UpnpSoapReply> AddAsync(NatMappingRequest request, Receipt receipt, uint lifetime, CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> arguments = [.. Key(receipt),
            new("NewInternalPort", request.Identity.InternalPort.ToString(CultureInfo.InvariantCulture)),
            new("NewInternalClient", network.InternalAddress), new("NewEnabled", "1"),
            new("NewPortMappingDescription", receipt.Description),
            new("NewLeaseDuration", lifetime.ToString(CultureInfo.InvariantCulture))];
        return transport.InvokeAsync("AddPortMapping", arguments, cancellationToken);
    }

    private async Task<XElement?> ReadAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var reply = await transport.InvokeAsync("GetSpecificPortMappingEntry", Key(receipt), cancellationToken).ConfigureAwait(false);
        if (reply.ErrorCode == 714) return null;
        if (reply.ErrorCode is not null || reply.Response is null) throw new XmlException("Mapping query unconfirmed.");
        return reply.Response;
    }

    private bool Matches(XElement entry, int port, Receipt receipt) =>
        IPAddress.TryParse(Value(entry, "NewInternalClient"), out var address) && address.ToString() == network.InternalAddress &&
        Value(entry, "NewInternalPort") == port.ToString(CultureInfo.InvariantCulture) &&
        Value(entry, "NewPortMappingDescription") == receipt.Description &&
        Value(entry, "NewEnabled") is "1" or "true";

    private static uint ReadLifetime(XElement entry) => uint.Parse(Value(entry, "NewLeaseDuration"), NumberStyles.None, CultureInfo.InvariantCulture);
    private static string Value(XElement entry, string name)
    {
        var values = entry.Elements().Where(item => item.Name.LocalName == name &&
            (item.Name.Namespace == XNamespace.None || item.Name.Namespace == entry.Name.Namespace)).Take(2).ToArray();
        return values.Length == 1 ? values[0].Value : throw new XmlException("Incomplete mapping response.");
    }
    private static KeyValuePair<string, string>[] Key(Receipt receipt) =>
        [new("NewRemoteHost", ""), new("NewExternalPort", receipt.ExternalPort.ToString(CultureInfo.InvariantCulture)), new("NewProtocol", "TCP")];

    private static NatBackendResult Result(NatBackendOutcome outcome, string code, Receipt? receipt = null) =>
        new(outcome, DetailCode: code, Backend: "UPnP IGD",
            LeaseKind: receipt?.PermanentRequested == true ? NatLeaseKind.Unknown : NatLeaseKind.Finite);

    private sealed class Receipt(int externalPort, string description)
    {
        public int ExternalPort { get; } = externalPort;
        public string Description { get; } = description;
        public bool Submitted { get; set; }
        public bool PermanentRequested { get; set; }
        public bool UncertainAdd { get; set; }
    }
}
