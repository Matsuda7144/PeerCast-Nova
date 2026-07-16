namespace PeerCast.Nova.Protocol.Pcp.Tests;

internal sealed class CollectingPcpAtomSink : IPcpAtomSink
{
    public List<string> Events { get; } = [];

    public void OnParentStarted(PcpParentStarted atom)
    {
        Events.Add(
            $"start:{atom.Header.Name}:depth={atom.Depth}:children={atom.Header.Value}");
    }

    public void OnLeafAtom(PcpLeafAtom atom)
    {
        Events.Add(
            $"leaf:{atom.Header.Name}:depth={atom.Depth}:payload={Convert.ToHexString(atom.CopyPayload())}");
    }

    public void OnParentEnded(PcpParentEnded atom)
    {
        Events.Add(
            $"end:{atom.Name}:depth={atom.Depth}");
    }
}
