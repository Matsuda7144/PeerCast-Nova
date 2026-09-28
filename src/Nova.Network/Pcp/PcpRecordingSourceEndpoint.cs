using System.Net;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Resolved upstream PCP source for one recording request.
/// </summary>
public sealed record PcpRecordingSourceEndpoint
{
    public PcpRecordingSourceEndpoint(
        string host,
        int port,
        long initialStreamPosition = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        ArgumentOutOfRangeException.ThrowIfLessThan(
            port,
            1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            port,
            IPEndPoint.MaxPort);

        ArgumentOutOfRangeException.ThrowIfNegative(
            initialStreamPosition);

        Host = host;
        Port = port;
        InitialStreamPosition = initialStreamPosition;
    }

    public string Host { get; }

    public int Port { get; }

    public long InitialStreamPosition { get; }
}
