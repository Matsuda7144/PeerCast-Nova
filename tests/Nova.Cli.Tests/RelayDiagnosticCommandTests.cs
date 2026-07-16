using System.Globalization;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Cli.Tests;

public sealed class RelayDiagnosticCommandTests
{
    private const string ChannelId = "531dc8dfc7fb42928ac2c0a626517a87";

    [Fact]
    public void Parse_WithRequiredChannelUsesFiniteLoopbackDefaults()
    {
        var options = RelayDiagnosticCommandOptions.Parse(
            ["--pcp-relay-diagnostics", "--channel", ChannelId]);

        Assert.Equal("127.0.0.1", options.UpstreamHost);
        Assert.Equal(7144, options.UpstreamPort);
        Assert.Equal(17145, options.ListenPort);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Duration);
        Assert.Equal(4, options.MinimumPackets);
        Assert.Equal(TimeSpan.FromSeconds(15), options.IdleTimeout);
        Assert.Equal(2, options.Reconnects);
    }

    [Fact]
    public void Parse_ValidExplicitValuesAreCultureIndependent()
    {
        var options = RelayDiagnosticCommandOptions.Parse(
            [
                "--pcp-relay-diagnostics",
                "--channel", ChannelId,
                "--host", "::1",
                "--port", "27144",
                "--position", "4294967296",
                "--relay-listen-port", "18145",
                "--relay-duration-ms", "45000",
                "--relay-min-packets", "12",
                "--relay-idle-timeout-ms", "20000",
                "--relay-reconnects", "3",
            ]);

        Assert.Equal("::1", options.UpstreamHost);
        Assert.Equal(27144, options.UpstreamPort);
        Assert.Equal(4_294_967_296, options.InitialPosition);
        Assert.Equal(18145, options.ListenPort);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Duration);
        Assert.Equal(12, options.MinimumPackets);
        Assert.Equal(TimeSpan.FromSeconds(20), options.IdleTimeout);
        Assert.Equal(3, options.Reconnects);
    }

    public static TheoryData<string[]> InvalidArguments =>
        new()
        {
            { ["--pcp-relay-diagnostics"] },
            { ["--pcp-relay-diagnostics", "--channel", "not-a-channel"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--port", "0"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--relay-listen-port", "65536"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--relay-duration-ms", "999"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--relay-min-packets", "0"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--relay-reconnects", "11"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--host", "bad\r\nhost"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--unknown", "1"] },
            { ["--pcp-relay-diagnostics", "--channel", ChannelId, "--channel", ChannelId] },
        };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Parse_InvalidOrAmbiguousInputIsRejected(string[] arguments)
    {
        Assert.Throws<ArgumentException>(
            () => RelayDiagnosticCommandOptions.Parse(arguments));
    }

    [Fact]
    public void Summary_PassesOnlyCompleteContentFreeRelayEvidence()
    {
        var summary = CreatePassingSummary();

        Assert.True(summary.IsAccepted(minimumPackets: 4));
        Assert.False((summary with
        {
            EndReason = PcpRelayCoordinatorEndReason.UpstreamFailed,
        }).IsAccepted(4));
        Assert.False((summary with
        {
            DeliveredPacketCount = 3,
        }).IsAccepted(4));
        Assert.False((summary with
        {
            DiscardedPacketCount = 1,
        }).IsAccepted(4));
        Assert.False((summary with
        {
            CleanupIncompleteCount = 1,
        }).IsAccepted(4));
        Assert.False((summary with
        {
            InternalFailureCount = 1,
        }).IsAccepted(4));
        Assert.False((summary with
        {
            ShutdownCompleted = false,
        }).IsAccepted(4));
    }

    [Fact]
    public void Summary_ExposesOnlyFixedEnumsNumbersAndBoolean()
    {
        var properties = typeof(RelayDiagnosticSummary).GetProperties();

        Assert.All(
            properties,
            property => Assert.True(
                property.PropertyType.IsEnum ||
                property.PropertyType == typeof(long) ||
                property.PropertyType == typeof(bool)));
    }

    [Fact]
    public async Task Run_OccupiedLoopbackPortFailsWithoutReadyOrSensitiveOutput()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();

        try
        {
            var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
            var options = new RelayDiagnosticCommandOptions
            {
                ChannelId = Guid.ParseExact(ChannelId, "N"),
                UpstreamPort = 1,
                ListenPort = port,
                Duration = TimeSpan.FromSeconds(1),
                MinimumPackets = 1,
                IdleTimeout = TimeSpan.FromSeconds(1),
                Reconnects = 0,
            };
            using var output = new StringWriter(
                CultureInfo.InvariantCulture);

            var result = await RelayDiagnosticCommand.RunAsync(
                options,
                output);
            var text = output.ToString();

            Assert.Equal(1, result.ExitCode);
            Assert.False(result.AcceptancePassed);
            Assert.Equal(
                PcpRelayCoordinatorReadinessOutcome.Failed,
                result.Readiness);
            Assert.Equal(
                PcpRelayCoordinatorEndReason.DownstreamFailed,
                result.Summary.EndReason);
            Assert.DoesNotContain("Relay State: Ready", text);
            Assert.DoesNotContain(ChannelId, text);
            Assert.Contains("Acceptance Gate: Failed", text);
        }
        finally
        {
            occupied.Stop();
        }
    }

    private static RelayDiagnosticSummary CreatePassingSummary()
    {
        return new RelayDiagnosticSummary(
            PcpRelayCoordinatorEndReason.Stopped,
            PcpRelayDrainOutcome.NotRequired,
            IngressPacketCount: 8,
            IngressPayloadByteCount: 32,
            AcceptedHandshakeCount: 1,
            DeliveredPacketCount: 4,
            DeliveredByteCount: 16,
            DiscardedPacketCount: 0,
            CleanupIncompleteCount: 0,
            InternalFailureCount: 0,
            ShutdownCompleted: true);
    }
}
