namespace PeerCast.Nova.Playback;

/// <summary>
/// Deterministic host-owned backend selection. Backend construction is delayed
/// until the runner has recognized a container.
/// </summary>
public sealed class PlaybackDecoderBackendFactory : IPlaybackDecoderFactory
{
    private readonly IPlaybackDecoderBackend[] _backends;

    public PlaybackDecoderBackendFactory(
        IEnumerable<IPlaybackDecoderBackend> backends,
        PlaybackDecoderBackendPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(backends);
        _backends = backends
            .OrderByDescending(static backend => backend.Descriptor.Priority)
            .ThenBy(static backend => backend.Descriptor.Id, StringComparer.Ordinal)
            .ToArray();

        policy ??= new PlaybackDecoderBackendPolicy();
        Audit = Array.AsReadOnly(_backends
            .Select(backend => new PlaybackDecoderBackendAuditEntry(
                backend.Descriptor.Id,
                policy.Evaluate(backend.Descriptor.Provenance)))
            .ToArray());
        var rejected = Audit.FirstOrDefault(static entry => !entry.IsAllowed);
        if (!rejected.IsAllowed)
        {
            throw new PlaybackDecoderBackendException(
                PlaybackDecoderBackendFailure.ProvenanceRejected,
                rejected.BackendId);
        }

        var duplicate = _backends
            .GroupBy(static backend => backend.Descriptor.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new PlaybackDecoderBackendException(
                PlaybackDecoderBackendFailure.InvalidDescriptor,
                duplicate.Key);
        }
    }

    public IReadOnlyList<PlaybackDecoderBackendDescriptor> Backends =>
        _backends.Select(static backend => backend.Descriptor).ToArray();

    public IReadOnlyList<PlaybackDecoderBackendAuditEntry> Audit { get; }

    public static IReadOnlyList<PlaybackDecoderBackendAuditEntry> AuditBackends(
        IEnumerable<IPlaybackDecoderBackend> backends,
        PlaybackDecoderBackendPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(backends);
        policy ??= new PlaybackDecoderBackendPolicy();
        return Array.AsReadOnly(backends
            .Select(backend => new PlaybackDecoderBackendAuditEntry(
                backend.Descriptor.Id,
                policy.Evaluate(backend.Descriptor.Provenance)))
            .OrderBy(static entry => entry.BackendId, StringComparer.Ordinal)
            .ToArray());
    }

    public IPlaybackDecoder? Create(PlaybackDecoderCreationContext context)
    {
        var backend = _backends.FirstOrDefault(candidate =>
            candidate.Descriptor.Supports(context.Container));
        if (backend is null)
        {
            return null;
        }

        try
        {
            return backend.Create(context) ??
                throw new InvalidOperationException("Backend returned no decoder.");
        }
        catch (PlaybackDecoderBackendException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PlaybackDecoderBackendException(
                PlaybackDecoderBackendFailure.InitializationFailed,
                backend.Descriptor.Id);
        }
    }
}
