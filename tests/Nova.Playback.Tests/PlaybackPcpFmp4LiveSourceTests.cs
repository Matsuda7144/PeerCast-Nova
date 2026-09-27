namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPcpFmp4LiveSourceTests
{
    [Fact]
    public void Parses_explicit_direct_pcp_channel_uri()
    {
        const string channel = "642ed9e0b91349e0abce5b4e6ec1cc38";
        Assert.True(PlaybackPcpFmp4LiveSource.TryParse(
            $"pcp://relay.example:7144/channel/{channel}",
            out var source,
            initialPosition: 1234));

        var parsed = Assert.IsType<PlaybackPcpFmp4LiveSource>(source);
        Assert.Equal("relay.example", parsed.Host);
        Assert.Equal(7144, parsed.Port);
        Assert.Equal(Guid.ParseExact(channel, "N"), parsed.ChannelId.Value);
        Assert.Equal(1234, parsed.InitialPosition);
    }

    [Fact]
    public void Parses_ipv6_direct_pcp_channel_uri()
    {
        Assert.True(PlaybackPcpFmp4LiveSource.TryParse(
            "pcp://[::1]:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38",
            out var source));

        var parsed = Assert.IsType<PlaybackPcpFmp4LiveSource>(source);
        Assert.Equal("::1", parsed.Host);
        Assert.Equal(7144, parsed.Port);
    }

    [Theory]
    [InlineData("http://relay.example:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38")]
    [InlineData("pcp://relay.example/channel/642ed9e0b91349e0abce5b4e6ec1cc38")]
    [InlineData("pcp://user@relay.example:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38")]
    [InlineData("pcp://relay.example:7144/channel/not-a-guid")]
    [InlineData("pcp://relay.example:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38/extra")]
    [InlineData("pcp://relay.example:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38?format=flv")]
    [InlineData("pcp://relay.example:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38#fragment")]
    public void Rejects_non_explicit_or_ambiguous_source_uris(string uri)
    {
        Assert.False(PlaybackPcpFmp4LiveSource.TryParse(uri, out var source));
        Assert.Null(source);
    }

    [Fact]
    public void Rejects_negative_start_position()
    {
        Assert.False(PlaybackPcpFmp4LiveSource.TryParse(
            "pcp://relay.example:7144/channel/642ed9e0b91349e0abce5b4e6ec1cc38",
            out var source,
            initialPosition: -1));
        Assert.Null(source);
    }
}
