using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class NatPmpMappingBackendTests
{
    [Fact]
    public void MapPacket_UsesTcpOpcodeAndNetworkByteOrder()
    {
        var request = Request();
        Assert.Equal(new byte[] { 0, 2, 0, 0, 0x1b, 0xe8, 0x1b, 0xe8, 0, 0, 0, 120 },
            NatPmpMappingBackend.EncodeRequest(request));
    }

    [Fact]
    public void DeletePacket_TargetsInternalPortWithZeroExternalPortAndLifetime()
    {
        var request = Request() with
        {
            Operation = NatMappingOperation.Unmap,
            SuggestedExternalPort = 0,
            RequestedLifetimeSeconds = 0,
        };
        Assert.Equal(new byte[] { 0, 2, 0, 0, 0x1b, 0xe8, 0, 0, 0, 0, 0, 0 },
            NatPmpMappingBackend.EncodeRequest(request));
        Assert.Throws<ArgumentException>(() => NatPmpMappingBackend.EncodeRequest(
            request with { Identity = request.Identity with { InternalPort = 0 } }));
    }

    [Fact]
    public void MappingResponse_ReturnsRouterPortLifetimeAndEpoch()
    {
        byte[] packet = [0, 130, 0, 0, 0, 0, 0, 10, 0x1b, 0xe8, 0xa4, 0x10, 0, 0, 0, 15];
        var result = NatPmpMappingBackend.DecodeResponse(Request(), packet);
        Assert.NotNull(result);
        Assert.Equal(NatBackendOutcome.Success, result.Outcome);
        Assert.Equal(42000, result.ExternalPort);
        Assert.Equal(15u, result.LifetimeSeconds);
        Assert.Equal(10u, result.RouterEpochSeconds);
    }

    [Theory]
    [InlineData(0, 1)] // wrong protocol version
    [InlineData(1, 129)] // UDP response to TCP request
    [InlineData(8, 0)] // wrong internal port
    [InlineData(12, 1)] // altered lifetime is accepted, not discarded
    public void ResponseMatching_RejectsWrongIdentityButPreservesRouterLifetime(int index, byte value)
    {
        byte[] packet = [0, 130, 0, 0, 0, 0, 0, 10, 0x1b, 0xe8, 0xa4, 0x10, 0, 0, 0, 15];
        packet[index] = value;
        var result = NatPmpMappingBackend.DecodeResponse(Request(), packet);
        if (index == 12)
        {
            Assert.Equal(16777231u, result!.LifetimeSeconds);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public void RouterRefusal_IsExplicitRejectedEvidence()
    {
        byte[] packet = [0, 130, 0, 2, 0, 0, 0, 10, 0x1b, 0xe8, 0, 0, 0, 0, 0, 0];
        var result = NatPmpMappingBackend.DecodeResponse(Request(), packet);
        Assert.Equal(NatBackendOutcome.Rejected, result!.Outcome);
        Assert.Equal("NatPmpResult2", result.DetailCode);
        Assert.Null(result.LifetimeSeconds);
        packet[8] = 0;
        Assert.Null(NatPmpMappingBackend.DecodeResponse(Request(), packet));
    }

    [Fact]
    public void DeleteConfirmation_RequiresZeroLifetimeAndZeroExternalPort()
    {
        var request = Request() with { Operation = NatMappingOperation.Unmap };
        byte[] packet = [0, 130, 0, 0, 0, 0, 0, 10, 0x1b, 0xe8, 0, 0, 0, 0, 0, 0];
        Assert.Equal(NatBackendOutcome.Success, NatPmpMappingBackend.DecodeResponse(request, packet)!.Outcome);
        packet[11] = 1;
        Assert.Null(NatPmpMappingBackend.DecodeResponse(request, packet));
    }

    private static NatMappingRequest Request() => new(Guid.NewGuid(), 1,
        new NatMappingIdentity(NatPortMappingTestHarness.Home, NatMappingProtocol.Tcp, 7144),
        NatMappingOperation.Map, 7144, 120, 1);
}
