using System.Diagnostics.CodeAnalysis;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Bounded channel-to-owner mapping for recording reader leases.
/// </summary>
/// <remarks>
/// The registry owns no channel stream and never disposes a registered factory.
/// Removal rejects acquisitions that have not completed against the current
/// registration, including late results from an in-flight factory call. It
/// does not cancel a lease that was already returned to a recording session.
/// Registration tokens are owned by the publisher and must be disposed when
/// that channel owner stops; disposing the registry never disposes a factory.
/// </remarks>
public sealed class RecordingStreamSourceLeaseRegistry :
    IRecordingStreamSourceLeaseFactory
{
    public const int DefaultMaximumFactoryCount = 4096;

    private readonly object gate = new();
    private readonly int maximumFactoryCount;
    private readonly Dictionary<ChannelId, RegistrationEntry> factories = [];

    public RecordingStreamSourceLeaseRegistry(
        int maximumFactoryCount = DefaultMaximumFactoryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumFactoryCount);

        this.maximumFactoryCount = maximumFactoryCount;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return factories.Count;
            }
        }
    }

    public void Upsert(
        ChannelId channelId,
        IRecordingStreamSourceLeaseFactory factory)
    {
        _ = Register(channelId, factory);
    }

    /// <summary>
    /// Publishes a factory and returns a token that removes only this exact
    /// registration. Replacing the same channel does not let an older
    /// owner's delayed cleanup remove the replacement.
    /// </summary>
    public IDisposable Register(
        ChannelId channelId,
        IRecordingStreamSourceLeaseFactory factory)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(factory);

        var entry = new RegistrationEntry(factory);
        var registration = new Registration(this, channelId, entry);

        lock (gate)
        {
            if (!factories.ContainsKey(channelId) &&
                factories.Count >= maximumFactoryCount)
            {
                throw new InvalidOperationException(
                    "The recording stream source factory limit has been reached.");
            }

            factories[channelId] = entry;
        }

        return registration;
    }

    public bool Remove(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        lock (gate)
        {
            return factories.Remove(channelId);
        }
    }

    /// <summary>
    /// Reserves an unused bounded channel slot without replacing another owner.
    /// The returned token removes only this registration, including after reuse.
    /// </summary>
    public bool TryRegister(
        ChannelId channelId,
        IRecordingStreamSourceLeaseFactory factory,
        [NotNullWhen(true)] out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(factory);
        lock (gate)
        {
            if (factories.ContainsKey(channelId) || factories.Count >= maximumFactoryCount)
            {
                registration = null;
                return false;
            }

            var entry = new RegistrationEntry(factory);
            factories.Add(channelId, entry);
            registration = new Registration(this, channelId, entry);
            return true;
        }
    }

    private bool Remove(
        ChannelId channelId,
        RegistrationEntry expectedEntry)
    {
        lock (gate)
        {
            if (!factories.TryGetValue(channelId, out var currentEntry) ||
                !ReferenceEquals(currentEntry, expectedEntry))
            {
                return false;
            }

            return factories.Remove(channelId);
        }
    }

    public bool TryGet(
        ChannelId channelId,
        out IRecordingStreamSourceLeaseFactory? factory)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        lock (gate)
        {
            if (factories.TryGetValue(channelId, out var entry))
            {
                factory = entry.Factory;
                return true;
            }

            factory = null;
            return false;
        }
    }

    public async ValueTask<IRecordingStreamSourceLease?> AcquireAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RegistrationEntry? entry;

        lock (gate)
        {
            _ = factories.TryGetValue(
                request.Candidate.ChannelId,
                out entry);
        }

        if (entry is null)
        {
            return null;
        }

        var lease = await entry.Factory.AcquireAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            return null;
        }

        lock (gate)
        {
            if (factories.TryGetValue(
                    request.Candidate.ChannelId,
                    out var currentEntry) &&
                ReferenceEquals(currentEntry, entry))
            {
                return lease;
            }
        }

        await lease.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private sealed record RegistrationEntry(
        IRecordingStreamSourceLeaseFactory Factory);

    private sealed class Registration : IDisposable
    {
        private RecordingStreamSourceLeaseRegistry? owner;
        private readonly ChannelId channelId;
        private readonly RegistrationEntry entry;

        public Registration(
            RecordingStreamSourceLeaseRegistry owner,
            ChannelId channelId,
            RegistrationEntry entry)
        {
            this.owner = owner;
            this.channelId = channelId;
            this.entry = entry;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Remove(
                channelId,
                entry);
        }
    }
}
