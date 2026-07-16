namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Wraps a packet-consumer failure so the owning session never mistakes it for
/// a retryable network or idle-timeout failure.
/// </summary>
public sealed class PcpChannelConsumerException : Exception
{
    public PcpChannelConsumerException()
        : this("The PCP channel packet consumer failed.")
    {
    }

    public PcpChannelConsumerException(string message)
        : this(message, innerException: null)
    {
    }

    public PcpChannelConsumerException(
        string message,
        Exception? innerException)
        : base(message, innerException)
    {
    }
}
