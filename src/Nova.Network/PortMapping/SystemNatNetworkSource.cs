using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.PortMapping;

public interface INatNetworkSource
{
    NatNetworkIdentity? GetCurrentNetwork();
}

/// <summary>
/// Conservative first-slice route selection: one active private IPv4 address
/// with one default gateway. Ambiguous multi-adapter routes remain unavailable.
/// NetworkChange invalidates the epoch even if DHCP later returns the same tuple.
/// </summary>
public sealed class SystemNatNetworkSource : INatNetworkSource, IDisposable
{
    private readonly object gate = new();
    private long epoch;
    private (NatDeviceIdentity Device, string Address)? previous;
    private int disposed;

    public SystemNatNetworkSource()
    {
        NetworkChange.NetworkAddressChanged += AddressChanged;
        NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
    }

    public event Action? Changed;

    internal readonly record struct NetworkAdapterCandidate(
        NatDeviceIdentity Device,
        string Address,
        NetworkInterfaceType InterfaceType,
        string Name,
        string Description);

    public NatNetworkIdentity? GetCurrentNetwork()
    {
        var candidates = new List<NetworkAdapterCandidate>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = adapter.GetIPProperties();
                var gateways = properties.GatewayAddresses
                    .Select(item => item.Address).Where(IsPrivateIpv4).Distinct().ToArray();
                if (gateways.Length != 1)
                {
                    continue;
                }

                foreach (var address in properties.UnicastAddresses.Select(item => item.Address).Where(IsPrivateIpv4))
                {
                    candidates.Add(new NetworkAdapterCandidate(
                        new NatDeviceIdentity(gateways[0].ToString(), adapter.Id),
                        address.ToString(),
                        adapter.NetworkInterfaceType,
                        adapter.Name,
                        adapter.Description));
                }
            }
        }
        catch (NetworkInformationException)
        {
            candidates.Clear();
            // No guessed gateway; the caller retains old mapping evidence.
        }

        var preferredLocalAddress = candidates.Count > 1 ? ProbeDefaultRouteLocalIpv4() : null;
        var selectedCandidate = SelectPrimaryCandidate(candidates, preferredLocalAddress);

        lock (gate)
        {
            (NatDeviceIdentity Device, string Address)? selected = selectedCandidate is { } candidate
                ? (candidate.Device, candidate.Address)
                : null;
            if (previous != selected)
            {
                epoch = checked(epoch + 1);
                previous = selected;
            }

            return selected is { } value ? new NatNetworkIdentity(value.Device, value.Address, epoch) : null;
        }
    }

    internal static NetworkAdapterCandidate? SelectPrimaryCandidate(
        IReadOnlyList<NetworkAdapterCandidate> candidates,
        string? preferredLocalAddress)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // WSL2 / Hyper-V / Docker / VPN 等の仮想アダプタを除外した物理アダプタを優先する。
        var physical = candidates.Where(static c => !IsLikelyVirtualAdapter(c)).ToArray();
        if (physical.Length == 1)
        {
            return physical[0];
        }

        var pool = physical.Length > 0 ? physical : candidates;
        if (!string.IsNullOrWhiteSpace(preferredLocalAddress))
        {
            var routeMatches = pool
                .Where(c => string.Equals(c.Address, preferredLocalAddress, StringComparison.Ordinal))
                .ToArray();
            if (routeMatches.Length == 1)
            {
                return routeMatches[0];
            }
        }

        return null;
    }

    private static bool IsLikelyVirtualAdapter(NetworkAdapterCandidate candidate)
    {
        if (candidate.InterfaceType is not (NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.GigabitEthernet or
            NetworkInterfaceType.FastEthernetT or
            NetworkInterfaceType.FastEthernetFx or
            NetworkInterfaceType.Wireless80211))
        {
            return true;
        }

        return ContainsVirtualKeyword(candidate.Name) || ContainsVirtualKeyword(candidate.Description);
    }

    private static bool ContainsVirtualKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Loopback", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ProbeDefaultRouteLocalIpv4()
    {
        try
        {
            // UDP Connect はパケットを送信せず OS のルーティングテーブルのみ参照して送信元 IP を返す。
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(IPAddress.Parse("198.51.100.1"), 53);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            NetworkChange.NetworkAddressChanged -= AddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
        }
    }

    private void AddressChanged(object? sender, EventArgs args) => Invalidate();

    private void AvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => Invalidate();

    private void Invalidate()
    {
        Changed?.Invoke();
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
    }
}
