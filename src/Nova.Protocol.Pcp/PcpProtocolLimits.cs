namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Defensive parser limits. Defaults are deliberately finite and can be tightened
/// after interoperability captures and production measurements are available.
/// </summary>
public sealed record PcpProtocolLimits
{
    public int MaxContentLength { get; init; } = 16 * 1024 * 1024;

    public int MaxChildCount { get; init; } = 4_096;

    public int MaxNestingDepth { get; init; } = 32;

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (MaxContentLength < 0)
        {
            throw new InvalidOperationException($"{nameof(MaxContentLength)} cannot be negative.");
        }

        if (MaxChildCount < 0)
        {
            throw new InvalidOperationException($"{nameof(MaxChildCount)} cannot be negative.");
        }

        if (MaxNestingDepth < 1)
        {
            throw new InvalidOperationException($"{nameof(MaxNestingDepth)} must be at least one.");
        }

        if (HandshakeTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(HandshakeTimeout)} must be positive.");
        }
    }
}
