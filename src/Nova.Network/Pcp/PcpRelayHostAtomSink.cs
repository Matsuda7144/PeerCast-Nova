using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>Bounded top-level HOST observation on the existing incremental decoder.</summary>
internal sealed class PcpRelayHostAtomSink(Action<PcpRelayHostCandidate> observed) : IPcpAtomSink
{
    private List<PcpAtom>? _fields;
    private bool _invalid;
    private int _bytes;

    public void OnParentStarted(PcpParentStarted atom)
    {
        if (atom.Depth == 0)
        {
            _fields = atom.Header.Name == PcpKnownAtomNames.Host ? [] : null;
            _invalid = atom.Header.Value > PcpRelayHostCandidateParser.MaximumFields;
            _bytes = 8;
        }
        else if (_fields is not null) _invalid = true;
    }

    public void OnLeafAtom(PcpLeafAtom atom)
    {
        if (_fields is null || _invalid || atom.Depth != 1) return;
        if (atom.Payload.Length > PcpRelayHostCandidateParser.MaximumHostBytes ||
            _bytes + 8L + atom.Payload.Length > PcpRelayHostCandidateParser.MaximumHostBytes)
        { _invalid = true; _fields.Clear(); return; }
        _bytes += 8 + (int)atom.Payload.Length;
        var name = atom.Header.Name.ToString();
        if (name is not ("id" or "cid" or "ip" or "port" or "flg1" or "numr" or "upip" or "uppt"))
            return;
        if (_fields.Count >= PcpRelayHostCandidateParser.MaximumFields)
        { _invalid = true; _fields.Clear(); return; }
        // Only small HOST fields are owned, never CHAN or media bytes.
        _fields.Add(PcpAtom.Leaf(atom.Header.Name, atom.CopyPayload()));
    }

    public void OnParentEnded(PcpParentEnded atom)
    {
        if (atom.Depth != 0 || _fields is null) return;
        var fields = _fields;
        _fields = null;
        if (!_invalid && PcpRelayHostCandidateParser.TryParse(
            PcpAtom.Parent(PcpKnownAtomNames.Host, fields)) is { } host)
            observed(host);
    }
}
