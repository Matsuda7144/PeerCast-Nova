using System.Security.Cryptography;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

public enum PcpControlledFlvFixtureReplayResult
{
    Completed,
    InvalidConfiguration,
    FixtureRejected,
    DigestMismatch,
    ConsumerRejected,
    Cancelled,
    Faulted,
}

public sealed record PcpControlledFlvFixtureReplayOptions
{
    public string FixturePath { get; init; } = string.Empty;

    public string ExpectedSha256 { get; init; } = string.Empty;

    public ChannelId? ChannelId { get; init; }

    public ChannelSessionId? SessionId { get; init; }

    public int MaximumFixtureBytes { get; init; } = 64 * 1024 * 1024;

    public int PacketPayloadBytes { get; init; } = 64 * 1024;
}

public readonly record struct PcpControlledFlvFixtureReplayReport(
    PcpControlledFlvFixtureReplayResult Result,
    string FixtureSha256,
    long PayloadByteCount,
    int PacketCount);

/// <summary>
/// Loads one repository-external controlled FLV fixture into bounded memory,
/// verifies its exact SHA-256 before exposing any bytes, then presents finite
/// callback-scoped PCP packet views. It performs no network access, retry,
/// reconnect, seeking, fallback, or payload retention after completion.
/// </summary>
public static class PcpControlledFlvFixtureReplayBoundary
{
    public static async ValueTask<PcpControlledFlvFixtureReplayReport> ReplayAsync(
        PcpControlledFlvFixtureReplayOptions? options,
        Func<PcpChannelPacket, CancellationToken, ValueTask<bool>>? consumer,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidate(options, consumer, out var normalizedDigest))
        {
            return new(
                PcpControlledFlvFixtureReplayResult.InvalidConfiguration,
                string.Empty,
                0,
                0);
        }

        byte[]? payload = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = await ReadBoundedAsync(
                options!.FixturePath,
                options.MaximumFixtureBytes,
                cancellationToken).ConfigureAwait(false);
            if (payload is null || payload.Length == 0)
            {
                return new(
                    PcpControlledFlvFixtureReplayResult.FixtureRejected,
                    string.Empty,
                    0,
                    0);
            }

            var observedDigest = Convert.ToHexString(SHA256.HashData(payload));
            if (!string.Equals(observedDigest, normalizedDigest, StringComparison.Ordinal))
            {
                return new(
                    PcpControlledFlvFixtureReplayResult.DigestMismatch,
                    observedDigest,
                    payload.LongLength,
                    0);
            }

            var packetCount = 0;
            for (var offset = 0; offset < payload.Length; offset += options.PacketPayloadBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(options.PacketPayloadBytes, payload.Length - offset);
                var wirePosition = checked((uint)offset);
                var packet = new PcpChannelPacket(
                    options.ChannelId!,
                    options.SessionId!,
                    packetCount,
                    type: 1,
                    wirePosition,
                    streamPosition: offset,
                    payload.AsMemory(offset, count),
                    discontinuity: null);
                if (!await consumer!(packet, cancellationToken).ConfigureAwait(false))
                {
                    return new(
                        PcpControlledFlvFixtureReplayResult.ConsumerRejected,
                        observedDigest,
                        payload.LongLength,
                        packetCount);
                }

                packetCount = checked(packetCount + 1);
            }

            return new(
                PcpControlledFlvFixtureReplayResult.Completed,
                observedDigest,
                payload.LongLength,
                packetCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                PcpControlledFlvFixtureReplayResult.Cancelled,
                string.Empty,
                payload?.LongLength ?? 0,
                0);
        }
        catch (IOException)
        {
            return new(
                PcpControlledFlvFixtureReplayResult.FixtureRejected,
                string.Empty,
                payload?.LongLength ?? 0,
                0);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                PcpControlledFlvFixtureReplayResult.FixtureRejected,
                string.Empty,
                payload?.LongLength ?? 0,
                0);
        }
        catch
        {
            return new(
                PcpControlledFlvFixtureReplayResult.Faulted,
                string.Empty,
                payload?.LongLength ?? 0,
                0);
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private static bool TryValidate(
        PcpControlledFlvFixtureReplayOptions? options,
        Func<PcpChannelPacket, CancellationToken, ValueTask<bool>>? consumer,
        out string normalizedDigest)
    {
        normalizedDigest = string.Empty;
        if (options is null || consumer is null || options.ChannelId is null ||
            options.SessionId is null || string.IsNullOrWhiteSpace(options.FixturePath) ||
            options.FixturePath.Contains('\0') || options.MaximumFixtureBytes is < 1 or >
                256 * 1024 * 1024 || options.PacketPayloadBytes is < 1 or > 1024 * 1024 ||
            options.PacketPayloadBytes > options.MaximumFixtureBytes ||
            options.ExpectedSha256.Length != 64)
        {
            return false;
        }

        foreach (var character in options.ExpectedSha256)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        normalizedDigest = options.ExpectedSha256.ToUpperInvariant();
        return true;
    }

    private static async ValueTask<byte[]?> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > int.MaxValue || stream.Length > maximumBytes)
        {
            return null;
        }

        var payload = new byte[checked((int)stream.Length)];
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = await stream.ReadAsync(
                payload.AsMemory(offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                CryptographicOperations.ZeroMemory(payload);
                return null;
            }

            offset = checked(offset + read);
        }

        var trailing = new byte[1];
        if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
        {
            CryptographicOperations.ZeroMemory(payload);
            return null;
        }

        return payload;
    }
}
