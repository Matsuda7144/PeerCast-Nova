using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Rtmp;

/// <summary>
/// One-shot IPv4 loopback ingest host. The first TCP client owns the generation;
/// additional clients are closed and its termination closes the listener.
/// The packet consumer is borrowed and must observe cancellation promptly.
/// Construction does not bind. After explicit Start, waiting for the first
/// publisher has no automatic deadline; caller cancellation or Dispose ends
/// that wait. IoTimeout applies only to connected protocol I/O.
/// </summary>
public sealed class ObsRtmpIngestHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ObsRtmpIngestOptions _options;
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly IPcpChannelPacketConsumer? _consumer;
    private readonly IObsRtmpEncodedMediaConsumer? _encodedConsumer;
    private readonly CancellationTokenSource _stop = new();
    private readonly ObsRtmpIngressAdmission _admission = new();
    private readonly ObsRtmpIngressObservation? _observation;
    private TcpListener? _listener;
    private Task<ObsRtmpIngestResult>? _completion;
    private Task? _disposeTask;
    private bool _listening;
    private bool _publisherConnected;
    private int? _boundPort;

    public ObsRtmpIngestHost(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IPcpChannelPacketConsumer consumer,
        ObsRtmpIngestOptions? options = null)
        : this(channelId, sessionId, consumer, options, null)
    {
    }

    /// <summary>Publishes admitted access units through the encoded media path.</summary>
    public static ObsRtmpIngestHost CreateForEncodedMedia(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IObsRtmpEncodedMediaConsumer consumer,
        ObsRtmpIngestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        return new(channelId, sessionId, null, options, null, consumer);
    }

    internal ObsRtmpIngestHost(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IPcpChannelPacketConsumer consumer,
        ObsRtmpIngestOptions? options,
        ObsRtmpIngressObservation? observation)
        : this(channelId, sessionId, consumer ?? throw new ArgumentNullException(nameof(consumer)),
            options, observation, null)
    {
    }

    private ObsRtmpIngestHost(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IPcpChannelPacketConsumer? consumer,
        ObsRtmpIngestOptions? options,
        ObsRtmpIngressObservation? observation,
        IObsRtmpEncodedMediaConsumer? encodedConsumer)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        _options = options is null ? new() : options with { };
        _options.Validate();
        _channelId = channelId;
        _sessionId = sessionId;
        _consumer = consumer;
        _encodedConsumer = encodedConsumer;
        _observation = observation;
    }

    public IPEndPoint LocalEndPoint
    {
        get
        {
            lock (_gate)
            {
                return _listener?.LocalEndpoint is IPEndPoint endpoint
                    ? new IPEndPoint(endpoint.Address, endpoint.Port)
                    : throw new InvalidOperationException("The RTMP listener is not running.");
            }
        }
    }

    public Task<ObsRtmpIngestResult> Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion ?? throw new InvalidOperationException("RTMP ingest has not started.");
            }
        }
    }

    internal (bool Listening, bool PublisherConnected, int? Port) ReadStatus()
    {
        lock (_gate)
        {
            return (_listening, _publisherConnected, _boundPort);
        }
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_completion is not null)
            {
                throw new InvalidOperationException("An RTMP ingest host supports one generation.");
            }

            var listener = new TcpListener(IPAddress.Loopback, _options.Port);
            listener.Start(backlog: 1);
            _listener = listener;
            _boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listening = true;
            _completion = RunAsync(listener, cancellationToken);
        }
    }

    /// <summary>
    /// Closes publisher/message admission and waits for an already admitted
    /// message to finish. Incomplete wire messages are not admitted. Timeout or
    /// caller cancellation ends this wait only; DisposeAsync remains the forced
    /// cancellation path. This is not OBS EOS or a downstream drain guarantee.
    /// </summary>
    public Task<ObsRtmpQuiescenceResult> RequestQuiescenceAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            var completion = _completion ?? throw new InvalidOperationException("RTMP ingest has not started.");
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(new ObsRtmpQuiescenceResult(ObsRtmpQuiescenceOutcome.Cancelled, null));
            if (!completion.IsCompleted)
            {
                _admission.Close();
                _listener?.Stop(); // Does not close the already accepted TCP client.
                _listening = false;
            }
            return WaitForQuiescenceAsync(completion, timeout, cancellationToken);
        }
    }

    private static async Task<ObsRtmpQuiescenceResult> WaitForQuiescenceAsync(
        Task<ObsRtmpIngestResult> completion, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var final = await completion.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            var outcome = final.EndReason switch
            {
                ObsRtmpIngestEndReason.Quiesced => ObsRtmpQuiescenceOutcome.Completed,
                ObsRtmpIngestEndReason.PublisherStopped => ObsRtmpQuiescenceOutcome.AlreadyEnded,
                ObsRtmpIngestEndReason.Cancelled => ObsRtmpQuiescenceOutcome.Cancelled,
                ObsRtmpIngestEndReason.TimedOut => ObsRtmpQuiescenceOutcome.TimedOut,
                _ => ObsRtmpQuiescenceOutcome.Failed,
            };
            return new(outcome, final);
        }
        catch (TimeoutException) { return new(ObsRtmpQuiescenceOutcome.TimedOut, null); }
        catch (OperationCanceledException) { return new(ObsRtmpQuiescenceOutcome.Cancelled, null); }
        catch (Exception) { return new(ObsRtmpQuiescenceOutcome.Failed, null); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _observation?.ForcedStopRequested?.Invoke();
        if (_completion is not null)
        {
            await _completion.ConfigureAwait(false);
        }

        _stop.Dispose();
        _admission.Dispose();
    }

    private async Task<ObsRtmpIngestResult> RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, _admission.ReadStop);
        var publishing = new ObsFlvPublishingSession(_channelId, _sessionId, _consumer, _options, _encodedConsumer);
        Task? reject = null;
        ObsRtmpIngestEndReason reason;
        ObsRtmpRejectionDiagnostic? rejection = null;
        var acceptedClient = false;
        try
        {
            var accept = listener.AcceptTcpClientAsync(reads.Token);
            if (!accept.IsCompleted) _observation?.AcceptWaiting?.Invoke();
            using var client = await accept.ConfigureAwait(false);
            acceptedClient = true;
            if (!_admission.TryAdmit()) throw new OperationCanceledException(reads.Token);
            lock (_gate) { _publisherConnected = true; }
            client.NoDelay = true;
            reject = RejectAdditionalClientsAsync(listener, reads.Token);
            var stream = client.GetStream();
            var observedStream = _observation?.ObserveStream?.Invoke(stream) ?? stream;
            var protocol = new ObsRtmpProtocolSession(observedStream, _options, publishing, _admission, _observation);
            reason = await protocol.RunAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            reason = ObsRtmpIngestEndReason.Cancelled;
        }
        catch (OperationCanceledException) when (_admission.ReadStop.IsCancellationRequested)
        {
            reason = ObsRtmpIngestEndReason.Quiesced;
        }
        catch (SocketException) when (!acceptedClient && reads.IsCancellationRequested)
        {
            reason = lifetime.IsCancellationRequested ? ObsRtmpIngestEndReason.Cancelled : ObsRtmpIngestEndReason.Quiesced;
        }
        catch (InvalidOperationException) when (!acceptedClient && reads.IsCancellationRequested)
        {
            reason = lifetime.IsCancellationRequested ? ObsRtmpIngestEndReason.Cancelled : ObsRtmpIngestEndReason.Quiesced;
        }
        catch (TimeoutException)
        {
            reason = ObsRtmpIngestEndReason.TimedOut;
        }
        catch (EndOfStreamException)
        {
            reason = ObsRtmpIngestEndReason.Disconnected;
        }
        catch (InvalidDataException exception)
        {
            reason = ObsRtmpIngestEndReason.ProtocolRejected;
            rejection = ObsRtmpRejection.Get(exception);
        }
        catch (ObsRtmpConsumerException)
        {
            reason = ObsRtmpIngestEndReason.ConsumerRejected;
        }
        catch (IOException)
        {
            reason = ObsRtmpIngestEndReason.TransportFailed;
        }
        catch (SocketException)
        {
            reason = ObsRtmpIngestEndReason.TransportFailed;
        }
        catch (Exception)
        {
            reason = ObsRtmpIngestEndReason.InternalFailure;
        }
        finally
        {
            lock (_gate)
            {
                _listening = false;
                _publisherConnected = false;
            }
            await lifetime.CancelAsync().ConfigureAwait(false);
            listener.Stop();
            if (reject is not null)
            {
                await reject.ConfigureAwait(false);
            }
        }

        return new ObsRtmpIngestResult(reason, publishing.PacketCount, publishing.ByteCount) { Rejection = rejection };
    }

    private static async Task RejectAdditionalClientsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                using var extra = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException)
        {
        }
        catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
