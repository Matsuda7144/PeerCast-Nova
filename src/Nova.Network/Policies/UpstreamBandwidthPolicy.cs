using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Policies;

public sealed record UpstreamBandwidthLimits(int? Ipv4Kbps = null, int? Ipv6Kbps = null);

/// <summary>
/// Complete owner-supplied downstream relay accounting for each family, in
/// decimal kbps. Null means unavailable or incomplete, including unknown rates.
/// This type does not certify measurements or reserve capacity.
/// </summary>
public sealed record BandwidthAccountingSnapshot(long? Ipv4Kbps, long? Ipv6Kbps);

public enum BandwidthAdmissionDecision
{
    Allowed,
    LimitReached,
    AccountingUnavailable,
    BitrateUnavailable,
}

/// <summary>
/// Numerical admission model only; no rate measurement, capacity reservation,
/// connection rejection, or traffic shaping. Indeterminate decisions require
/// accounting integration before they can be used by a runtime owner.
/// </summary>
public sealed class UpstreamBandwidthPolicy
{
    private UpstreamBandwidthLimits _limits;

    public UpstreamBandwidthPolicy(UpstreamBandwidthLimits? limits = null)
    {
        var normalized = Validate(limits);
        Volatile.Write(ref _limits, normalized);
    }

    public UpstreamBandwidthLimits Limits => Volatile.Read(ref _limits);

    public void UpdateLimits(UpstreamBandwidthLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var normalized = Validate(limits);
        Volatile.Write(ref _limits, normalized);
    }

    private static UpstreamBandwidthLimits Validate(UpstreamBandwidthLimits? limits)
    {
        var normalized = limits is null ? new UpstreamBandwidthLimits() : limits with { };
        if (normalized.Ipv4Kbps < 0 || normalized.Ipv6Kbps < 0)
            throw new ArgumentOutOfRangeException(nameof(limits),
                "Upstream bandwidth limits cannot be negative.");
        return normalized;
    }

    public static AddressFamily ClassifyAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? AddressFamily.InterNetwork : address.AddressFamily;
    }

    public long? GetLimitBytesPerSecond(IPAddress address)
    {
        // Decimal kbps: 1000 bits/s / 8 = 125 bytes/s. Widen before multiplication.
        var limit = GetLimitKbps(address);
        return limit is { } kbps ? kbps * 125L : null;
    }

    public BandwidthAdmissionDecision Evaluate(IPAddress remoteAddress,
        BandwidthAccountingSnapshot accounting, int? candidateBitrateKbps)
    {
        ArgumentNullException.ThrowIfNull(accounting);
        if (accounting.Ipv4Kbps < 0 || accounting.Ipv6Kbps < 0)
            throw new ArgumentOutOfRangeException(nameof(accounting));
        if (candidateBitrateKbps < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateBitrateKbps));

        var limit = GetLimitKbps(remoteAddress);
        if (limit is null) return BandwidthAdmissionDecision.Allowed;
        if (limit == 0) return BandwidthAdmissionDecision.LimitReached;
        var current = ClassifyAddress(remoteAddress) == AddressFamily.InterNetwork
            ? accounting.Ipv4Kbps : accounting.Ipv6Kbps;
        if (current is null) return BandwidthAdmissionDecision.AccountingUnavailable;
        // Absent or zero CHAN bitrate is unknown, never a free reservation.
        if (candidateBitrateKbps is null or 0) return BandwidthAdmissionDecision.BitrateUnavailable;
        return current > limit || candidateBitrateKbps > limit - current
            ? BandwidthAdmissionDecision.LimitReached : BandwidthAdmissionDecision.Allowed;
    }

    private int? GetLimitKbps(IPAddress address) => ClassifyAddress(address) switch
    {
        AddressFamily.InterNetwork => Limits.Ipv4Kbps,
        AddressFamily.InterNetworkV6 => Limits.Ipv6Kbps,
        _ => throw new ArgumentException("An IPv4 or IPv6 address is required.", nameof(address)),
    };
}
