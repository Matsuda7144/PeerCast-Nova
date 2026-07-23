namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsAudioRenderTraceTests
{
    [Fact]
    public void Records_four_ordered_writes_and_pre_stop_padding()
    {
        var trace = new PlaybackWindowsAudioRenderTrace(capturePreStopPadding: true);

        for (var index = 0; index < 4; index++)
        {
            trace.RecordWrite(CreateObservation(
                paddingFrameCount: index * 1024,
                startRequested: index == 0,
                startAcknowledged: index == 0));
        }
        trace.RecordPreStopPadding(available: true, paddingFrameCount: 512);

        var snapshot = trace.AudioRenderTrace;
        Assert.Equal(4, snapshot.Count);
        Assert.False(snapshot.Overflowed);
        for (var index = 0; index < snapshot.Count; index++)
        {
            var observation = snapshot.GetObservation(index);
            Assert.Equal(1024, observation.RequestedFrameCount);
            Assert.Equal(4096, observation.RequestedByteCount);
            Assert.True(observation.HasCapacityFrameCount);
            Assert.Equal(4800, observation.CapacityFrameCount);
            Assert.True(observation.HasPaddingFrameCount);
            Assert.Equal(index * 1024, observation.PaddingFrameCount);
            Assert.True(observation.HasAvailableFrameCount);
            Assert.Equal(4800 - (index * 1024), observation.AvailableFrameCount);
            Assert.Equal(index == 0, observation.StartRequested);
            Assert.True(observation.GetBufferAcknowledged);
            Assert.True(observation.ReleaseBufferAcknowledged);
            Assert.Equal(index == 0, observation.StartAcknowledged);
            Assert.Equal(PlaybackWindowsAudioRenderWriteResult.Processed, observation.Result);
        }
        Assert.Equal(1, snapshot.PreStop.QueryCount);
        Assert.True(snapshot.PreStop.Available);
        Assert.Equal(512, snapshot.PreStop.PaddingFrameCount);
        Assert.False(snapshot.PreStop.Overflowed);
    }

    [Fact]
    public void Fifth_write_marks_overflow_without_replacing_first_four()
    {
        var trace = new PlaybackWindowsAudioRenderTrace(capturePreStopPadding: false);

        for (var index = 0; index < 5; index++)
        {
            trace.RecordWrite(CreateObservation(paddingFrameCount: index));
        }

        var first = trace.AudioRenderTrace;
        var second = trace.AudioRenderTrace;
        Assert.Equal(4, first.Count);
        Assert.True(first.Overflowed);
        Assert.Equal(first, second);
        Assert.Equal(0, first.First.PaddingFrameCount);
        Assert.Equal(3, first.Fourth.PaddingFrameCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.GetObservation(4));
    }

    [Fact]
    public void Invalid_capacity_relationship_marks_overflow_without_retaining_write()
    {
        var trace = new PlaybackWindowsAudioRenderTrace(capturePreStopPadding: false);

        trace.RecordWrite(CreateObservation(paddingFrameCount: 100) with
        {
            AvailableFrameCount = 4701,
        });

        var snapshot = trace.AudioRenderTrace;
        Assert.Equal(0, snapshot.Count);
        Assert.True(snapshot.Overflowed);
    }

    [Fact]
    public void Partial_failed_write_is_retained_without_inventing_native_values()
    {
        var trace = new PlaybackWindowsAudioRenderTrace(capturePreStopPadding: false);

        trace.RecordWrite(new(
            RequestedFrameCount: 1024,
            RequestedByteCount: 4096,
            HasCapacityFrameCount: false,
            CapacityFrameCount: 0,
            HasPaddingFrameCount: false,
            PaddingFrameCount: 0,
            HasAvailableFrameCount: false,
            AvailableFrameCount: 0,
            StartRequested: true,
            GetBufferAcknowledged: false,
            ReleaseBufferAcknowledged: false,
            StartAcknowledged: false,
            Result: PlaybackWindowsAudioRenderWriteResult.Failure));

        var snapshot = trace.AudioRenderTrace;
        Assert.Equal(1, snapshot.Count);
        Assert.False(snapshot.Overflowed);
        Assert.False(snapshot.First.HasCapacityFrameCount);
        Assert.False(snapshot.First.HasPaddingFrameCount);
        Assert.False(snapshot.First.HasAvailableFrameCount);
        Assert.Equal(PlaybackWindowsAudioRenderWriteResult.Failure, snapshot.First.Result);
    }

    [Fact]
    public void Repeated_pre_stop_query_is_bounded_and_fail_closed()
    {
        var trace = new PlaybackWindowsAudioRenderTrace(capturePreStopPadding: true);

        trace.RecordPreStopPadding(available: true, paddingFrameCount: 256);
        trace.RecordPreStopPadding(available: true, paddingFrameCount: 128);

        var snapshot = trace.AudioRenderTrace.PreStop;
        Assert.Equal(2, snapshot.QueryCount);
        Assert.True(snapshot.Available);
        Assert.Equal(256, snapshot.PaddingFrameCount);
        Assert.True(snapshot.Overflowed);
    }

    [Fact]
    public void Failed_pre_stop_query_retains_unavailable_checkpoint()
    {
        var trace = new PlaybackWindowsAudioRenderTrace(capturePreStopPadding: true);

        trace.RecordPreStopPadding(available: false, paddingFrameCount: 0);

        var snapshot = trace.AudioRenderTrace.PreStop;
        Assert.Equal(1, snapshot.QueryCount);
        Assert.False(snapshot.Available);
        Assert.Equal(0, snapshot.PaddingFrameCount);
        Assert.False(snapshot.Overflowed);
    }

    private static PlaybackWindowsAudioRenderWriteObservation CreateObservation(
        int paddingFrameCount,
        bool startRequested = false,
        bool startAcknowledged = false) =>
        new(
            RequestedFrameCount: 1024,
            RequestedByteCount: 4096,
            HasCapacityFrameCount: true,
            CapacityFrameCount: 4800,
            HasPaddingFrameCount: true,
            PaddingFrameCount: paddingFrameCount,
            HasAvailableFrameCount: true,
            AvailableFrameCount: 4800 - paddingFrameCount,
            StartRequested: startRequested,
            GetBufferAcknowledged: true,
            ReleaseBufferAcknowledged: true,
            StartAcknowledged: startAcknowledged,
            Result: PlaybackWindowsAudioRenderWriteResult.Processed);
}
