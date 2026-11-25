using System.Net.NetworkInformation;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class SystemNatNetworkSourceTests
{
    [Fact]
    public void SelectPrimaryCandidate_IgnoresVirtualAdaptersAndSelectsPhysicalLan()
    {
        var physical = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("192.168.1.1", "eth0"),
            "192.168.1.25",
            NetworkInterfaceType.Ethernet,
            "Ethernet",
            "Intel(R) Ethernet Connection");
        var wsl = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("172.24.0.1", "wsl0"),
            "172.24.0.2",
            NetworkInterfaceType.Ethernet,
            "vEthernet (WSL)",
            "Hyper-V Virtual Ethernet Adapter");
        var vpn = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("10.0.0.1", "wg0"),
            "10.0.0.2",
            NetworkInterfaceType.Ethernet,
            "WireGuard Tunnel",
            "WireGuard Tunnel");

        var selected = SystemNatNetworkSource.SelectPrimaryCandidate(
            [wsl, physical, vpn],
            preferredLocalAddress: null);

        Assert.Equal(physical, selected);
    }

    [Fact]
    public void SelectPrimaryCandidate_UsesDefaultRouteLocalAddressWhenMultiplePhysicalAdaptersExist()
    {
        var wired = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("192.168.1.1", "eth0"),
            "192.168.1.10",
            NetworkInterfaceType.Ethernet,
            "Ethernet",
            "Realtek PCIe GbE Family Controller");
        var wifi = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("192.168.1.1", "wifi0"),
            "192.168.1.20",
            NetworkInterfaceType.Wireless80211,
            "Wi-Fi",
            "Intel(R) Wi-Fi 6E AX211");

        var selected = SystemNatNetworkSource.SelectPrimaryCandidate(
            [wired, wifi],
            preferredLocalAddress: "192.168.1.10");

        Assert.Equal(wired, selected);
    }

    [Fact]
    public void SelectPrimaryCandidate_ReturnsNullWhenMultiplePhysicalAdaptersRemainAmbiguous()
    {
        var wired = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("192.168.1.1", "eth0"),
            "192.168.1.10",
            NetworkInterfaceType.Ethernet,
            "Ethernet",
            "Realtek PCIe GbE Family Controller");
        var wifi = new SystemNatNetworkSource.NetworkAdapterCandidate(
            new NatDeviceIdentity("192.168.1.1", "wifi0"),
            "192.168.1.20",
            NetworkInterfaceType.Wireless80211,
            "Wi-Fi",
            "Intel(R) Wi-Fi 6E AX211");

        var selected = SystemNatNetworkSource.SelectPrimaryCandidate(
            [wired, wifi],
            preferredLocalAddress: null);

        Assert.Null(selected);
    }
}
