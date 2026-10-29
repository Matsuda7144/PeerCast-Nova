using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Publishing;

public enum ObsPublishLanFailure { None, InvalidSettings, InterfaceUnavailable, AdmissionUnavailable, BandwidthUnsupported, BindFailed }

public sealed class ObsPublishLanException(ObsPublishLanFailure failure) : InvalidOperationException("LAN publishing is unavailable.")
{
    public ObsPublishLanFailure Failure { get; } = failure;
}

/// <summary>Immutable configuration only. Resolving NICs neither binds nor sends packets.</summary>
public sealed record ObsPublishLanOptions(bool Enabled = false, string InterfaceId = "", string Address = "", int Port = 0)
{
    public void Validate()
    {
        // OFF permits an empty selection, not a corrupt persisted document.
        if (InterfaceId is null || Address is null || InterfaceId.Length > 256 || InterfaceId.Any(char.IsControl) ||
            Port is < 0 or > 65535 || (Address.Length != 0 && !TryPrivateAddress(Address, out _)))
            throw new ObsPublishLanException(ObsPublishLanFailure.InvalidSettings);
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(InterfaceId) || !TryPrivateAddress(Address, out _) || Port == 0)
            throw new ObsPublishLanException(ObsPublishLanFailure.InvalidSettings);
    }

    public ObsPublishLanBinding Resolve(IObsPublishLanInterfaces interfaces)
    {
        Validate();
        if (!Enabled) throw new ObsPublishLanException(ObsPublishLanFailure.InvalidSettings);
        try
        {
            // Ambiguous assignments fail closed even if one NIC happens to match the saved ID.
            var matches = interfaces.Read().Where(item => item.Address == Address).ToArray();
            if (matches.Length != 1 || !matches[0].IsUp || matches[0].Id != InterfaceId)
                throw new ObsPublishLanException(ObsPublishLanFailure.InterfaceUnavailable);
            return new ObsPublishLanBinding(matches[0]);
        }
        catch (ObsPublishLanException) { throw; }
        catch { throw new ObsPublishLanException(ObsPublishLanFailure.InterfaceUnavailable); }
    }

    public static bool TryPrivateAddress(string? text, out IPAddress? address)
    {
        address = null;
        if (!IPAddress.TryParse(text, out var value) || value.AddressFamily != AddressFamily.InterNetwork || value.ToString() != text)
            return false;
        var b = value.GetAddressBytes();
        if (b[0] != 10 && !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && b[1] == 168)) return false;
        address = value;
        return true;
    }
}

public sealed record ObsPublishLanInterface(string Id, string Name, bool IsUp, string Address, string Mask, int PrefixLength);
public interface IObsPublishLanInterfaces { IReadOnlyList<ObsPublishLanInterface> Read(); }

public sealed class SystemObsPublishLanInterfaces : IObsPublishLanInterfaces
{
    public static SystemObsPublishLanInterfaces Instance { get; } = new();
    public IReadOnlyList<ObsPublishLanInterface> Read()
    {
        var result = new List<ObsPublishLanInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var ip in nic.GetIPProperties().UnicastAddresses)
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    result.Add(new(nic.Id, nic.Name, nic.OperationalStatus == OperationalStatus.Up,
                        ip.Address.ToString(), ip.IPv4Mask.ToString(), ip.PrefixLength));
        return result;
    }
}

/// <summary>One Start's interface identity/subnet, never refreshed into a different bind.</summary>
public sealed class ObsPublishLanBinding
{
    private readonly uint _address, _mask, _network, _broadcast;
    public ObsPublishLanBinding(ObsPublishLanInterface nic)
    {
        if (!nic.IsUp || !ObsPublishLanOptions.TryPrivateAddress(nic.Address, out var ip) ||
            nic.PrefixLength is < 1 or > 30 || !IPAddress.TryParse(nic.Mask, out var mask) ||
            mask.AddressFamily != AddressFamily.InterNetwork)
            throw new ObsPublishLanException(ObsPublishLanFailure.InterfaceUnavailable);
        _address = Number(ip!);
        _mask = Number(mask);
        if (_mask != uint.MaxValue << (32 - nic.PrefixLength))
            throw new ObsPublishLanException(ObsPublishLanFailure.InterfaceUnavailable);
        _network = _address & _mask;
        _broadcast = _network | ~_mask;
        if (_address == _network || _address == _broadcast)
            throw new ObsPublishLanException(ObsPublishLanFailure.InterfaceUnavailable);
    }
    public bool Accepts(IPAddress? remote)
    {
        if (remote is null || !ObsPublishLanOptions.TryPrivateAddress(remote.ToString(), out _)) return false;
        var value = Number(remote);
        return (value & _mask) == _network && value != _network && value != _broadcast;
    }
    private static uint Number(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
}

public sealed record ObsPublishLanStatus(bool Enabled, bool Ready, string? Address, int? Port, ObsPublishLanFailure Failure);
