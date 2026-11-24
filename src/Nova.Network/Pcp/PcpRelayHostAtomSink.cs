using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>Bounded HOST observation, including receive-only BCST/HOST envelopes.</summary>
internal sealed class PcpRelayHostAtomSink(Action<PcpRelayHostCandidate> observed,
    PcpBroadcastHostObserver? broadcasts = null) : IPcpAtomSink
{
    private List<PcpAtom>? _fields, _envelope;
    private bool _invalid;
    private long _bytes;
    private int _atoms, _hostDepth, _hosts, _ignoredSubtreeDepth;
    private long _hostBytes;

    public void OnParentStarted(PcpParentStarted atom)
    {
        if (atom.Depth == 0)
        {
            _envelope = broadcasts is not null && atom.Header.Name == PcpBroadcastHostReader.Broadcast ? [] : null;
            _fields = atom.Header.Name == PcpKnownAtomNames.Host ? [] : null;
            _hostDepth = 0; _hosts = 0; _atoms = 1; _bytes = 8; _hostBytes = 8; _ignoredSubtreeDepth = 0;
            _invalid = atom.Header.Value > (_envelope is null ? PcpRelayHostCandidateParser.MaximumFields : 48);
        }
        else if (_envelope is not null || _fields is not null)
        {
            Count(0);
            if (_ignoredSubtreeDepth > 0) return;
            if (_envelope is not null && atom.Depth == 1 && atom.Header.Name == PcpKnownAtomNames.Channel)
            {
                _ignoredSubtreeDepth = atom.Depth;
                return;
            }
            if (_envelope is not null && atom.Depth == 1 && atom.Header.Name == PcpKnownAtomNames.Host &&
                atom.Header.Value <= PcpRelayHostCandidateParser.MaximumFields && ++_hosts <= PcpBroadcastHostReader.MaximumHosts)
            { _fields = []; _hostDepth = 1; _hostBytes = 8; }
            else _invalid = true;
        }
    }

    public void OnLeafAtom(PcpLeafAtom atom)
    {
        if (_envelope is null && _fields is null || _invalid) return;
        Count(atom.Payload.Length);
        if (_invalid || _ignoredSubtreeDepth > 0) return;
        var name = atom.Header.Name.ToString();
        if (_fields is not null && atom.Depth == _hostDepth + 1)
        {
            _hostBytes += 8 + atom.Payload.Length;
            if (_hostBytes > PcpRelayHostCandidateParser.MaximumHostBytes) { _invalid = true; return; }
            if (name is "id" or "cid" or "ip" or "port" or "flg1" or "numr" or "numl" or "ver" or "vevp" or "upip" or "uppt")
                _fields.Add(PcpAtom.Leaf(atom.Header.Name, atom.CopyPayload()));
        }
        else if (_envelope is not null && atom.Depth == 1 && name is "cid" or "from" or "dest" or "ttl" or "hops" or "grp")
        {
            if (atom.Payload.Length > 16) { _invalid = true; return; }
            _envelope.Add(PcpAtom.Leaf(atom.Header.Name, atom.CopyPayload()));
        }
    }

    private void Count(long payload)
    {
        _bytes += 8 + payload;
        if (++_atoms > PcpBroadcastHostReader.MaximumAtoms || _bytes >
            (_envelope is null ? PcpRelayHostCandidateParser.MaximumHostBytes : PcpBroadcastHostReader.MaximumBytes))
            _invalid = true;
    }

    public void OnParentEnded(PcpParentEnded atom)
    {
        if (_ignoredSubtreeDepth > 0)
        {
            if (atom.Depth == _ignoredSubtreeDepth) _ignoredSubtreeDepth = 0;
            return;
        }
        if (_fields is not null && atom.Depth == _hostDepth)
        {
            var host = PcpAtom.Parent(PcpKnownAtomNames.Host, _fields);
            _fields = null;
            if (!_invalid)
            {
                if (_envelope is not null) _envelope.Add(host);
                else if (PcpRelayHostCandidateParser.TryParse(host) is { } parsed) observed(parsed);
            }
        }
        if (atom.Depth == 0 && _envelope is not null)
        {
            if (!_invalid) broadcasts!.Read(PcpAtom.Parent(PcpBroadcastHostReader.Broadcast, _envelope));
            _envelope = null;
        }
    }
}
