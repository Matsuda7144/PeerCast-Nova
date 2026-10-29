using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Publishing;

public enum PublishingRelayMode { Loopback, Lan, Wan }
public enum ObsPublishWanFailure { None, InvalidSettings, InterfaceUnavailable, AdmissionUnavailable, BandwidthUnsupported, BindFailed, NetworkChanged, CleanupIncomplete }
public sealed class ObsPublishWanException(ObsPublishWanFailure failure) : InvalidOperationException("WAN publishing is unavailable.")
{
    public ObsPublishWanFailure Failure { get; } = failure;
}

/// <summary>Local binding and user-declared public endpoint are separate, immutable configuration values.</summary>
public sealed record ObsPublishWanOptions(bool Enabled = false, string InterfaceId = "", string Address = "", int Port = 0,
    string AdvertisedAddress = "", int AdvertisedPort = 0)
{
    public void Validate()
    {
        if (InterfaceId is null || Address is null || AdvertisedAddress is null || InterfaceId.Length > 256 ||
            InterfaceId.Any(char.IsControl) || Port is < 0 or > 65535 || AdvertisedPort is < 0 or > 65535 ||
            (Address.Length != 0 && !PublishingIpv4.IsBindAddress(Address)) ||
            (AdvertisedAddress.Length == 0 ? AdvertisedPort != 0 : !PublishingIpv4.IsPublicAddress(AdvertisedAddress) || AdvertisedPort == 0))
            throw new ObsPublishWanException(ObsPublishWanFailure.InvalidSettings);
        if (Enabled && (string.IsNullOrWhiteSpace(InterfaceId) || !PublishingIpv4.IsBindAddress(Address) || Port == 0))
            throw new ObsPublishWanException(ObsPublishWanFailure.InvalidSettings);
    }
    public ObsPublishWanBinding Resolve(IObsPublishLanInterfaces interfaces)
    {
        Validate();
        if (!Enabled) throw new ObsPublishWanException(ObsPublishWanFailure.InvalidSettings);
        try
        {
            var matches = interfaces.Read().Where(n => n.Address == Address).ToArray();
            if (matches.Length != 1 || !matches[0].IsUp || matches[0].Id != InterfaceId)
                throw new ObsPublishWanException(ObsPublishWanFailure.InterfaceUnavailable);
            return new(matches[0]);
        }
        catch (ObsPublishWanException) { throw; }
        catch { throw new ObsPublishWanException(ObsPublishWanFailure.InterfaceUnavailable); }
    }
}

public sealed class ObsPublishWanBinding
{
    private readonly ObsPublishLanInterface _nic;
    internal ObsPublishWanBinding(ObsPublishLanInterface nic)
    {
        if (!nic.IsUp || !PublishingIpv4.IsBindAddress(nic.Address)) throw new ObsPublishWanException(ObsPublishWanFailure.InterfaceUnavailable);
        if (nic.PrefixLength is < 1 or > 32 || !IPAddress.TryParse(nic.Mask, out var mask) || mask.AddressFamily != AddressFamily.InterNetwork)
            throw new ObsPublishWanException(ObsPublishWanFailure.InterfaceUnavailable);
        var bits = BinaryPrimitives.ReadUInt32BigEndian(mask.GetAddressBytes());
        var value = BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(nic.Address).GetAddressBytes());
        if (bits != uint.MaxValue << (32 - nic.PrefixLength) || nic.PrefixLength <= 30 &&
            (value == (value & bits) || value == ((value & bits) | ~bits)))
            throw new ObsPublishWanException(ObsPublishWanFailure.InterfaceUnavailable);
        _nic = nic;
    }
    public bool StillMatches(IObsPublishLanInterfaces interfaces)
    {
        try
        {
            var matches = interfaces.Read().Where(n => n.Address == _nic.Address).ToArray();
            return matches.Length == 1 && matches[0].IsUp && matches[0].Id == _nic.Id &&
                matches[0].Mask == _nic.Mask && matches[0].PrefixLength == _nic.PrefixLength;
        }
        catch { return false; }
    }
}

/// <summary>
/// Conservative IPv4 publication policy based on IANA's special-purpose registry
/// (2025-10-09). Special-purpose ranges, including global anycast services, are
/// not ordinary user broadcast endpoints. No online registry lookup is performed.
/// </summary>
public static class PublishingIpv4
{
    public static bool IsBindAddress(string? text) => ObsPublishLanOptions.TryPrivateAddress(text, out _) || IsPublicAddress(text);
    public static bool IsSourceAddress(IPAddress? address) => address is not null && IsBindAddress(address.ToString());
    public static bool IsPublicAddress(string? text)
    {
        if (!IPAddress.TryParse(text, out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != text ||
            ObsPublishLanOptions.TryPrivateAddress(text, out _)) return false;
        var b = address.GetAddressBytes();
        if (b[0] is 0 or 127 or >= 224 || b[0] == 100 && b[1] is >= 64 and <= 127 ||
            b[0] == 169 && b[1] == 254 || b[0] == 198 && b[1] is 18 or 19) return false;
        return !(b[0] == 192 && (b[1] == 0 && b[2] is 0 or 2 || b[1] == 31 && b[2] == 196 ||
            b[1] == 52 && b[2] == 193 || b[1] == 88 && b[2] == 99 || b[1] == 175 && b[2] == 48) ||
            b[0] == 198 && b[1] == 51 && b[2] == 100 || b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
}

public sealed record ObsPublishWanStatus(bool Enabled, bool ListenerReady, string? BindAddress, int? BindPort,
    string? AdvertisedAddress, int? AdvertisedPort, ObsPublishWanFailure Failure, bool CleanupIncomplete)
{
    public bool ReachabilityVerified { get; }
    public IPEndPoint? TrackerObservedEndpoint { get; }
    public IPEndPoint? VerifiedEndpoint { get; }
}
