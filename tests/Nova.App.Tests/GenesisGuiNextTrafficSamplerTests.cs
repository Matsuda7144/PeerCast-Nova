using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextTrafficSamplerTests
{
    private sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    private readonly Clock clock = new();
    private readonly Guid scope = Guid.NewGuid();
    private PcpTransportSnapshot Snapshot(long at, long sent = 0, long received = 0, long generation = 1, int count = 2)
    {
        clock.Now = Math.Max(clock.Now, at);
        return new(scope, generation, count, sent, received, true, at, 1000);
    }

    [Fact]
    public void UsesElapsedTimeAndActualDeltasWithExplicitScopeAndSessionLabel()
    {
        var sampler = new GenesisGuiNextTrafficSampler(clock);
        var first = sampler.Sample(Snapshot(1000, 9000, 10000));
        Assert.Equal("2", first.Connections.Text);
        Assert.Equal("—", first.Up.Text);
        var next = sampler.Sample(Snapshot(3000, 259000, 510000));
        Assert.Equal("1 Mbps", next.Up.Text);
        Assert.Equal("2 Mbps", next.Down.Text);
        Assert.Equal("1M", next.Up.Compact);
        var display = GenesisGuiNextTopStatusPresentation.Create(null, null, next);
        Assert.Equal("PCP", GenesisGuiNextTopStatusPresentation.Label(1, true));
        Assert.Contains("固有Peer数や物理ワイヤ量ではありません", display.AccessibleText);
        Assert.Contains("OBS・YP Announce・HTTP", display.AccessibleText);
    }

    [Fact]
    public void DistinguishesIdleZeroTinyPositiveAndUnsupportedObservations()
    {
        var sampler = new GenesisGuiNextTrafficSampler(clock);
        Assert.Equal("—", sampler.Sample(null).Connections.Text);
        sampler.Sample(Snapshot(0));
        Assert.Equal("0 kbps", sampler.Sample(Snapshot(1000)).Down.Text);
        Assert.Equal("<0.1 kbps", sampler.Sample(Snapshot(2000, received: 1)).Down.Text);
        Assert.Equal("—", sampler.Sample(null).Down.Text);
        Assert.Equal("—", sampler.Sample(Snapshot(3000)).Down.Text);
    }

    [Fact]
    public void RepeatedSamplesDoNotRecalculateAndStaleObservationsExpire()
    {
        var sampler = new GenesisGuiNextTrafficSampler(clock);
        var first = Snapshot(0);
        sampler.Sample(first);
        Assert.Equal("—", sampler.Sample(Snapshot(100, 100)).Up.Text);
        var sample = Snapshot(1000, 1000);
        var value = sampler.Sample(sample);
        Assert.Equal("8 kbps", value.Up.Text);
        Assert.Equal(value, sampler.Sample(sample));
        clock.Now = 3001;
        Assert.Equal("—", sampler.Sample(sample).Connections.Text);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("scope")]
    [InlineData("reset")]
    [InlineData("invalid")]
    [InlineData("backward")]
    [InlineData("frequency")]
    [InlineData("stopped")]
    public void DiscontinuitiesNeverBorrowThePreviousRate(string change)
    {
        var sampler = new GenesisGuiNextTrafficSampler(clock);
        sampler.Sample(Snapshot(1000, 1000));
        Assert.Equal("8 kbps", sampler.Sample(Snapshot(2000, 2000)).Up.Text);
        var sample = Snapshot(3000, 3000);
        sample = change switch
        {
            "generation" => sample with { Generation = 2 },
            "scope" => sample with { Scope = Guid.NewGuid() },
            "reset" => sample with { SentBytes = 0 },
            "invalid" => sample with { IsValid = false },
            "backward" => sample with { Timestamp = 1500 },
            "frequency" => sample with { TimestampFrequency = 0 },
            "stopped" => sample with { Generation = 2, Connections = 0, SentBytes = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        var value = sampler.Sample(sample);
        Assert.Equal("—", value.Up.Text);
        if (change == "stopped")
        {
            Assert.Equal("0", value.Connections.Text);
            var idle = sampler.Sample(Snapshot(4000, generation: 2, count: 0));
            Assert.Equal("0 kbps", idle.Up.Text);
            Assert.Equal("0 kbps", idle.Down.Text);
        }
    }

    [Fact]
    public void CompactRateFitsWithoutLosingItsUnit()
    {
        var sampler = new GenesisGuiNextTrafficSampler(clock);
        sampler.Sample(Snapshot(0));
        var value = sampler.Sample(Snapshot(1000, 116250));
        Assert.Equal("930 kbps", value.Up.Text);
        Assert.Equal("930k", value.Up.Compact);
    }

    [Fact]
    public void ExtremeCountersDoNotOverflowOrProduceAnInvalidRate()
    {
        var sampler = new GenesisGuiNextTrafficSampler(clock);
        sampler.Sample(Snapshot(0));
        var value = sampler.Sample(Snapshot(1000, long.MaxValue, long.MaxValue));
        Assert.Equal("73.8 Ebps", value.Up.Text);
        Assert.Equal(value.Up, value.Down);
    }
}
