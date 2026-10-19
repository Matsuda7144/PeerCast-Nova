using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamRelayRequestValidatorTests
{
    private static readonly Guid ChannelId = Guid.ParseExact(
        "531dc8dfc7fb42928ac2c0a626517a87",
        "N");

    [Fact]
    public void ValidateExtractsRoutingIdentityAndStreamPosition()
    {
        var validation = PcpDownstreamRelayRequestValidator.Validate(
            CreateRequest(ChannelId, position: "42"),
            expectedPcpVersion: 1);

        Assert.True(validation.IsAccepted);
        Assert.Equal(200, validation.StatusCode);
        Assert.NotNull(validation.Request);
        Assert.Equal(ChannelId, validation.Request.ChannelId);
        Assert.Equal(42, validation.Request.StreamPosition);
    }

    [Fact]
    public void ValidateKeepsDifferentWellFormedChannelRoutable()
    {
        var otherChannelId = Guid.ParseExact(
            "102132435465768798a9babbdcddedef",
            "N");

        var validation = PcpDownstreamRelayRequestValidator.Validate(
            CreateRequest(otherChannelId, position: "0"),
            expectedPcpVersion: 1);

        Assert.True(validation.IsAccepted);
        Assert.Equal(otherChannelId, validation.Request!.ChannelId);
    }

    [Fact]
    public void ValidateRejectsUnsupportedMethodBeforeRouting()
    {
        var request = CreateRequest(ChannelId, position: "0") with
        {
            Method = "POST",
        };

        var validation = PcpDownstreamRelayRequestValidator.Validate(
            request,
            expectedPcpVersion: 1);

        Assert.False(validation.IsAccepted);
        Assert.Equal(400, validation.StatusCode);
        Assert.Null(validation.Request);
    }

    [Fact]
    public void ValidateRejectsMalformedChannelPathAsNotFound()
    {
        var request = CreateRequest(ChannelId, position: "0") with
        {
            RequestTarget = "/channel/not-a-channel-id",
        };

        var validation = PcpDownstreamRelayRequestValidator.Validate(
            request,
            expectedPcpVersion: 1);

        Assert.False(validation.IsAccepted);
        Assert.Equal(404, validation.StatusCode);
        Assert.Null(validation.Request);
    }

    [Theory]
    [InlineData("2", "0")]
    [InlineData("1", "not-a-position")]
    public void ValidateRejectsInvalidPcpHeaders(
        string pcpVersion,
        string position)
    {
        var validation = PcpDownstreamRelayRequestValidator.Validate(
            CreateRequest(ChannelId, position, pcpVersion),
            expectedPcpVersion: 1);

        Assert.False(validation.IsAccepted);
        Assert.Equal(400, validation.StatusCode);
        Assert.Null(validation.Request);
    }

    private static PcpHttpRequestHeader CreateRequest(
        Guid channelId,
        string position,
        string pcpVersion = "1")
    {
        return new PcpHttpRequestHeader(
            "GET",
            $"/channel/{channelId:N}",
            new Version(1, 1),
            new Dictionary<string, string>
            {
                ["x-peercast-pcp"] = pcpVersion,
                ["x-peercast-pos"] = position,
            });
    }
}
