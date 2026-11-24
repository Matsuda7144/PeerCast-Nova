using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

internal sealed record PcpBroadcastHostObserver(Guid ChannelId, Guid LocalSessionId, Action<PcpRelayHostCandidate> Observed)
{
    internal void Read(PcpAtom atom)
    {
        foreach (var host in PcpBroadcastHostReader.Read(atom, ChannelId, LocalSessionId)) Observed(host);
    }
}

// Receive-only HOST envelopes. No forwarding, routing, TTL decrement, sockets,
// or discovery. Unsupported/malformed envelopes contribute no observations.
internal static class PcpBroadcastHostReader
{
    internal const int MaximumBytes = 16 * 1024, MaximumAtoms = 300, MaximumHosts = 8;
    internal static readonly PcpAtomName Broadcast = PcpAtomName.FromAscii("bcst");

    internal static IReadOnlyList<PcpRelayHostCandidate> Read(PcpAtom atom, Guid channel, Guid local)
    {
        if (!atom.IsParent || atom.Name != Broadcast || atom.Children.Count > 48) return [];
        var metadata = new Dictionary<string, PcpAtom>();
        var hosts = new List<PcpRelayHostCandidate>();
        long bytes = 8; var count = 1;
        foreach (var child in atom.Children)
        {
            count++; bytes += 8L + child.Content.Length;
            if (child.IsParent)
            {
                if (child.Name == PcpKnownAtomNames.Channel)
                {
                    if (!TryCountChildSubtree(child, ref count, ref bytes)) return [];
                    continue;
                }
                if (child.Name != PcpKnownAtomNames.Host || child.Children.Count > PcpRelayHostCandidateParser.MaximumFields || hosts.Count == MaximumHosts) return [];
                foreach (var field in child.Children)
                {
                    if (field.IsParent) return [];
                    count++; bytes += 8L + field.Content.Length;
                }
                if (PcpRelayHostCandidateParser.TryParse(child) is not { } host || host.ChannelId != channel) return [];
                hosts.Add(host);
            }
            else if (child.Name.ToString() is "cid" or "from" or "dest" or "ttl" or "hops" or "grp")
            {
                if (!metadata.TryAdd(child.Name.ToString(), child)) return [];
            }
            if (bytes > MaximumBytes || count > MaximumAtoms) return [];
        }
        Guid? Id(string name) => metadata.TryGetValue(name, out var field) && field.Content.Length == 16
            ? PcpWireValueCodec.DecodeGuid(field.Content.Span) : null;
        byte? Byte(string name) => metadata.TryGetValue(name, out var field) && field.Content.Length == 1 ? field.Content.Span[0] : null;
        if (Id("cid") != channel || Id("from") is not { } from || from == Guid.Empty || from == local ||
            metadata.ContainsKey("dest") && Id("dest") != local || Byte("ttl") is not (> 0 and <= 32) ||
            Byte("hops") is not (<= 31) || Byte("ttl") + Byte("hops") > 32 || Byte("grp") is not (> 0 and <= 7)) return [];
        return hosts.AsReadOnly();
    }

    private static bool TryCountChildSubtree(PcpAtom root, ref int count, ref long bytes)
    {
        var stack = new Stack<PcpAtom>();
        foreach (var child in root.Children) stack.Push(child);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            bytes += 8L + current.Content.Length;
            if (bytes > MaximumBytes || count > MaximumAtoms) return false;
            if (current.IsParent)
            {
                foreach (var nested in current.Children) stack.Push(nested);
            }
        }
        return true;
    }
}
