using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class PublishRecordingRegistryTests
{
    [Fact]
    public void Reservation_rejects_duplicate_without_replacing_the_existing_factory()
    {
        var registry = new RecordingStreamSourceLeaseRegistry(1);
        var id = new ChannelId(Guid.NewGuid());
        var first = new EmptyFactory();
        Assert.True(registry.TryRegister(id, first, out var token));
        using var registration = token;
        Assert.False(registry.TryRegister(id, new EmptyFactory(), out var duplicate));
        Assert.Null(duplicate);
        Assert.True(registry.TryGet(id, out var current));
        Assert.Same(first, current);
    }

    [Fact]
    public void Capacity_is_bounded_and_old_reservation_disposal_cannot_remove_new_generation()
    {
        var registry = new RecordingStreamSourceLeaseRegistry(1);
        var id = new ChannelId(Guid.NewGuid());
        Assert.True(registry.TryRegister(id, new EmptyFactory(), out var first));
        Assert.False(registry.TryRegister(new ChannelId(Guid.NewGuid()), new EmptyFactory(), out _));
        first!.Dispose();
        Assert.True(registry.TryRegister(id, new EmptyFactory(), out var second));
        using var registration = second;
        first.Dispose();
        Assert.Equal(1, registry.Count);
        second!.Dispose();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Invalid_reservation_arguments_do_not_mutate_registry()
    {
        var registry = new RecordingStreamSourceLeaseRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.TryRegister(null!, new EmptyFactory(), out _));
        Assert.Throws<ArgumentNullException>(() => registry.TryRegister(new ChannelId(Guid.NewGuid()), null!, out _));
        Assert.Equal(0, registry.Count);
    }

    private sealed class EmptyFactory : IRecordingStreamSourceLeaseFactory
    {
        public ValueTask<IRecordingStreamSourceLease?> AcquireAsync(RecordingSinkStartRequest request, CancellationToken token) =>
            ValueTask.FromResult<IRecordingStreamSourceLease?>(null);
    }
}
