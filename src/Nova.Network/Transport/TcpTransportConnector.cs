using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Transport;

/// <summary>
/// Creates outbound TCP connections with explicit cancellation and timeout behavior.
/// </summary>
public sealed class TcpTransportConnector
{
    private readonly TcpTransportOptions _options;

    public TcpTransportConnector(TcpTransportOptions? options = null)
    {
        _options = options is null ? new TcpTransportOptions() : options with { };
        _options.Validate();
    }

    public async ValueTask<TcpTransportConnection> ConnectAsync(
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        cancellationToken.ThrowIfCancellationRequested();

        var client = new TcpClient(remoteEndPoint.AddressFamily);
        TcpSocketConfigurator.Apply(client, _options);

        try
        {
            using var timeoutSource = new CancellationTokenSource(_options.ConnectTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

            try
            {
                await client.ConnectAsync(
                    remoteEndPoint,
                    linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (timeoutSource.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Connecting to {remoteEndPoint} exceeded {_options.ConnectTimeout}.",
                    exception);
            }

            return new TcpTransportConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async ValueTask<TcpTransportConnection> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "The TCP port is outside the valid range.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var client = new TcpClient();
        TcpSocketConfigurator.Apply(client, _options);

        try
        {
            using var timeoutSource = new CancellationTokenSource(_options.ConnectTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

            try
            {
                await client.ConnectAsync(
                    host,
                    port,
                    linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (timeoutSource.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Connecting to {host}:{port} exceeded {_options.ConnectTimeout}.",
                    exception);
            }

            return new TcpTransportConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
