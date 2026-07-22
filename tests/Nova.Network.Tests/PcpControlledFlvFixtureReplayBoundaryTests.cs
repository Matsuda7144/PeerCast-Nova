using System.Security.Cryptography;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpControlledFlvFixtureReplayBoundaryTests
{
    [Fact]
    public async Task Exact_digest_is_verified_before_finite_packet_replay()
    {
        var payload = Enumerable.Range(0, 20).Select(value => (byte)value).ToArray();
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, payload);
        var observed = new List<byte>();
        try
        {
            var report = await PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
                Options(path, Convert.ToHexString(SHA256.HashData(payload))) with
                {
                    PacketPayloadBytes = 7,
                },
                (packet, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    observed.AddRange(packet.Payload.ToArray());
                    return ValueTask.FromResult(true);
                });

            Assert.Equal(PcpControlledFlvFixtureReplayResult.Completed, report.Result);
            Assert.Equal(3, report.PacketCount);
            Assert.Equal(payload.Length, report.PayloadByteCount);
            Assert.Equal(payload, observed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Digest_mismatch_exposes_no_packet()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var calls = 0;
        try
        {
            var report = await PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
                Options(path, new string('A', 64)),
                (packet, token) =>
                {
                    _ = packet;
                    token.ThrowIfCancellationRequested();
                    calls++;
                    return ValueTask.FromResult(true);
                });

            Assert.Equal(PcpControlledFlvFixtureReplayResult.DigestMismatch, report.Result);
            Assert.Equal(0, calls);
            Assert.Equal(0, report.PacketCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Consumer_rejection_stops_without_retry()
    {
        var payload = new byte[16];
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, payload);
        var calls = 0;
        try
        {
            var report = await PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
                Options(path, Convert.ToHexString(SHA256.HashData(payload))) with
                {
                    PacketPayloadBytes = 4,
                },
                (packet, token) =>
                {
                    _ = packet;
                    token.ThrowIfCancellationRequested();
                    calls++;
                    return ValueTask.FromResult(false);
                });

            Assert.Equal(PcpControlledFlvFixtureReplayResult.ConsumerRejected, report.Result);
            Assert.Equal(1, calls);
            Assert.Equal(0, report.PacketCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Oversized_or_invalid_configuration_fails_closed()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, new byte[8]);
        try
        {
            var oversized = await PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
                Options(path, new string('A', 64)) with
                {
                    MaximumFixtureBytes = 4,
                    PacketPayloadBytes = 4,
                },
                static (packet, token) =>
                {
                    _ = packet;
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(true);
                });
            var invalid = await PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
                Options(path, "invalid"),
                static (packet, token) =>
                {
                    _ = packet;
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(true);
                });

            Assert.Equal(PcpControlledFlvFixtureReplayResult.FixtureRejected, oversized.Result);
            Assert.Equal(PcpControlledFlvFixtureReplayResult.InvalidConfiguration, invalid.Result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PcpControlledFlvFixtureReplayOptions Options(
        string path,
        string digest) => new()
        {
            FixturePath = path,
            ExpectedSha256 = digest,
            ChannelId = new ChannelId(
                Guid.ParseExact("71440000000000000000000000000001", "N")),
            SessionId = new ChannelSessionId(
                Guid.ParseExact("71440000000000000000000000000002", "N")),
        };
}
