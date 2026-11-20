using System.Globalization;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackDecoderLifecycleProbeTests
{
    private const string ChannelId =
        "531dc8dfc7fb42928ac2c0a626517a87";

    [Fact]
    public void Parse_UsesFiniteContentSafeDefaults()
    {
        var options = PlaybackDecoderLifecycleProbeOptions.Parse(
            [
                "--playback-decoder-lifecycle-probe",
                "--channel",
                ChannelId,
            ]);

        Assert.Equal("127.0.0.1", options.UpstreamHost);
        Assert.Equal(7144, options.UpstreamPort);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.IdleTimeout);
        Assert.Equal(
            TimeSpan.FromSeconds(2),
            options.DecoderStopTimeout);
        Assert.Equal(0, options.Reconnects);
        Assert.Equal(64 * 1024, options.MaxProbeBytes);
        Assert.Equal(4, options.MaxDiscontinuities);
        Assert.Equal(4, options.InputLimit);
    }

    [Fact]
    public void Parse_ValidExplicitValuesAreCultureIndependent()
    {
        var options = PlaybackDecoderLifecycleProbeOptions.Parse(
            [
                "--playback-decoder-lifecycle-probe",
                "--channel", ChannelId,
                "--host", "::1",
                "--port", "27144",
                "--position", "4294967296",
                "--playback-lifecycle-duration-ms", "20000",
                "--playback-idle-timeout-ms", "12000",
                "--playback-decoder-stop-timeout-ms", "1500",
                "--playback-reconnects", "2",
                "--playback-probe-max-bytes", "131072",
                "--playback-probe-max-discontinuities", "8",
                "--playback-lifecycle-inputs", "10",
            ]);

        Assert.Equal("::1", options.UpstreamHost);
        Assert.Equal(27144, options.UpstreamPort);
        Assert.Equal(4_294_967_296, options.InitialPosition);
        Assert.Equal(TimeSpan.FromSeconds(20), options.Duration);
        Assert.Equal(TimeSpan.FromSeconds(12), options.IdleTimeout);
        Assert.Equal(
            TimeSpan.FromMilliseconds(1_500),
            options.DecoderStopTimeout);
        Assert.Equal(2, options.Reconnects);
        Assert.Equal(128 * 1024, options.MaxProbeBytes);
        Assert.Equal(8, options.MaxDiscontinuities);
        Assert.Equal(10, options.InputLimit);
    }

    public static TheoryData<string[]> InvalidArguments =>
        new()
        {
            {
                ["--playback-decoder-lifecycle-probe"]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", "invalid",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--port", "0",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-lifecycle-duration-ms", "999",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-idle-timeout-ms", "999",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-decoder-stop-timeout-ms", "9",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-reconnects", "11",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-probe-max-bytes", "15",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-probe-max-discontinuities", "65",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--playback-lifecycle-inputs", "0",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--host", "bad\r\nhost",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--unknown", "1",
                ]
            },
            {
                [
                    "--playback-decoder-lifecycle-probe",
                    "--channel", ChannelId,
                    "--channel", ChannelId,
                ]
            },
        };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Parse_InvalidOrAmbiguousInputIsRejected(
        string[] arguments)
    {
        Assert.Throws<ArgumentException>(
            () => PlaybackDecoderLifecycleProbeOptions.Parse(
                arguments));
    }

    [Fact]
    public void Summary_AcceptsOnlyCompleteLifecycleEvidence()
    {
        var summary = CreatePassingSummary();

        Assert.True(summary.IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False(summary.IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 0));
        Assert.False(summary.IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.Timeout,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            ClassificationState =
                PlaybackMediaClassificationState.LimitReached,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            Basis = null,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            SubmittedInputCount = 3,
            DecoderInputCount = 3,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            SubmittedByteCount = 99,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            DecoderCompleteCount = 1,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            DecoderDisposeCount = 0,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            SessionEndReason =
                PcpChannelSessionEndReason.ConnectionEnded,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            AttemptCount = 0,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            BufferOutstandingPacketCount = 1,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
        Assert.False((summary with
        {
            ShutdownCompleted = false,
        }).IsAccepted(
            PlaybackDecoderLifecycleStopTrigger.InputLimit,
            requiredInputCount: 4));
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
            typeof(PlaybackDecoderRunEndReason?),
            typeof(PcpChannelSessionEndReason?),
        };
        var unexpected = typeof(PlaybackDecoderLifecycleSummary)
            .GetProperties()
            .Where(property =>
                !allowed.Contains(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public async Task ProbeDecoder_RecordsFixedCountersAndDisposesOnce()
    {
        var observedCount = 0L;
        var decoder = new PlaybackLifecycleProbeDecoder(
            count => observedCount = count);
        var payload = new byte[] { 1, 2, 3, 4 };
        var input = CreateEncodedInput(payload);

        await decoder.DecodeAsync(input, CancellationToken.None);
        await decoder.ResetAsync(CancellationToken.None);
        await decoder.CompleteAsync(CancellationToken.None);
        await decoder.DisposeAsync();
        await decoder.DisposeAsync();

        Assert.Equal(1, observedCount);
        Assert.Equal(1, decoder.InputCount);
        Assert.Equal(4, decoder.ByteCount);
        Assert.Equal(1, decoder.ResetCount);
        Assert.Equal(1, decoder.CompleteCount);
        Assert.Equal(1, decoder.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await decoder.DecodeAsync(
                input,
                CancellationToken.None));
    }

    [Fact]
    public void ProbeBackend_DeclaresAllRecognizedContainersWithoutCreatingDecoder()
    {
        var backend = new PlaybackLifecycleProbeDecoderFactory(_ => { });

        Assert.Equal("nova.lifecycle-probe", backend.Descriptor.Id);
        Assert.Equal(6, backend.Descriptor.SupportedContainers.Count);
        Assert.DoesNotContain(
            PlaybackMediaContainer.Unknown,
            backend.Descriptor.SupportedContainers);
        Assert.Equal(0, backend.CreateCount);
        Assert.Null(backend.LastCreated);
    }

    [Fact]
    public async Task Run_ConnectionFailureProducesFixedSanitizedOutput()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var unusedPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var options = new PlaybackDecoderLifecycleProbeOptions
        {
            ChannelId = Guid.ParseExact(ChannelId, "N"),
            UpstreamPort = unusedPort,
            // Allow the transport failure and teardown to finish before the probe deadline.
            Duration = TimeSpan.FromSeconds(10),
            IdleTimeout = TimeSpan.FromSeconds(1),
            MaxProbeBytes = 16,
        };
        using var output = new StringWriter(
            CultureInfo.InvariantCulture);

        var result = await PlaybackDecoderLifecycleProbeCommand.RunAsync(
            options,
            output);
        var text = output.ToString();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.AcceptancePassed);
        Assert.Equal(
            PlaybackDecoderLifecycleStopTrigger.Failure,
            result.StopTrigger);
        Assert.DoesNotContain(
            ChannelId,
            text,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "127.0.0.1",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Container: Unknown",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Lifecycle Gate: Failed",
            text,
            StringComparison.Ordinal);
    }

    private static PlaybackEncodedInput CreateEncodedInput(
        ReadOnlyMemory<byte> payload)
    {
        var constructor = typeof(PlaybackEncodedInput)
            .GetConstructors(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .Single();

        return (PlaybackEncodedInput)constructor.Invoke(
            [
                PlaybackEncodedInputKind.Packet,
                1L,
                null,
                PlaybackDiscontinuityKind.None,
                payload,
            ]);
    }

    private static PlaybackDecoderLifecycleSummary CreatePassingSummary()
    {
        return new PlaybackDecoderLifecycleSummary(
            PlaybackMediaClassificationState.Recognized,
            PlaybackMediaContainer.FlashVideo,
            PlaybackMediaClassificationBasis.FileHeader,
            PlaybackDecoderRunEndReason.OwnerStopped,
            SubmittedInputCount: 4,
            SubmittedByteCount: 100,
            RunnerResetCount: 1,
            FactoryCreateCount: 1,
            DecoderInputCount: 4,
            DecoderByteCount: 100,
            DecoderResetCount: 1,
            DecoderCompleteCount: 0,
            DecoderDisposeCount: 1,
            CleanupDeferred: false,
            DeferredCleanupPending: false,
            PcpChannelSessionEndReason.Stopped,
            AttemptCount: 1,
            ReconnectCount: 0,
            HttpStatusCode: 200,
            SessionPacketCount: 4,
            SessionPayloadByteCount: 100,
            BufferSkippedPacketCount: 0,
            BufferSkippedByteCount: 0,
            BufferPendingPacketCount: 0,
            BufferOutstandingPacketCount: 0,
            ShutdownCompleted: true);
    }
}
