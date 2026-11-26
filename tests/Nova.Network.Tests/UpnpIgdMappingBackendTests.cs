using System.Globalization;
using System.Xml.Linq;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class UpnpIgdMappingBackendTests
{
    [Fact]
    public async Task FiniteMapRenewUnmap_RequiresMatchingReceiptAndConfirmsDeletion()
    {
        var router = new FakeUpnpGateway();
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        var request = Request();
        var mapped = await backend.ExecuteAsync(request, default);
        Assert.Equal(NatBackendOutcome.Success, mapped.Outcome);
        Assert.Equal(NatLeaseKind.Finite, mapped.LeaseKind);
        Assert.Equal(120u, mapped.LifetimeSeconds);
        Assert.Equal("UPnP IGD", mapped.Backend);
        Assert.Equal(NatBackendOutcome.Success, (await backend.ExecuteAsync(request with { Operation = NatMappingOperation.Renew }, default)).Outcome);
        Assert.Equal(2, router.Adds);
        Assert.Equal(NatBackendOutcome.Success, (await backend.ExecuteAsync(Delete(request), default)).Outcome);
        Assert.Null(router.Entry);
        Assert.Equal(1, router.Deletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingMapping_IsNeverAdoptedEvenForSameClientAndPort(bool sameTarget)
    {
        var router = new FakeUpnpGateway { Entry = FakeUpnpGateway.Mapping("other-app", sameTarget ? "192.168.1.2" : "192.168.1.3") };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        var result = await backend.ExecuteAsync(Request(), default);
        Assert.Equal(NatBackendOutcome.Rejected, result.Outcome);
        Assert.Equal("PortConflict", result.DetailCode);
        await backend.ExecuteAsync(Delete(Request()), default);
        Assert.Equal(0, router.Adds);
        Assert.Equal(0, router.Deletes);
        Assert.NotNull(router.Entry);
    }

    [Fact]
    public async Task ReplacedMapping_IsNeitherRenewedNorDeleted()
    {
        var router = new FakeUpnpGateway();
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        await backend.ExecuteAsync(Request(), default);
        router.Entry = FakeUpnpGateway.Mapping("other-app");
        Assert.Equal(NatBackendOutcome.ResultUnknown, (await backend.ExecuteAsync(Request() with { Operation = NatMappingOperation.Renew }, default)).Outcome);
        Assert.Equal(NatBackendOutcome.ResultUnknown, (await backend.ExecuteAsync(Delete(Request()), default)).Outcome);
        Assert.Equal(1, router.Adds);
        Assert.Equal(0, router.Deletes);
    }

    [Fact]
    public async Task PermanentOnlyRouter_RetriesOnly725AndVerifiesWithoutRenewing()
    {
        var router = new FakeUpnpGateway { PermanentOnly = true };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        var result = await backend.ExecuteAsync(Request(), default);
        Assert.Equal(NatBackendOutcome.Success, result.Outcome);
        Assert.Equal(NatLeaseKind.Permanent, result.LeaseKind);
        Assert.Equal(0u, result.LifetimeSeconds);
        Assert.Equal(new uint[] { 120, 0 }, router.RequestedLifetimes);
        await backend.ExecuteAsync(Request() with { Operation = NatMappingOperation.Renew }, default);
        Assert.Equal(2, router.Adds);
        router.Entry = null;
        await backend.ExecuteAsync(Request() with { Operation = NatMappingOperation.Renew }, default);
        Assert.Equal(0u, router.RequestedLifetimes[^1]);
        await backend.ExecuteAsync(Delete(Request()), default);
        Assert.Null(router.Entry);
    }

    [Theory]
    [InlineData(718, "PortConflict")]
    [InlineData(729, "PortConflict")]
    [InlineData(501, "UpnpError501")]
    public async Task SoapRejection_DoesNotRetryOrClaimSuccess(int error, string code)
    {
        var router = new FakeUpnpGateway { AddError = error };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        var result = await backend.ExecuteAsync(Request(), default);
        Assert.Equal(NatBackendOutcome.Rejected, result.Outcome);
        Assert.Equal(code, result.DetailCode);
        Assert.Equal(1, router.Adds);
    }

    [Fact]
    public async Task LostAddResponse_RetainsReceiptAndAllowsVerifiedCleanup()
    {
        var router = new FakeUpnpGateway { AfterAdd = _ => throw new OperationCanceledException() };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        Assert.Equal(NatBackendOutcome.ResultUnknown, (await backend.ExecuteAsync(Request(), default)).Outcome);
        Assert.NotNull(router.Entry);
        Assert.Equal(NatBackendOutcome.Success, (await backend.ExecuteAsync(Delete(Request()), default)).Outcome);
        Assert.Equal(1, router.Deletes);
    }

    [Fact]
    public async Task UnconfirmedQuery_NeverSendsMutationOrInfersAbsence()
    {
        var router = new FakeUpnpGateway { QueryError = 501 };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        Assert.Equal(NatBackendOutcome.NotSent, (await backend.ExecuteAsync(Request(), default)).Outcome);
        Assert.Equal(0, router.Adds);
        Assert.Equal(0, router.Deletes);
    }

    [Fact]
    public async Task UnconfirmedDelete_DoesNotClaimCleanup()
    {
        var router = new FakeUpnpGateway { KeepAfterDelete = true };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);
        await backend.ExecuteAsync(Request(), default);
        Assert.Equal(NatBackendOutcome.ResultUnknown, (await backend.ExecuteAsync(Delete(Request()), default)).Outcome);
        Assert.NotNull(router.Entry);
    }

    [Fact]
    public async Task MapAndRenew_AttachPublicExternalIpv4AndIgnorePrivateAddresses()
    {
        var router = new FakeUpnpGateway { ExternalIpAddress = "198.41.0.4" };
        var backend = new UpnpIgdMappingBackend(NatPortMappingTestHarness.Home, router);

        var mapped = await backend.ExecuteAsync(Request(), default);
        Assert.Equal(NatBackendOutcome.Success, mapped.Outcome);
        Assert.Equal("198.41.0.4", mapped.ExternalAddress);

        router.ExternalIpAddress = "192.168.100.1";
        var renewed = await backend.ExecuteAsync(Request() with { Operation = NatMappingOperation.Renew }, default);
        Assert.Equal(NatBackendOutcome.Success, renewed.Outcome);
        Assert.Null(renewed.ExternalAddress);

        router.ExternalIpAddress = "invalid-ip";
        var retry = await backend.ExecuteAsync(Request() with { Operation = NatMappingOperation.Renew }, default);
        Assert.Equal(NatBackendOutcome.Success, retry.Outcome);
        Assert.Null(retry.ExternalAddress);
    }

    internal static NatMappingRequest Request() => new(Guid.NewGuid(), 1,
        new(NatPortMappingTestHarness.Home, NatMappingProtocol.Tcp, 7144), NatMappingOperation.Map, 7144, 120, 1);
    internal static NatMappingRequest Delete(NatMappingRequest request) => request with
    { Operation = NatMappingOperation.Unmap, RequestedLifetimeSeconds = 0, SuggestedExternalPort = 0, OwnerCount = 0 };
}

internal sealed class FakeUpnpGateway : IUpnpIgdTransport
{
    public Dictionary<int, XElement> Entries { get; } = [];
    public List<(string Action, int Port)> History { get; } = [];
    public XElement? Entry
    {
        get => Entries.GetValueOrDefault(7144);
        set { if (value is null) Entries.Remove(7144); else Entries[7144] = value; }
    }
    public bool PermanentOnly { get; set; }
    public int? AddError { get; set; }
    public int? QueryError { get; set; }
    public bool KeepAfterDelete { get; set; }
    public Func<CancellationToken, Task>? AfterAdd { get; set; }
    public List<uint> RequestedLifetimes { get; } = [];
    public int Adds { get; private set; }
    public int Deletes { get; private set; }
    public string? ExternalIpAddress { get; set; }

    public Task<string?> TryGetExternalIpAddressAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ExternalIpAddress);

    public async Task<UpnpSoapReply> InvokeAsync(string action, IReadOnlyList<KeyValuePair<string, string>> arguments, CancellationToken cancellationToken)
    {
        var values = arguments.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("TCP", values["NewProtocol"]);
        Assert.Equal("", values["NewRemoteHost"]);
        var port = int.Parse(values["NewExternalPort"], CultureInfo.InvariantCulture);
        History.Add((action, port));
        switch (action)
        {
            case "GetSpecificPortMappingEntry":
                return QueryError is { } error ? new(error, null) : Entries.TryGetValue(port, out var entry) ? new(null, new XElement(entry)) : new(714, null);
            case "AddPortMapping":
                Adds++;
                var lifetime = uint.Parse(values["NewLeaseDuration"], CultureInfo.InvariantCulture);
                RequestedLifetimes.Add(lifetime);
                if (AddError is { } addError) return new(addError, null);
                if (PermanentOnly && lifetime != 0) return new(725, null);
                Assert.Equal(port.ToString(CultureInfo.InvariantCulture), values["NewInternalPort"]);
                Entries[port] = Mapping(values["NewPortMappingDescription"], values["NewInternalClient"], lifetime, port);
                if (AfterAdd is { } after) await after(cancellationToken);
                return new(null, new XElement("AddPortMappingResponse"));
            case "DeletePortMapping":
                Deletes++;
                if (!KeepAfterDelete) Entries.Remove(port);
                return new(null, new XElement("DeletePortMappingResponse"));
            default: throw new InvalidOperationException(action);
        }
    }

    internal static XElement Mapping(string description, string address = "192.168.1.2", uint lifetime = 120, int port = 7144) =>
        new("GetSpecificPortMappingEntryResponse", new XElement("NewInternalClient", address),
            new XElement("NewInternalPort", port), new XElement("NewEnabled", "1"),
            new XElement("NewPortMappingDescription", description), new XElement("NewLeaseDuration", lifetime));
}
