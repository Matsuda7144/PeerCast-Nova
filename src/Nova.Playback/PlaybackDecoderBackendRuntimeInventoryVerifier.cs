namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderBackendRuntimeInventoryVerification
{
    Verified,
    MissingFile,
    UnexpectedFile,
    SizeMismatch,
    DigestMismatch,
}

public static class PlaybackDecoderBackendRuntimeInventoryVerifier
{
    public static async ValueTask<PlaybackDecoderBackendRuntimeInventoryVerification> VerifyAsync(
        PlaybackDecoderBackendRuntimeInventory inventory,
        IReadOnlyDictionary<string, Stream> runtimeFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(runtimeFiles);

        var normalizedFiles = new Dictionary<string, Stream>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var runtimeFile in runtimeFiles)
        {
            if (!normalizedFiles.TryAdd(runtimeFile.Key, runtimeFile.Value))
            {
                return PlaybackDecoderBackendRuntimeInventoryVerification.UnexpectedFile;
            }
        }

        if (normalizedFiles.Count < inventory.Files.Count)
        {
            return PlaybackDecoderBackendRuntimeInventoryVerification.MissingFile;
        }

        if (normalizedFiles.Count > inventory.Files.Count)
        {
            return PlaybackDecoderBackendRuntimeInventoryVerification.UnexpectedFile;
        }

        foreach (var expected in inventory.Files)
        {
            if (!normalizedFiles.TryGetValue(expected.Name, out var stream))
            {
                return PlaybackDecoderBackendRuntimeInventoryVerification.MissingFile;
            }

            ArgumentNullException.ThrowIfNull(stream);
            if (stream.CanSeek && stream.Length - stream.Position != expected.Length)
            {
                return PlaybackDecoderBackendRuntimeInventoryVerification.SizeMismatch;
            }

            var manifest = new PlaybackDecoderBackendArtifactManifest(
                "runtime.file",
                "1",
                "SHA256",
                expected.Sha256);
            var result = await PlaybackDecoderBackendArtifactVerifier.VerifyAsync(
                stream,
                manifest,
                expected.Length,
                cancellationToken).ConfigureAwait(false);
            if (result != PlaybackDecoderBackendArtifactVerification.Verified)
            {
                return result == PlaybackDecoderBackendArtifactVerification.SizeLimitExceeded
                    ? PlaybackDecoderBackendRuntimeInventoryVerification.SizeMismatch
                    : PlaybackDecoderBackendRuntimeInventoryVerification.DigestMismatch;
            }
        }

        return PlaybackDecoderBackendRuntimeInventoryVerification.Verified;
    }
}
