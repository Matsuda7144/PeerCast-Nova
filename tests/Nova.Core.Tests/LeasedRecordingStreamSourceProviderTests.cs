using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class LeasedRecordingStreamSourceProviderTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public async Task OpenRead_DisposesLeaseWithoutDisposingSharedReader()
    {
        var sharedReader = new MemoryStream([0x10, 0x20, 0x30]);
        var lease = new TrackingLease(sharedReader);
        var provider = new LeasedRecordingStreamSourceProvider(
            new DelegateLeaseFactory((_, _) =>
                ValueTask.FromResult<IRecordingStreamSourceLease?>(lease)));

        var recordingReader = await provider.OpenReadAsync(
            CreateRequest(),
            CancellationToken.None);

        await recordingReader.DisposeAsync();

        Assert.Equal(1, lease.DisposeCount);
        Assert.True(sharedReader.CanRead);
        Assert.Equal(0, sharedReader.Position);
        Assert.Equal(0x10, sharedReader.ReadByte());
        sharedReader.Dispose();
    }

    [Fact]
    public async Task OpenRead_MissingChannelSourceFailsClosed()
    {
        var provider = new LeasedRecordingStreamSourceProvider(
            new RecordingStreamSourceLeaseRegistry());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.OpenReadAsync(
                CreateRequest(),
                CancellationToken.None));

        Assert.Contains(
            "No recording stream source",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRead_PreCancelledDoesNotAcquireLease()
    {
        var factory = new CountingLeaseFactory();
        var provider = new LeasedRecordingStreamSourceProvider(factory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await provider.OpenReadAsync(
                CreateRequest(),
                cancellation.Token));

        Assert.Equal(0, factory.AcquireCount);
    }

    [Fact]
    public async Task OpenRead_CancelledAfterAcquisitionReleasesLease()
    {
        var lease = new TrackingLease(new MemoryStream([0x01]));
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new DelegateLeaseFactory(
            async (_, _) =>
            {
                await release.Task;
                return lease;
            });
        var provider = new LeasedRecordingStreamSourceProvider(factory);
        using var cancellation = new CancellationTokenSource();
        var openTask = provider.OpenReadAsync(
            CreateRequest(),
            cancellation.Token).AsTask();

        cancellation.Cancel();
        release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await openTask);

        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task OpenRead_UnreadableSourceReleasesLease()
    {
        var lease = new TrackingLease(new WriteOnlyStream());
        var provider = new LeasedRecordingStreamSourceProvider(
            new DelegateLeaseFactory((_, _) =>
                ValueTask.FromResult<IRecordingStreamSourceLease?>(lease)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.OpenReadAsync(
                CreateRequest(),
                CancellationToken.None));

        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task OpenRead_EofAndSourceFaultPropagateThroughAdapter()
    {
        var eofProvider = new LeasedRecordingStreamSourceProvider(
            new DelegateLeaseFactory((_, _) =>
                ValueTask.FromResult<IRecordingStreamSourceLease?>(
                    new TrackingLease(new MemoryStream([0x41])))));
        await using (var eofReader = await eofProvider.OpenReadAsync(
                         CreateRequest(),
                         CancellationToken.None))
        {
            Assert.Equal(0x41, eofReader.ReadByte());
            Assert.Equal(-1, eofReader.ReadByte());
        }

        var faultProvider = new LeasedRecordingStreamSourceProvider(
            new DelegateLeaseFactory((_, _) =>
                ValueTask.FromResult<IRecordingStreamSourceLease?>(
                    new TrackingLease(new FaultingStream()))));
        await using var faultReader = await faultProvider.OpenReadAsync(
            CreateRequest(),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await faultReader.ReadExactlyAsync(new byte[1]));
    }

    [Fact]
    public async Task Registry_ForwardsIndependentLeasesAndRemovalStopsNewAcquisition()
    {
        var registry = new RecordingStreamSourceLeaseRegistry(
            maximumFactoryCount: 1);
        var firstFactory = new CountingLeaseFactory();
        var replacementFactory = new CountingLeaseFactory();

        registry.Upsert(ChannelId, firstFactory);
        var firstLease = await registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(firstLease);
        await using (firstLease)
        {
            registry.Upsert(ChannelId, replacementFactory);
        }

        var replacementLease = await registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(replacementLease);
        await replacementLease.DisposeAsync();

        Assert.Equal(1, firstFactory.AcquireCount);
        Assert.Equal(1, replacementFactory.AcquireCount);
        Assert.True(registry.Remove(ChannelId));
        Assert.False(registry.Remove(ChannelId));
        Assert.Null(await registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Registry_RemoveWinsAgainstPendingAcquireAndDisposesLateLease()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new TrackingLease(new MemoryStream());
        var factory = new DelegateLeaseFactory(
            async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task;
                return lease;
            });
        var registry = new RecordingStreamSourceLeaseRegistry();
        var registration = registry.Register(ChannelId, factory);

        var acquireTask = registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None).AsTask();
        await entered.Task;

        registration.Dispose();
        release.TrySetResult();

        Assert.Null(await acquireTask);
        Assert.Equal(1, lease.DisposeCount);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Registry_OldRegistrationCleanupCannotRemoveReplacement()
    {
        var oldEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLease = new TrackingLease(new MemoryStream());
        var oldFactory = new DelegateLeaseFactory(
            async (_, _) =>
            {
                oldEntered.TrySetResult();
                await releaseOld.Task;
                return oldLease;
            });
        var newFactory = new CountingLeaseFactory();
        var registry = new RecordingStreamSourceLeaseRegistry();
        var oldRegistration = registry.Register(ChannelId, oldFactory);

        var oldAcquireTask = registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None).AsTask();
        await oldEntered.Task;

        using var newRegistration = registry.Register(ChannelId, newFactory);
        oldRegistration.Dispose();
        releaseOld.TrySetResult();

        Assert.Null(await oldAcquireTask);
        Assert.Equal(1, oldLease.DisposeCount);

        var newLease = await registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(newLease);
        await newLease.DisposeAsync();
        Assert.Equal(1, newFactory.AcquireCount);
        Assert.True(registry.TryGet(ChannelId, out var currentFactory));
        Assert.Same(newFactory, currentFactory);
    }

    [Fact]
    public void Registry_EnforcesBoundedFactoryCount()
    {
        var registry = new RecordingStreamSourceLeaseRegistry(
            maximumFactoryCount: 1);
        registry.Upsert(
            ChannelId,
            new CountingLeaseFactory());

        var otherChannel = new ChannelId(
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));

        Assert.Throws<InvalidOperationException>(
            () => registry.Upsert(
                otherChannel,
                new CountingLeaseFactory()));
    }

    private static RecordingSinkStartRequest CreateRequest()
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                ChannelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "flv")),
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));
    }

    private sealed class DelegateLeaseFactory :
        IRecordingStreamSourceLeaseFactory
    {
        private readonly Func<
            RecordingSinkStartRequest,
            CancellationToken,
            ValueTask<IRecordingStreamSourceLease?>> factory;

        public DelegateLeaseFactory(
            Func<
                RecordingSinkStartRequest,
                CancellationToken,
                ValueTask<IRecordingStreamSourceLease?>> factory)
        {
            this.factory = factory;
        }

        public ValueTask<IRecordingStreamSourceLease?> AcquireAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            return factory(request, cancellationToken);
        }
    }

    private sealed class CountingLeaseFactory :
        IRecordingStreamSourceLeaseFactory
    {
        private int acquireCount;

        public int AcquireCount => Volatile.Read(ref acquireCount);

        public ValueTask<IRecordingStreamSourceLease?> AcquireAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref acquireCount);
            return ValueTask.FromResult<IRecordingStreamSourceLease?>(
                new TrackingLease(new MemoryStream()));
        }
    }

    private sealed class TrackingLease : IRecordingStreamSourceLease
    {
        private readonly Stream stream;

        public TrackingLease(Stream stream)
        {
            this.stream = stream;
        }

        public int DisposeCount { get; private set; }

        public Stream ReadStream => stream;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    private sealed class FaultingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidDataException("synthetic channel fault");

        public override int Read(Span<byte> buffer) =>
            throw new InvalidDataException("synthetic channel fault");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
