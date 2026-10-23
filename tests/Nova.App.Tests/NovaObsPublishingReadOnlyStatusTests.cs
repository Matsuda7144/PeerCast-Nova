using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaObsPublishingReadOnlyStatusTests
{
    [Fact]
    public void EqualRevisionCannotReplacePublishedFactsAndNewRevisionCanAdvance()
    {
        var source = new Source();
        var adapter = new NovaObsPublishingReadOnlyStatus(source);
        var initial = adapter.Read();
        source.Current = source.Current with { State = ChannelSessionState.Negotiating, ListenerReady = true };
        Assert.Same(initial, adapter.Read());
        source.Current = source.Current with { Revision = 2 };
        var listening = adapter.Read();
        Assert.Equal("OBS待機中", listening.StateText);
        var delayed = source.Current;
        source.Current = source.Current with { Revision = 3, PublisherConnected = true };
        var connected = adapter.Read();
        Assert.Equal("OBS接続中", connected.StateText);
        source.Current = delayed;
        Assert.Same(connected, adapter.Read());
    }

    [Fact]
    public void BorrowedRuntimeReadsNeverInvokeFactoryStartOrDispose()
    {
        var factory = new TrackingFactory();
        var owner = factory.Create();
        var adapter = new NovaObsPublishingReadOnlyStatus(owner);
        Assert.Equal(0, factory.Owner.ReadCount);
        for (var index = 0; index < 20; index++)
            Assert.Equal(NovaObsPublishingAvailability.NotStarted, adapter.Read().Availability);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(20, factory.Owner.ReadCount);
        Assert.Equal(0, factory.Owner.StartCount);
        Assert.Equal(0, factory.Owner.DisposeCount);
    }

    [Fact]
    public void MissingOwnerIsUnavailableAndDoesNotInventAnIdentity()
    {
        var result = new NovaObsPublishingReadOnlyStatus(null).Read();
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, result.Availability);
        Assert.Null(result.ChannelId);
        Assert.Null(result.SessionId);
        Assert.Equal("利用不可", result.StateText);
    }

    [Fact]
    public void ConstructionDoesNotReadOrStartAndReadBorrowsExactlyOneSnapshot()
    {
        var source = new Source();
        var adapter = new NovaObsPublishingReadOnlyStatus(source);
        Assert.Equal(0, source.ReadCount);
        var result = adapter.Read();
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(NovaObsPublishingAvailability.NotStarted, result.Availability);
        Assert.Equal(source.Current.ChannelId.Value, result.ChannelId);
        Assert.Equal(source.Current.SessionId.Value, result.SessionId);
        Assert.Equal("未起動（待受未開始）", result.StateText);
        Assert.DoesNotContain(typeof(INovaObsPublishingReadOnlyStatus).GetMethods(),
            method => method.Name is "Start" or "Stop" or "Dispose" or "DisposeAsync");
    }

    [Theory]
    [InlineData(false, false, false, ChannelSessionState.Connecting, "待受準備中")]
    [InlineData(true, false, false, ChannelSessionState.Negotiating, "OBS待機中")]
    [InlineData(true, true, false, ChannelSessionState.Negotiating, "OBS接続中")]
    [InlineData(true, true, true, ChannelSessionState.Receiving, "OBSから配信を受信中")]
    public void ReusesExistingProjectionAndDoesNotTreatListenerOrTcpAsAcceptedMedia(
        bool listener, bool connected, bool head, ChannelSessionState state, string label)
    {
        var source = new Source();
        source.Current = source.Current with
        { ListenerReady = listener, PublisherConnected = connected, HeadAvailable = head, State = state };
        var result = new NovaObsPublishingReadOnlyStatus(source).Read();
        Assert.Equal(NovaObsPublishingAvailability.Observed, result.Availability);
        Assert.Equal(label, result.StateText);
    }

    [Fact]
    public void StoppedAndNotStartedAreDifferentAndTerminalDominatesDelayedReceiving()
    {
        var source = new Source();
        var adapter = new NovaObsPublishingReadOnlyStatus(source);
        Assert.Equal(NovaObsPublishingAvailability.NotStarted, adapter.Read().Availability);
        source.Current = source.Current with
        { State = ChannelSessionState.Stopped, EndReason = ObsPublishChannelEndReason.PublisherStopped, Revision = 10 };
        var stopped = adapter.Read();
        Assert.Equal(NovaObsPublishingAvailability.Stopped, stopped.Availability);
        Assert.Equal("停止", stopped.StateText);
        source.Current = source.Current with
        { State = ChannelSessionState.Receiving, EndReason = null, HeadAvailable = true, Revision = 11 };
        Assert.Same(stopped, adapter.Read());
    }

    [Fact]
    public void ErrorsKeepOnlyExistingSafeClassificationAndOmitConnectionInformation()
    {
        var source = new Source();
        source.Current = source.Current with
        {
            State = ChannelSessionState.Stopped,
            EndReason = ObsPublishChannelEndReason.ProtocolRejected,
            RejectionStage = ObsRtmpRejectionStage.AacSequenceHeader,
            RejectionReason = ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig,
        };
        var result = new NovaObsPublishingReadOnlyStatus(source).Read();
        Assert.Equal(NovaObsPublishingAvailability.Stopped, result.Availability);
        Assert.Equal("配信エラー", result.StateText);
        Assert.Equal(source.Current.RejectionStage, result.RejectionStage);
        Assert.Equal(source.Current.RejectionReason, result.RejectionReason);
        Assert.Equal(source.Current.EndReason, result.EndReason);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("DO-NOT-DISPLAY", json);
        Assert.DoesNotContain("rtmp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", json, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(result.DiagnosticText.Length, 1, 150);
    }

    [Fact]
    public void SourceFailureIsUnavailableWithoutExceptionTextAndRecoveryCanReadAgain()
    {
        var source = new Source { Throw = true };
        var adapter = new NovaObsPublishingReadOnlyStatus(source);
        var result = adapter.Read();
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, result.Availability);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(result));
        source.Throw = false;
        Assert.Equal(NovaObsPublishingAvailability.NotStarted, adapter.Read().Availability);
    }

    [Fact]
    public void ReplacementOwnerRequiresExplicitNewComposition()
    {
        var source = new Source();
        var adapter = new NovaObsPublishingReadOnlyStatus(source);
        _ = adapter.Read();
        source.Current = source.Current with { SessionId = new ChannelSessionId(Guid.NewGuid()) };
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, adapter.Read().Availability);
        Assert.Equal(NovaObsPublishingAvailability.NotStarted, new NovaObsPublishingReadOnlyStatus(source).Read().Availability);
    }

    [Fact]
    public void SameSessionCannotSwapChannelIdentityAndLowerRevisionCannotOverwriteState()
    {
        var source = new Source();
        var adapter = new NovaObsPublishingReadOnlyStatus(source);
        var initial = adapter.Read();
        source.Current = source.Current with { Revision = 0, ListenerReady = true };
        Assert.Same(initial, adapter.Read());
        source.Current = source.Current with { Revision = 2, ChannelId = new ChannelId(Guid.NewGuid()) };
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, adapter.Read().Availability);
    }

    [Fact]
    public void InvalidSnapshotIsFailClosed()
    {
        var source = new Source();
        source.Current = source.Current with { State = (ChannelSessionState)999 };
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, new NovaObsPublishingReadOnlyStatus(source).Read().Availability);
        source.Current = null!;
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, new NovaObsPublishingReadOnlyStatus(source).Read().Availability);
    }

    private sealed class Source : INovaObsPublishingStatusSource
    {
        public ObsPublishStatusSnapshot Current { get; set; } = new(
            new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()), 1,
            ChannelSessionState.Created, false, false, false,
            "rtmp://DO-NOT-DISPLAY", "DO-NOT-DISPLAY", "http://DO-NOT-DISPLAY", null, null, null);
        public bool Throw { get; set; }
        public int ReadCount { get; private set; }
        public ObsPublishStatusSnapshot Snapshot
        {
            get
            {
                ReadCount++;
                if (Throw) throw new InvalidOperationException("SECRET endpoint and credential");
                return Current;
            }
        }
    }

    private sealed class TrackingFactory : INovaObsPublishingRuntimeFactory
    {
        public TrackingRuntime Owner { get; } = new();
        public int CreateCount { get; private set; }
        public INovaObsPublishingRuntime Create() { CreateCount++; return Owner; }
    }

    private sealed class TrackingRuntime : INovaObsPublishingRuntime
    {
        private readonly Source _source = new();
        public int ReadCount => _source.ReadCount;
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public ObsPublishStatusSnapshot Snapshot => _source.Snapshot;
        public void Start(CancellationToken cancellationToken) => StartCount++;
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
