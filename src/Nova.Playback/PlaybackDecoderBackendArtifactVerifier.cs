using System.Buffers;
using System.Security.Cryptography;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderBackendArtifactVerification
{
    Verified,
    DigestMismatch,
    SizeLimitExceeded,
}

public static class PlaybackDecoderBackendArtifactVerifier
{
    public const long DefaultMaximumArtifactBytes = 512L * 1024 * 1024;
    public const long MaximumArtifactBytesLimit = 4L * 1024 * 1024 * 1024;

    public static async ValueTask<PlaybackDecoderBackendArtifactVerification> VerifyAsync(
        Stream artifact,
        PlaybackDecoderBackendArtifactManifest manifest,
        long maximumArtifactBytes = DefaultMaximumArtifactBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!artifact.CanRead)
        {
            throw new ArgumentException("The artifact stream must be readable.", nameof(artifact));
        }

        if (maximumArtifactBytes is <= 0 or > MaximumArtifactBytesLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
        }

        if (artifact.CanSeek && artifact.Length - artifact.Position > maximumArtifactBytes)
        {
            return PlaybackDecoderBackendArtifactVerification.SizeLimitExceeded;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long totalBytes = 0;
        try
        {
            while (true)
            {
                var read = await artifact.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > maximumArtifactBytes)
                {
                    return PlaybackDecoderBackendArtifactVerification.SizeLimitExceeded;
                }

                hash.AppendData(buffer, 0, read);
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(manifest.Sha256))
                ? PlaybackDecoderBackendArtifactVerification.Verified
                : PlaybackDecoderBackendArtifactVerification.DigestMismatch;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
