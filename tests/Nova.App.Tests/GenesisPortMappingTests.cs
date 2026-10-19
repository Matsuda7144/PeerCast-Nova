using System.Net;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPortMappingTests
{
    [Fact]
    public void RequestNormalizesListenerAndDefaultsExternalPort()
    {
        var request = new GenesisPortMappingRequest(
            "  primary-v4  ",
            internalPort: 7144,
            localAddress: IPAddress.Loopback);

        Assert.Equal("primary-v4", request.ListenerId);
        Assert.Equal(7144, request.InternalPort);
        Assert.Equal(7144, request.RequestedExternalPort);
        Assert.Equal(IPAddress.Loopback, request.LocalAddress);
    }

    [Fact]
    public void RequestRejectsNonIpv4AddressAndPersistentZeroPort()
    {
        Assert.Throws<ArgumentException>(
            () => new GenesisPortMappingRequest(
                "primary-v4",
                internalPort: 7144,
                localAddress: IPAddress.IPv6Loopback));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GenesisPortMappingRequest(
                "primary-v4",
                internalPort: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GenesisPortMappingRequest(
                "primary-v4",
                internalPort: 7144,
                requestedExternalPort: 0));
    }

    [Fact]
    public void AssignmentPreservesRouterMappingEvidenceOnly()
    {
        var request = new GenesisPortMappingRequest(
            "primary-v4",
            internalPort: 7144,
            requestedExternalPort: 17144);
        var mappedAt = new DateTimeOffset(
            2026,
            8,
            20,
            8,
            0,
            0,
            TimeSpan.Zero);
        var assignment = new GenesisPortMappingAssignment(
            request,
            GenesisPortMappingMethod.NatPmp,
            externalPort: 27144,
            mappedAt,
            IPAddress.Parse("203.0.113.10"));
        var result = GenesisPortMappingResult.Mapped(assignment);

        Assert.Same(request, assignment.Request);
        Assert.Equal(7144, assignment.Request.InternalPort);
        Assert.Equal(17144, assignment.Request.RequestedExternalPort);
        Assert.Equal(27144, assignment.ExternalPort);
        Assert.Equal(GenesisPortMappingMethod.NatPmp, assignment.Method);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), assignment.ExternalAddress);
        Assert.Equal(mappedAt, assignment.MappedAt);
        Assert.Equal(GenesisPortMappingOutcome.Mapped, result.Outcome);
        Assert.Same(assignment, result.Assignment);
        Assert.Same(request, result.Request);
    }

    [Fact]
    public void DiscoveryDistinguishesRouterAbsenceFailureAndCancellation()
    {
        var found = GenesisPortMappingDiscoveryResult.Found(
            GenesisPortMappingMethod.UpnpIgd);
        var notFound = GenesisPortMappingDiscoveryResult.RouterNotFound();
        var failed = GenesisPortMappingDiscoveryResult.Failed("  timeout  ");
        var cancelled = GenesisPortMappingDiscoveryResult.Cancelled();

        Assert.Equal(GenesisPortMappingDiscoveryOutcome.Found, found.Outcome);
        Assert.Equal(GenesisPortMappingMethod.UpnpIgd, found.Method);
        Assert.Equal(
            GenesisPortMappingDiscoveryOutcome.RouterNotFound,
            notFound.Outcome);
        Assert.Null(notFound.Method);
        Assert.Equal(GenesisPortMappingDiscoveryOutcome.Failed, failed.Outcome);
        Assert.Equal("timeout", failed.DiagnosticCode);
        Assert.Equal(
            GenesisPortMappingDiscoveryOutcome.Cancelled,
            cancelled.Outcome);
        Assert.Null(cancelled.DiagnosticCode);
    }

    [Fact]
    public void MapAndUnmapResultsKeepOperationSemanticsSeparate()
    {
        var request = new GenesisPortMappingRequest(
            "primary-v4",
            internalPort: 7144);
        var assignment = new GenesisPortMappingAssignment(
            request,
            GenesisPortMappingMethod.UpnpIgd,
            externalPort: 7144,
            DateTimeOffset.UnixEpoch);

        var routerMissing = GenesisPortMappingResult.RouterNotFound(request);
        var mapFailed = GenesisPortMappingResult.Failed(
            request,
            "  mapping-failed  ");
        var mapCancelled = GenesisPortMappingResult.Cancelled(request);
        var unmapped = GenesisPortUnmappingResult.Unmapped(assignment);
        var unmapFailed = GenesisPortUnmappingResult.Failed(
            assignment,
            "  unmap-failed  ");
        var unmapCancelled = GenesisPortUnmappingResult.Cancelled(assignment);

        Assert.Equal(
            GenesisPortMappingOutcome.RouterNotFound,
            routerMissing.Outcome);
        Assert.Null(routerMissing.Assignment);
        Assert.Equal(GenesisPortMappingOutcome.Failed, mapFailed.Outcome);
        Assert.Equal("mapping-failed", mapFailed.DiagnosticCode);
        Assert.Equal(GenesisPortMappingOutcome.Cancelled, mapCancelled.Outcome);
        Assert.Equal(GenesisPortUnmappingOutcome.Unmapped, unmapped.Outcome);
        Assert.Same(assignment, unmapped.Assignment);
        Assert.Equal(GenesisPortUnmappingOutcome.Failed, unmapFailed.Outcome);
        Assert.Equal("unmap-failed", unmapFailed.DiagnosticCode);
        Assert.Equal(
            GenesisPortUnmappingOutcome.Cancelled,
            unmapCancelled.Outcome);
    }

    [Fact]
    public void DiagnosticCodesRemainBounded()
    {
        var request = new GenesisPortMappingRequest(
            "primary-v4",
            internalPort: 7144);
        var tooLong = new string(
            'x',
            GenesisPortMappingDiagnostic.MaximumCodeCharacters + 1);

        Assert.Throws<ArgumentException>(
            () => GenesisPortMappingDiscoveryResult.Failed(tooLong));
        Assert.Throws<ArgumentException>(
            () => GenesisPortMappingResult.Failed(request, tooLong));
    }

    [Fact]
    public void StateModelCoversRequiredGuiPhases()
    {
        GenesisPortMappingState[] requiredStates =
        [
            GenesisPortMappingState.Disabled,
            GenesisPortMappingState.Discovering,
            GenesisPortMappingState.RouterNotFound,
            GenesisPortMappingState.Mapping,
            GenesisPortMappingState.Mapped,
            GenesisPortMappingState.Failed,
        ];

        Assert.Equal(6, requiredStates.Distinct().Count());
    }
}
