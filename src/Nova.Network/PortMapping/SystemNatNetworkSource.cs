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

    public NatNetworkIdentity? GetCurrentNetwork()
    {
        var candidates = new List<(NatDeviceIdentity Device, string Address)>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
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
                    candidates.Add((new NatDeviceIdentity(gateways[0].ToString(), adapter.Id), address.ToString()));
                }
            }
        }
        catch (NetworkInformationException)
        {
            candidates.Clear();
            // No guessed gateway; the caller retains old mapping evidence.
        }

        lock (gate)
        {
            (NatDeviceIdentity Device, string Address)? selected = candidates.Count == 1 ? candidates[0] : null;
            if (previous != selected)
            {
                epoch = checked(epoch + 1);
                previous = selected;
            }

            return selected is { } value ? new NatNetworkIdentity(value.Device, value.Address, epoch) : null;
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
