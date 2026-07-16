using System.Globalization;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Cli;

internal enum RelayDiagnosticStopTrigger
{
    WindowElapsed,
    CoordinatorEnded,
    CallerCanceled,
}

internal sealed record RelayDiagnosticSummary(
    PcpRelayCoordinatorEndReason EndReason,
    PcpRelayDrainOutcome DrainOutcome,
    long IngressPacketCount,
    long IngressPayloadByteCount,
    long AcceptedHandshakeCount,
    long DeliveredPacketCount,
    long DeliveredByteCount,
    long DiscardedPacketCount,
    long CleanupIncompleteCount,
    long InternalFailureCount,
    bool ShutdownCompleted)
{
    public bool IsAccepted(int minimumPackets)
    {
        return EndReason is
                PcpRelayCoordinatorEndReason.Stopped or
                PcpRelayCoordinatorEndReason.UpstreamEnded &&
            IngressPacketCount >= minimumPackets &&
            IngressPayloadByteCount > 0 &&
            AcceptedHandshakeCount >= 1 &&
            DeliveredPacketCount >= minimumPackets &&
            DeliveredByteCount > 0 &&
            DiscardedPacketCount == 0 &&
            CleanupIncompleteCount == 0 &&
            InternalFailureCount == 0 &&
            ShutdownCompleted;
    }

    public static RelayDiagnosticSummary From(
        PcpRelayCoordinatorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var downstream = result.Downstream?.Snapshot;

        return new RelayDiagnosticSummary(
            result.EndReason,
            result.DrainOutcome,
            result.Ingress.AcceptedPacketCount,
            result.Ingress.AcceptedPayloadByteCount,
            downstream?.AcceptedHandshakeCount ?? 0,
            downstream?.DeliveredPacketCount ?? 0,
            downstream?.DeliveredByteCount ?? 0,
            downstream?.DiscardedPacketCount ?? 0,
            downstream?.CleanupIncompleteCount ?? 0,
            downstream?.InternalFailureCount ?? 0,
            result.ShutdownCompleted);
    }
}

internal sealed record RelayDiagnosticCommandResult(
    int ExitCode,
    bool AcceptancePassed,
    PcpRelayCoordinatorReadinessOutcome Readiness,
    RelayDiagnosticStopTrigger StopTrigger,
    RelayDiagnosticSummary Summary);

internal static class RelayDiagnosticCommand
{
    public static async Task<RelayDiagnosticCommandResult> RunAsync(
        RelayDiagnosticCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConcurrentConnections = 8,
        };
        var tracker = new ChannelStatusTracker(
            new ChannelId(options.ChannelId),
            new ChannelSessionId(Guid.NewGuid()));
        await using var coordinator = new PcpRelayCoordinator(
            new TcpTransportConnector(transportOptions),
            tracker,
            new IPEndPoint(IPAddress.Loopback, options.ListenPort),
            new PcpRelayCoordinatorOptions
            {
                DrainTimeout = TimeSpan.FromSeconds(2),
                UpstreamSession = new PcpChannelSessionOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(5),
                    MaxReconnectAttempts = options.Reconnects,
                    ReconnectDelay = TimeSpan.FromMilliseconds(100),
                    Stream = new PcpChannelStreamOptions
                    {
                        ReadIdleTimeout = options.IdleTimeout,
                    },
                },
                DownstreamTransport = transportOptions,
                Fanout = new PcpRelayFanoutOptions
                {
                    MaxChildren = 4,
                    MaxPendingPacketsPerChild = 16,
                    MaxPendingBytesPerChild = 4L * 1024 * 1024,
                    ChildStopTimeout = TimeSpan.FromSeconds(2),
                },
                DownstreamChild = new PcpDownstreamChildOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(5),
                    WriteTimeout = TimeSpan.FromSeconds(3),
                },
            });

        var runTask = coordinator.RunAsync(
            options.UpstreamHost,
            options.UpstreamPort,
            options.InitialPosition,
            cancellationToken);
        var readiness = await coordinator.WaitForReadinessAsync(
            CancellationToken.None)
            .ConfigureAwait(false);

        if (readiness == PcpRelayCoordinatorReadinessOutcome.Ready)
        {
            await output.WriteLineAsync(
                "Relay State: Ready").ConfigureAwait(false);
            await output.WriteLineAsync(
                $"Relay Listen: 127.0.0.1:{options.ListenPort.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
            await output.WriteLineAsync(
                $"Relay Window Milliseconds: {options.Duration.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        }

        var windowTask = Task.Delay(
            options.Duration,
            CancellationToken.None);
        var first = await Task.WhenAny(
            runTask,
            windowTask).ConfigureAwait(false);
        RelayDiagnosticStopTrigger stopTrigger;

        if (ReferenceEquals(first, windowTask))
        {
            stopTrigger = RelayDiagnosticStopTrigger.WindowElapsed;
            coordinator.RequestStop();
        }
        else
        {
            stopTrigger = cancellationToken.IsCancellationRequested
                ? RelayDiagnosticStopTrigger.CallerCanceled
                : RelayDiagnosticStopTrigger.CoordinatorEnded;
        }

        var result = await runTask.ConfigureAwait(false);
        var summary = RelayDiagnosticSummary.From(result);
        var accepted = summary.IsAccepted(options.MinimumPackets);

        await WriteSummaryAsync(
            output,
            stopTrigger,
            summary,
            accepted).ConfigureAwait(false);

        return new RelayDiagnosticCommandResult(
            accepted ? 0 : 1,
            accepted,
            readiness,
            stopTrigger,
            summary);
    }

    private static async Task WriteSummaryAsync(
        TextWriter output,
        RelayDiagnosticStopTrigger stopTrigger,
        RelayDiagnosticSummary summary,
        bool accepted)
    {
        await output.WriteLineAsync(
            $"Relay Stop Trigger: {stopTrigger}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Relay End: {summary.EndReason}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Drain: {summary.DrainOutcome}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Ingress Packets: {summary.IngressPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Ingress Payload Bytes: {summary.IngressPayloadByteCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Accepted Handshakes: {summary.AcceptedHandshakeCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Delivered Packets: {summary.DeliveredPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Delivered Bytes: {summary.DeliveredByteCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Discarded Packets: {summary.DiscardedPacketCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Cleanup Incomplete: {summary.CleanupIncompleteCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Internal Failures: {summary.InternalFailureCount.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Shutdown Completed: {summary.ShutdownCompleted}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Acceptance Gate: {(accepted ? "Passed" : "Failed")}").ConfigureAwait(false);
    }
}
