using System.Globalization;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackSignatureCommandTests
{
    private const string ChannelId =
        "531dc8dfc7fb42928ac2c0a626517a87";

    [Fact]
    public void Parse_UsesFiniteContentSafeDefaults()
    {
        var options = PlaybackSignatureCommandOptions.Parse(
            ["--playback-signature-probe", "--channel", ChannelId]);

        Assert.Equal("127.0.0.1", options.UpstreamHost);
        Assert.Equal(7144, options.UpstreamPort);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.IdleTimeout);
        Assert.Equal(0, options.Reconnects);
        Assert.Equal(64 * 1024, options.MaxProbeBytes);
        Assert.Equal(4, options.MaxDiscontinuities);
    }

    [Fact]
    public void Parse_ValidExplicitValuesAreCultureIndependent()
    {
        var options = PlaybackSignatureCommandOptions.Parse(
            [
                "--playback-signature-probe",
                "--channel", ChannelId,
                "--host", "::1",
                "--port", "27144",
                "--position", "4294967296",
                "--playback-probe-timeout-ms", "20000",
                "--playback-idle-timeout-ms", "12000",
                "--playback-reconnects", "2",
                "--playback-probe-max-bytes", "131072",
                "--playback-probe-max-discontinuities", "8",
            ]);

        Assert.Equal("::1", options.UpstreamHost);
        Assert.Equal(27144, options.UpstreamPort);
        Assert.Equal(4_294_967_296, options.InitialPosition);
        Assert.Equal(TimeSpan.FromSeconds(20), options.Duration);
        Assert.Equal(TimeSpan.FromSeconds(12), options.IdleTimeout);
        Assert.Equal(2, options.Reconnects);
        Assert.Equal(128 * 1024, options.MaxProbeBytes);
        Assert.Equal(8, options.MaxDiscontinuities);
    }

    public static TheoryData<string[]> InvalidArguments =>
        new()
        {
            { ["--playback-signature-probe"] },
            { ["--playback-signature-probe", "--channel", "invalid"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--port", "0"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--playback-probe-timeout-ms", "999"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--playback-idle-timeout-ms", "999"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--playback-reconnects", "11"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--playback-probe-max-bytes", "15"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--playback-probe-max-discontinuities", "65"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--host", "bad\r\nhost"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--unknown", "1"] },
            { ["--playback-signature-probe", "--channel", ChannelId, "--channel", ChannelId] },
        };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Parse_InvalidOrAmbiguousInputIsRejected(string[] arguments)
    {
        Assert.Throws<ArgumentException>(
            () => PlaybackSignatureCommandOptions.Parse(arguments));
    }

    [Fact]
    public void Summary_AcceptsOnlyCompleteRecognizedContentSafeResult()
    {
        var summary = CreatePassingSummary();

        Assert.True(summary.IsAccepted(
            PlaybackSignatureStopTrigger.Recognized));
        Assert.False(summary.IsAccepted(
            PlaybackSignatureStopTrigger.Timeout));
        Assert.False((summary with
        {
            ClassificationState =
                PlaybackMediaClassificationState.LimitReached,
        }).IsAccepted(PlaybackSignatureStopTrigger.Recognized));
        Assert.False((summary with
        {
            Container = PlaybackMediaContainer.Unknown,
        }).IsAccepted(PlaybackSignatureStopTrigger.Recognized));
        Assert.False((summary with
        {
            BufferSkippedPacketCount = 1,
        }).IsAccepted(PlaybackSignatureStopTrigger.Recognized));
        Assert.False((summary with
        {
            BufferOutstandingPacketCount = 1,
        }).IsAccepted(PlaybackSignatureStopTrigger.Recognized));
        Assert.False((summary with
        {
            ShutdownCompleted = false,
        }).IsAccepted(PlaybackSignatureStopTrigger.Recognized));
    }

    [Fact]
    public void Summary_ExposesOnlyFixedEnumsNumbersAndBoolean()
    {
        var allowed = new HashSet<Type>
        {
            typeof(bool),
            typeof(int),
            typeof(long),
            typeof(int?),
            typeof(PlaybackMediaClassificationState),
            typeof(PlaybackMediaContainer),
            typeof(PlaybackMediaClassificationBasis?),
            typeof(PlaybackMediaClassificationLimit?),
            typeof(PcpChannelSessionEndReason?),
        };

        var unexpected = typeof(PlaybackSignatureSummary)
            .GetProperties()
            .Where(property => !allowed.Contains(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public async Task Run_ConnectionFailureProducesFixedSanitizedOutput()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var unusedPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var options = new PlaybackSignatureCommandOptions
        {
            ChannelId = Guid.ParseExact(ChannelId, "N"),
            UpstreamPort = unusedPort,
            Duration = TimeSpan.FromSeconds(8),
            IdleTimeout = TimeSpan.FromSeconds(4),
            MaxProbeBytes = 16,
        };
        using var output = new StringWriter(
            CultureInfo.InvariantCulture);

        var result = await PlaybackSignatureCommand.RunAsync(
            options,
            output);
        var text = output.ToString();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.AcceptancePassed);
        Assert.Equal(PlaybackSignatureStopTrigger.Failure, result.StopTrigger);
        Assert.DoesNotContain(ChannelId, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
        Assert.Contains("Container: Unknown", text, StringComparison.Ordinal);
        Assert.Contains(
            "Classification Gate: Failed",
            text,
            StringComparison.Ordinal);
    }

    private static PlaybackSignatureSummary CreatePassingSummary()
    {
        return new PlaybackSignatureSummary(
            PlaybackMediaClassificationState.Recognized,
            PlaybackMediaContainer.FlashVideo,
            PlaybackMediaClassificationBasis.FileHeader,
            Limit: null,
            ObservedPacketCount: 1,
            ExaminedByteCount: 9,
            DiscontinuityCount: 0,
            PcpChannelSessionEndReason.Stopped,
            AttemptCount: 1,
            ReconnectCount: 0,
            HttpStatusCode: 200,
            SessionPacketCount: 1,
            SessionPayloadByteCount: 9,
            BufferSkippedPacketCount: 0,
            BufferSkippedByteCount: 0,
            BufferPendingPacketCount: 0,
            BufferOutstandingPacketCount: 0,
            ShutdownCompleted: true);
    }
}
