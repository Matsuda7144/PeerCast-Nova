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
    private readonly IPcpChannelPacketConsumer _consumer;
    private readonly CancellationTokenSource _stop = new();
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
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(consumer);
        _options = options is null ? new() : options with { };
        _options.Validate();
        _channelId = channelId;
        _sessionId = sessionId;
        _consumer = consumer;
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
        if (_completion is not null)
        {
            await _completion.ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task<ObsRtmpIngestResult> RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var publishing = new ObsFlvPublishingSession(_channelId, _sessionId, _consumer, _options);
        Task? reject = null;
        ObsRtmpIngestEndReason reason;
        ObsRtmpRejectionDiagnostic? rejection = null;
        try
        {
            using var client = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
            lock (_gate) { _publisherConnected = true; }
            client.NoDelay = true;
            reject = RejectAdditionalClientsAsync(listener, lifetime.Token);
            var protocol = new ObsRtmpProtocolSession(client.GetStream(), _options, publishing);
            reason = await protocol.RunAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            reason = ObsRtmpIngestEndReason.Cancelled;
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
    }
}
