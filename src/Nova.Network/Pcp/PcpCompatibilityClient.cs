using System.Collections.Immutable;
using System.IO.Pipelines;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Performs the legacy relay-entry and HELO/OLEH compatibility probe.
///
/// The normal probe closes the connection after OK or QUIT. The explicit
/// observation path may read a finite number of post-handshake atom trees but
/// retains only content-free shapes.
/// </summary>
public sealed class PcpCompatibilityClient
{
    private readonly TcpTransportConnector _connector;
    private readonly PcpCompatibilityOptions _options;

    public PcpCompatibilityClient(
        TcpTransportConnector connector,
        PcpCompatibilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connector);

        _connector = connector;
        _options = options is null
            ? new PcpCompatibilityOptions()
            : options with { };

        _options.Validate();
    }

    public Task<PcpCompatibilityProbeResult> ProbeAsync(
        string host,
        int port,
        Guid channelId,
        long streamPosition = 0,
        int pcpVersion = 1,
        CancellationToken cancellationToken = default)
    {
        return ProbeCoreAsync(
            host,
            port,
            channelId,
            observationOptions: null,
            streamPosition,
            pcpVersion,
            cancellationToken);
    }

    public Task<PcpCompatibilityProbeResult> ProbeAndObserveAsync(
        string host,
        int port,
        Guid channelId,
        PcpPostHandshakeObservationOptions observationOptions,
        long streamPosition = 0,
        int pcpVersion = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observationOptions);

        var copiedOptions = observationOptions with { };
        copiedOptions.Validate();

        return ProbeCoreAsync(
            host,
            port,
            channelId,
            copiedOptions,
            streamPosition,
            pcpVersion,
            cancellationToken);
    }

    private async Task<PcpCompatibilityProbeResult> ProbeCoreAsync(
        string host,
        int port,
        Guid channelId,
        PcpPostHandshakeObservationOptions? observationOptions,
        long streamPosition,
        int pcpVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        using var timeoutSource = new CancellationTokenSource(
            _options.HandshakeTimeout);

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        var token = linkedSource.Token;

        try
        {
            await using var connection = await _connector.ConnectAsync(
                host,
                port,
                token);

            var pipeReader = PipeReader.Create(
                connection.TransportStream,
                new StreamPipeReaderOptions(leaveOpen: true));

            try
            {
                var handshake = await PcpRelayHandshakeClient.NegotiateAsync(
                    connection,
                    pipeReader,
                    host,
                    port,
                    channelId,
                    Guid.NewGuid(),
                    streamPosition,
                    pcpVersion,
                    new PcpRelayHandshakeSettings(
                        _options.MaxHttpHeaderBytes,
                        _options.MaxHandshakeAtomBytes,
                        _options.MaxAtomsBeforeTerminal,
                        _options.UserAgent,
                        _options.ServantVersion),
                    token).ConfigureAwait(false);

                var result = handshake.ToCompatibilityResult();

                if (result.Outcome != PcpCompatibilityOutcome.Accepted ||
                    observationOptions is null)
                {
                    return result;
                }

                timeoutSource.CancelAfter(Timeout.InfiniteTimeSpan);

                var observation = await ObservePostHandshakeAsync(
                    pipeReader,
                    observationOptions,
                    cancellationToken).ConfigureAwait(false);

                return result with
                {
                    PostHandshakeObservation = observation,
                };
            }
            finally
            {
                await pipeReader.CompleteAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The PCP compatibility probe exceeded {_options.HandshakeTimeout}.",
                exception);
        }
    }

    private static async Task<PcpPostHandshakeObservation>
        ObservePostHandshakeAsync(
            PipeReader pipeReader,
            PcpPostHandshakeObservationOptions options,
            CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(options.Timeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        var token = linkedSource.Token;
        var shapes = ImmutableArray.CreateBuilder<PcpAtomShape>(
            options.MaxTopLevelAtoms);

        long observedWireBytes = 0;

        await using var atomReader = new PcpAtomPipeMessageReader(
            pipeReader,
            new PcpProtocolLimits
            {
                MaxContentLength = options.MaxAtomBytes,
            },
            options.MaxAtomBytes);

        try
        {
            for (var index = 0; index < options.MaxTopLevelAtoms; index++)
            {
                var atom = await atomReader.ReadNextAsync(token)
                    .ConfigureAwait(false);

                var shape = PcpAtomShape.FromAtom(atom);
                shapes.Add(shape);
                observedWireBytes = checked(
                    observedWireBytes + shape.EncodedLength);

                if (atom.Name == PcpKnownAtomNames.Quit)
                {
                    return new PcpPostHandshakeObservation(
                        shapes.ToImmutable(),
                        PcpPostHandshakeStopReason.QuitReceived,
                        observedWireBytes);
                }
            }

            return new PcpPostHandshakeObservation(
                shapes.MoveToImmutable(),
                PcpPostHandshakeStopReason.TopLevelAtomLimitReached,
                observedWireBytes);
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            return new PcpPostHandshakeObservation(
                shapes.ToImmutable(),
                PcpPostHandshakeStopReason.TimedOut,
                observedWireBytes);
        }
        catch (PcpProtocolException exception)
            when (exception.Error ==
                PcpParseError.ContentLengthLimitExceeded)
        {
            return new PcpPostHandshakeObservation(
                shapes.ToImmutable(),
                PcpPostHandshakeStopReason.AtomSizeLimitExceeded,
                observedWireBytes);
        }
        catch (PcpProtocolException exception)
            when (exception.Error ==
                PcpParseError.UnexpectedEndOfStream)
        {
            return new PcpPostHandshakeObservation(
                shapes.ToImmutable(),
                PcpPostHandshakeStopReason.ConnectionEnded,
                observedWireBytes);
        }
    }

}
