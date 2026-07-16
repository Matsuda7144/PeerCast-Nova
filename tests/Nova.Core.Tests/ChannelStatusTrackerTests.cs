using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Tests;

public sealed class ChannelStatusTrackerTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public void Identifiers_RejectEmptyValues()
    {
        Assert.Throws<ArgumentException>(() => new ChannelId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ChannelSessionId(Guid.Empty));
    }

    [Fact]
    public void Constructor_PublishesCreatedSnapshot()
    {
        var tracker = CreateTracker();

        var snapshot = tracker.Snapshot;

        Assert.Same(ChannelId, snapshot.ChannelId);
        Assert.Same(SessionId, snapshot.SessionId);
        Assert.Equal(ChannelSessionState.Created, snapshot.State);
        Assert.Same(ChannelMetadataSnapshot.Empty, snapshot.Metadata);
        Assert.Null(snapshot.StreamPosition);
        Assert.Null(snapshot.Fault);
        Assert.Equal(0, snapshot.Revision);
    }

    [Fact]
    public void TransitionTo_ValidPathPublishesImmutableRevisions()
    {
        var tracker = CreateTracker();
        var created = tracker.Snapshot;

        var connecting = tracker.TransitionTo(ChannelSessionState.Connecting);
        var negotiating = tracker.TransitionTo(ChannelSessionState.Negotiating);
        var receiving = tracker.TransitionTo(ChannelSessionState.Receiving);

        Assert.Equal(ChannelSessionState.Created, created.State);
        Assert.Equal(ChannelSessionState.Connecting, connecting.State);
        Assert.Equal(ChannelSessionState.Negotiating, negotiating.State);
        Assert.Equal(ChannelSessionState.Receiving, receiving.State);
        Assert.Equal(3, receiving.Revision);
    }

    [Fact]
    public void TransitionTo_RepeatedStateIsIdempotent()
    {
        var tracker = CreateTracker();
        var connecting = tracker.TransitionTo(ChannelSessionState.Connecting);

        var repeated = tracker.TransitionTo(ChannelSessionState.Connecting);

        Assert.Same(connecting, repeated);
        Assert.Equal(1, repeated.Revision);
    }

    [Fact]
    public void TransitionTo_RejectsInvalidSkip()
    {
        var tracker = CreateTracker();

        Assert.Throws<InvalidOperationException>(
            () => tracker.TransitionTo(ChannelSessionState.Receiving));
    }

    [Fact]
    public void Reconnect_PreservesLocalIdentityMetadataAndPosition()
    {
        var tracker = CreateReceivingTracker();
        var metadata = new ChannelMetadataSnapshot(
            name: "Controlled test channel",
            contentType: "video/test",
            bitrateKbps: 512);

        tracker.UpdateMetadata(metadata);
        tracker.AdvanceStreamPosition(4096);

        var reconnecting = tracker.TransitionTo(
            ChannelSessionState.Reconnecting);

        Assert.Same(ChannelId, reconnecting.ChannelId);
        Assert.Same(SessionId, reconnecting.SessionId);
        Assert.Same(metadata, reconnecting.Metadata);
        Assert.Equal(4096, reconnecting.StreamPosition);
    }

    [Fact]
    public void UpdateMetadata_PublishesOnlyObservableChanges()
    {
        var tracker = CreateTracker();
        tracker.TransitionTo(ChannelSessionState.Connecting);
        tracker.TransitionTo(ChannelSessionState.Negotiating);
        var metadata = new ChannelMetadataSnapshot(name: "Test");

        var updated = tracker.UpdateMetadata(metadata);
        var repeated = tracker.UpdateMetadata(metadata);

        Assert.Same(metadata, updated.Metadata);
        Assert.Same(updated, repeated);
    }

    [Fact]
    public void AdvanceStreamPosition_RequiresReceivingAndMonotonicValues()
    {
        var tracker = CreateReceivingTracker();

        var advanced = tracker.AdvanceStreamPosition(128);
        var repeated = tracker.AdvanceStreamPosition(128);

        Assert.Equal(128, advanced.StreamPosition);
        Assert.Same(advanced, repeated);
        Assert.Throws<InvalidOperationException>(
            () => tracker.AdvanceStreamPosition(127));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => tracker.AdvanceStreamPosition(-1));
    }

    [Fact]
    public void ApplyObservation_MetadataAndPositionUseOneRevision()
    {
        var tracker = CreateReceivingTracker();
        var before = tracker.Snapshot;
        var track = new ChannelTrackMetadataSnapshot(
            Title: "Synthetic title",
            Creator: "Synthetic creator");
        var metadata = new ChannelMetadataSnapshot(
            name: "Synthetic channel",
            streamType: "RAW",
            comment: "Synthetic comment",
            streamExtension: ".test",
            track: track);

        var applied = tracker.ApplyObservation(metadata, 1024);

        Assert.Equal(before.Revision + 1, applied.Revision);
        Assert.Same(metadata, applied.Metadata);
        Assert.Same(track, applied.Metadata.Track);
        Assert.Equal(1024, applied.StreamPosition);
    }

    [Fact]
    public void Stopped_IsTerminal()
    {
        var tracker = CreateTracker();
        tracker.TransitionTo(ChannelSessionState.Stopping);
        tracker.TransitionTo(ChannelSessionState.Stopped);

        Assert.Throws<InvalidOperationException>(
            () => tracker.TransitionTo(ChannelSessionState.Connecting));
    }

    [Fact]
    public void FaultSnapshot_RejectsInvalidFixedValuesAndHasNoTextProperty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChannelFaultSnapshot(
                (ChannelFaultCategory)int.MaxValue,
                ChannelFaultStage.Connecting,
                attemptCount: 1,
                reconnectCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChannelFaultSnapshot(
                ChannelFaultCategory.ConnectionFailed,
                (ChannelFaultStage)int.MaxValue,
                attemptCount: 1,
                reconnectCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChannelFaultSnapshot(
                ChannelFaultCategory.ConnectionFailed,
                ChannelFaultStage.Connecting,
                attemptCount: 0,
                reconnectCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChannelFaultSnapshot(
                ChannelFaultCategory.ConnectionFailed,
                ChannelFaultStage.Connecting,
                attemptCount: 1,
                reconnectCount: 1));

        var propertyTypes = typeof(ChannelFaultSnapshot)
            .GetProperties()
            .Select(property => property.PropertyType);

        Assert.DoesNotContain(typeof(string), propertyTypes);
        Assert.DoesNotContain(
            propertyTypes,
            type => typeof(Exception).IsAssignableFrom(type));
    }

    [Fact]
    public void RecordFault_IsAtomicAndDirectFaultTransitionIsRejected()
    {
        var tracker = CreateReceivingTracker();
        var before = tracker.Snapshot;
        var fault = new ChannelFaultSnapshot(
            ChannelFaultCategory.ProtocolViolation,
            ChannelFaultStage.Receiving,
            attemptCount: 2,
            reconnectCount: 1);

        Assert.Throws<InvalidOperationException>(
            () => tracker.TransitionTo(ChannelSessionState.Faulted));

        var faulted = tracker.RecordFault(fault);
        var repeated = tracker.RecordFault(fault);

        Assert.Equal(ChannelSessionState.Faulted, faulted.State);
        Assert.Same(fault, faulted.Fault);
        Assert.Equal(before.Revision + 1, faulted.Revision);
        Assert.Same(faulted, repeated);
    }

    [Fact]
    public void Fault_PersistsThroughStopAndClearsForExplicitReconnect()
    {
        var stoppingTracker = CreateReceivingTracker();
        var stoppingFault = new ChannelFaultSnapshot(
            ChannelFaultCategory.RemoteEnded,
            ChannelFaultStage.Receiving,
            attemptCount: 1,
            reconnectCount: 0);
        stoppingTracker.RecordFault(stoppingFault);

        var stopping = stoppingTracker.TransitionTo(
            ChannelSessionState.Stopping);
        var stopped = stoppingTracker.TransitionTo(
            ChannelSessionState.Stopped);

        Assert.Same(stoppingFault, stopping.Fault);
        Assert.Same(stoppingFault, stopped.Fault);

        var reconnectingTracker = CreateReceivingTracker();
        reconnectingTracker.RecordFault(stoppingFault);

        var reconnecting = reconnectingTracker.TransitionTo(
            ChannelSessionState.Reconnecting);

        Assert.Null(reconnecting.Fault);
    }

    private static ChannelStatusTracker CreateTracker()
    {
        return new ChannelStatusTracker(ChannelId, SessionId);
    }

    private static ChannelStatusTracker CreateReceivingTracker()
    {
        var tracker = CreateTracker();
        tracker.TransitionTo(ChannelSessionState.Connecting);
        tracker.TransitionTo(ChannelSessionState.Negotiating);
        tracker.TransitionTo(ChannelSessionState.Receiving);
        return tracker;
    }
}
