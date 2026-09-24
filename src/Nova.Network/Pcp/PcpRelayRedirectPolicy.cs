using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Pcp;

internal enum PcpRelayRedirectEligibilityReason
{
    Eligible = 1,
    RedirectBudgetExhausted,
    LocalRoleUnsupported,
    NonIpv4Address,
    SpecialPurposeAddress,
    MulticastAddress,
}

internal readonly record struct PcpRelayRedirectEligibility(
    bool IsEligible,
    PcpRelayRedirectEligibilityReason Reason);

internal static class PcpRelayRedirectPolicy
{
    internal const int MaximumRedirectAttempts = 8;

    internal static PcpRelayRedirectEligibility Evaluate(
        PcpRelayRedirectPlanEntry entry,
        int redirectsAlreadyAttempted)
    {
        if (redirectsAlreadyAttempted < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(redirectsAlreadyAttempted),
                redirectsAlreadyAttempted,
                "The redirect attempt count cannot be negative.");
        }

        if (redirectsAlreadyAttempted >= MaximumRedirectAttempts)
        {
            return Ineligible(
                PcpRelayRedirectEligibilityReason.RedirectBudgetExhausted);
        }

        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Role == PcpRelayRedirectEndpointRole.Local)
        {
            return Ineligible(
                PcpRelayRedirectEligibilityReason.LocalRoleUnsupported);
        }

        var address = entry.Endpoint.Address;
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return Ineligible(
                PcpRelayRedirectEligibilityReason.NonIpv4Address);
        }

        if (!TryGetIpv4Value(address, out var ipv4Value))
        {
            return Ineligible(
                PcpRelayRedirectEligibilityReason.NonIpv4Address);
        }

        if (IsInCidr(ipv4Value, 224, 0, 0, 0, 4))
        {
            return Ineligible(
                PcpRelayRedirectEligibilityReason.MulticastAddress);
        }

        if (IsSpecialPurposeAddress(ipv4Value))
        {
            return Ineligible(
                PcpRelayRedirectEligibilityReason.SpecialPurposeAddress);
        }

        return new(true, PcpRelayRedirectEligibilityReason.Eligible);
    }

    private static PcpRelayRedirectEligibility Ineligible(
        PcpRelayRedirectEligibilityReason reason)
    {
        return new(false, reason);
    }

    private static bool TryGetIpv4Value(
        IPAddress address,
        out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!address.TryWriteBytes(bytes, out var bytesWritten) ||
            bytesWritten != bytes.Length)
        {
            value = 0;
            return false;
        }

        value =
            ((uint)bytes[0] << 24) |
            ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) |
            bytes[3];
        return true;
    }

    private static bool IsSpecialPurposeAddress(uint address)
    {
        return
            IsInCidr(address, 0, 0, 0, 0, 8) ||
            IsInCidr(address, 10, 0, 0, 0, 8) ||
            IsInCidr(address, 100, 64, 0, 0, 10) ||
            IsInCidr(address, 127, 0, 0, 0, 8) ||
            IsInCidr(address, 169, 254, 0, 0, 16) ||
            IsInCidr(address, 172, 16, 0, 0, 12) ||
            IsInCidr(address, 192, 0, 0, 0, 24) ||
            IsInCidr(address, 192, 0, 2, 0, 24) ||
            IsInCidr(address, 192, 31, 196, 0, 24) ||
            IsInCidr(address, 192, 52, 193, 0, 24) ||
            IsInCidr(address, 192, 88, 99, 0, 24) ||
            IsInCidr(address, 192, 168, 0, 0, 16) ||
            IsInCidr(address, 192, 175, 48, 0, 24) ||
            IsInCidr(address, 198, 18, 0, 0, 15) ||
            IsInCidr(address, 198, 51, 100, 0, 24) ||
            IsInCidr(address, 203, 0, 113, 0, 24) ||
            IsInCidr(address, 240, 0, 0, 0, 4);
    }

    private static bool IsInCidr(
        uint address,
        byte network0,
        byte network1,
        byte network2,
        byte network3,
        int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        var network =
            ((uint)network0 << 24) |
            ((uint)network1 << 16) |
            ((uint)network2 << 8) |
            network3;
        return (address & mask) == (network & mask);
    }
}
