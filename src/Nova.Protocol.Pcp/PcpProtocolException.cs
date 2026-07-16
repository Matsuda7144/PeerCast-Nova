namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Represents a malformed or unsupported PCP protocol frame.
/// </summary>
public sealed class PcpProtocolException : Exception
{
    public PcpProtocolException()
        : this("A PCP protocol error occurred.")
    {
    }

    public PcpProtocolException(string message)
        : this(message, innerException: null)
    {
    }

    public PcpProtocolException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Error = PcpParseError.None;
        ByteOffset = -1;
    }

    public PcpProtocolException(PcpParseError error, long byteOffset)
        : base(CreateMessage(error, byteOffset))
    {
        Error = error;
        ByteOffset = byteOffset;
    }

    public PcpParseError Error { get; }

    /// <summary>
    /// Gets the zero-based wire offset associated with the parse failure,
    /// or -1 when the exception was created without framing context.
    /// </summary>
    public long ByteOffset { get; }

    private static string CreateMessage(PcpParseError error, long byteOffset)
    {
        return $"PCP framing failed with '{error}' at byte offset {byteOffset}.";
    }
}
