namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Receives framing events synchronously while PipeReader-owned memory is valid.
/// Implementations must copy leaf payloads that need to outlive the callback.
/// </summary>
public interface IPcpAtomSink
{
    void OnParentStarted(PcpParentStarted atom);

    void OnLeafAtom(PcpLeafAtom atom);

    void OnParentEnded(PcpParentEnded atom);
}
