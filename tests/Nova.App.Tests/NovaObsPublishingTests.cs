using System.Text.Json;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaObsPublishingTests
{
    [Fact]
    public async Task UnstartedRuntimeReadIsSanitizedAndDoesNotBindOrCreateAnotherOwner()
    {
        var occupied = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        occupied.Start();
        try
        {
            var endpoint = (System.Net.IPEndPoint)occupied.LocalEndpoint;
            await using var runtime = new NovaObsPublishingRuntime(new()
            {
                Ingest = new() { Port = endpoint.Port, Application = "PRIVATE-APP", StreamName = "PRIVATE-KEY" },
                Http = new() { Port = endpoint.Port },
                RelayPort = endpoint.Port,
            });
            var adapter = new NovaObsPublishingReadOnlyStatus(runtime);
            var first = adapter.Read();
            Assert.Equal(NovaObsPublishingAvailability.NotStarted, first.Availability);
            Assert.Same(first, adapter.Read());
            var json = JsonSerializer.Serialize(first);
            Assert.DoesNotContain("PRIVATE", json);
            Assert.DoesNotContain("rtmp", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", json, StringComparison.OrdinalIgnoreCase);
            await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(NovaObsPublishingAvailability.Stopped, adapter.Read().Availability);
            Assert.Equal(endpoint, occupied.LocalEndpoint);
        }
        finally { occupied.Stop(); }
    }

    [Fact]
    public async Task Setup_uses_the_runtime_configuration_and_effective_bound_port()
    {
        await using var runtime = new NovaObsPublishingRuntime(new()
        {
            Ingest = new() { Port = 0, Application = "configured-app", StreamName = "configured-name" },
        });
        Assert.Null(runtime.Snapshot.RtmpServer);
        runtime.Start(CancellationToken.None);
        var status = runtime.Snapshot;
        var panel = Project(status);
        Assert.NotNull(status.RtmpServer);
        Assert.StartsWith("rtmp://127.0.0.1:", panel.Server, StringComparison.Ordinal);
        Assert.EndsWith("/configured-app", panel.Server, StringComparison.Ordinal);
        Assert.DoesNotContain(":0/", panel.Server, StringComparison.Ordinal);
        Assert.Equal(status.RtmpServer, panel.Server);
        Assert.Equal("configured-name", panel.PublishName);
        Assert.Equal("OBS待機中", panel.StateText);
        Assert.Equal(NovaObsPublishingTone.Normal, panel.Tone);
        Assert.Null(panel.HttpStreamUrl);
    }

    [Theory]
    [InlineData(ChannelSessionState.Created, true, false, false, "OBS待機中")]
    [InlineData(ChannelSessionState.Connecting, false, true, false, "OBS接続中")]
    [InlineData(ChannelSessionState.Negotiating, true, true, false, "OBS接続中")]
    [InlineData(ChannelSessionState.Negotiating, true, false, false, "OBS待機中")]
    [InlineData(ChannelSessionState.Receiving, true, true, true, "OBSから配信を受信中")]
    [InlineData(ChannelSessionState.Receiving, true, true, false, "OBS接続中")]
    [InlineData(ChannelSessionState.Stopping, false, false, false, "停止中")]
    [InlineData(ChannelSessionState.Stopped, false, false, false, "停止")]
    [InlineData(ChannelSessionState.Faulted, false, false, false, "配信エラー")]
    public void Lifecycle_mapping_requires_accepted_HEAD_for_active(
        ChannelSessionState state, bool listener, bool connected, bool head, string expected)
    {
        var panel = Project(Status() with
        {
            State = state,
            ListenerReady = listener,
            PublisherConnected = connected,
            HeadAvailable = head,
        });
        Assert.Equal(expected, panel.StateText);
        Assert.Equal(expected == "OBSから配信を受信中", panel.Tone == NovaObsPublishingTone.Active);
    }

    [Theory]
    [InlineData(ObsPublishChannelEndReason.PublisherStopped, "停止", NovaObsPublishingTone.Normal)]
    [InlineData(ObsPublishChannelEndReason.Disconnected, "停止", NovaObsPublishingTone.Normal)]
    [InlineData(ObsPublishChannelEndReason.HostCancelled, "停止（キャンセル）", NovaObsPublishingTone.Special)]
    [InlineData(ObsPublishChannelEndReason.ProtocolRejected, "配信エラー", NovaObsPublishingTone.Warning)]
    [InlineData(ObsPublishChannelEndReason.StartupFailed, "配信エラー", NovaObsPublishingTone.Warning)]
    public void Terminal_reason_dominates_stale_receiving_without_success_claim(
        ObsPublishChannelEndReason reason, string expected, NovaObsPublishingTone tone)
    {
        var panel = Project(Status() with { State = ChannelSessionState.Receiving, HeadAvailable = true, EndReason = reason });
        Assert.Equal(expected, panel.StateText);
        Assert.Equal(tone, panel.Tone);
        Assert.True(panel.IsTerminal);
        Assert.Equal("OBS配信待受: 停止", panel.ListenerText);
        Assert.Null(panel.HttpStreamUrl);
        Assert.DoesNotContain("正常終了", panel.StateText, StringComparison.Ordinal);
    }

    [Fact]
    public void AAC_diagnostic_projects_only_original_bounded_identifiers()
    {
        var status = Status() with
        {
            State = ChannelSessionState.Stopped,
            EndReason = ObsPublishChannelEndReason.ProtocolRejected,
            RejectionStage = ObsRtmpRejectionStage.AacSequenceHeader,
            RejectionReason = ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig,
        };
        var panel = Project(status);
        Assert.Equal("AAC設定が現在の対応条件と一致しません。", panel.DiagnosticText);
        Assert.Equal("Stage: AacSequenceHeader / Reason: UnsupportedAudioSpecificConfig", panel.TechnicalDetails);
        var json = JsonSerializer.Serialize(panel);
        foreach (var excluded in new[] { "Payload", "Context", "ConfigLength", "Credential", "Transaction", "Metadata", "Exception" })
            Assert.DoesNotContain(excluded, json, StringComparison.Ordinal);
        Assert.All(typeof(ObsPublishStatusSnapshot).GetProperties(), property =>
            Assert.False(typeof(IDisposable).IsAssignableFrom(property.PropertyType)));
    }

    [Theory]
    [InlineData(ObsRtmpRejectionReason.InvalidAvcConfiguration)]
    [InlineData(ObsRtmpRejectionReason.UnsupportedAvcConfigurationExtension)]
    public void AVC_diagnostic_keeps_the_original_reason(ObsRtmpRejectionReason reason)
    {
        var panel = Project(Status() with
        {
            EndReason = ObsPublishChannelEndReason.ProtocolRejected,
            RejectionStage = ObsRtmpRejectionStage.AvcSequenceHeader,
            RejectionReason = reason,
        });
        Assert.Contains("H.264 / AVC", panel.DiagnosticText, StringComparison.Ordinal);
        Assert.Contains(reason.ToString(), panel.TechnicalDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void Old_generation_and_session_cannot_overwrite_selected_new_generation()
    {
        var projection = new NovaObsPublishingProjection();
        var old = Status();
        var next = Status();
        Assert.True(projection.SelectGeneration(1, old.SessionId.Value));
        Assert.NotNull(projection.Project(1, old));
        Assert.True(projection.SelectGeneration(2, next.SessionId.Value));
        Assert.False(projection.SelectGeneration(1, old.SessionId.Value));
        Assert.Null(projection.Project(1, old with { Revision = 999 }));
        Assert.Null(projection.Project(2, old));
        Assert.NotNull(projection.Project(2, next));
    }

    [Fact]
    public void Stale_revision_and_delayed_receiving_cannot_replace_stopped_state()
    {
        var projection = new NovaObsPublishingProjection();
        var status = Status();
        projection.SelectGeneration(1, status.SessionId.Value);
        Assert.NotNull(projection.Project(1, status with { Revision = 10, State = ChannelSessionState.Stopped }));
        Assert.Null(projection.Project(1, status with { Revision = 9 }));
        Assert.Null(projection.Project(1, status with
        {
            Revision = 11,
            State = ChannelSessionState.Receiving,
            HeadAvailable = true,
        }));
        Assert.NotNull(projection.Project(1, status with
        {
            Revision = 11,
            State = ChannelSessionState.Stopped,
            EndReason = ObsPublishChannelEndReason.HostCancelled,
        }));
    }

    [Fact]
    public async Task Disposed_runtime_is_no_longer_receiving_or_listening()
    {
        var runtime = new NovaObsPublishingRuntime(new() { Ingest = new() { Port = 0 } });
        runtime.Start(CancellationToken.None);
        await runtime.DisposeAsync();
        var result = await runtime.Completion;
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, result.EndReason);
        Assert.True(result.ShutdownCompleted);
        var status = runtime.Snapshot;
        Assert.Equal(result.EndReason, status.EndReason);
        Assert.Equal(ChannelSessionState.Stopped, status.State);
        Assert.False(status.ListenerReady);
        Assert.False(status.HeadAvailable);
        Assert.Null(status.HttpStreamUrl);
        Assert.Equal("停止（キャンセル）", Project(status).StateText);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Forced_child_stop_before_owner_cancellation_preserves_terminal_evidence(
        bool relayFirst, bool failCleanup)
    {
        var cancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endSelected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCancel = new ManualResetEventSlim();
        var limit = TimeSpan.FromSeconds(10);
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = async point =>
            {
                if (point == ObsPublishTerminationPoint.BeforeResourceRelease) await releaseCleanup.Task;
                if (point == ObsPublishTerminationPoint.RecordingAdmissionClosed) endSelected.TrySetResult();
                if (failCleanup && point == ObsPublishTerminationPoint.FanoutCleanup)
                    throw new IOException("fixture cleanup failure");
            },
        };
        var runtime = NovaObsPublishingRuntime.ForOwner(host);
        runtime.Start(CancellationToken.None);
        using var callback = host.OwnerStopToken.Register(() =>
        {
            cancelEntered.TrySetResult();
            if (!releaseCancel.Wait(limit)) throw new TimeoutException();
        });
        var dispose = runtime.DisposeAsync().AsTask();
        try
        {
            await cancelEntered.Task.WaitAsync(limit);
            // Run one existing owned release first while cancellation propagation
            // and the other releases remain blocked. Reflection avoids a new host API.
            var child = relayFirst
                ? (IAsyncDisposable)Assert.IsType<PcpDownstreamRelayServer>(typeof(ObsPublishChannelHost)
                    .GetField("_relay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host))
                : host.Fanout;
            await child.DisposeAsync().AsTask().WaitAsync(limit);
            await endSelected.Task.WaitAsync(limit);
        }
        finally
        {
            releaseCleanup.TrySetResult();
            releaseCancel.Set();
            if (failCleanup)
                await Assert.ThrowsAsync<InvalidOperationException>(() => dispose.WaitAsync(limit));
            else
                await dispose.WaitAsync(limit);
        }
        var result = await runtime.Completion.WaitAsync(limit);
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, result.EndReason);
        Assert.Equal(ObsRtmpIngestEndReason.Cancelled, result.Ingest!.EndReason);
        if (relayFirst)
            Assert.Equal(PcpDownstreamRelayServerEndReason.Stopped, result.Relay!.EndReason);
        else
            Assert.Contains(result.Relay!.EndReason, new[]
            {
                PcpDownstreamRelayServerEndReason.Stopped,
                PcpDownstreamRelayServerEndReason.CallerCanceled,
            });
        Assert.Equal(!failCleanup, result.ShutdownCompleted);
        var status = runtime.Snapshot;
        Assert.Equal(result.EndReason, status.EndReason);
        Assert.Equal(ChannelSessionState.Stopped, status.State);
        Assert.False(status.ListenerReady);
        Assert.False(status.HeadAvailable);
        Assert.Null(status.HttpStreamUrl);
        Assert.Equal("停止（キャンセル）", Project(status).StateText);
    }

    [Theory]
    [InlineData(false, ObsPublishChannelEndReason.ConsumerFaulted)]
    [InlineData(true, ObsPublishChannelEndReason.InternalFailure)]
    public async Task Dispose_preserves_failure_selected_before_forced_cleanup(
        bool failEndSelection, ObsPublishChannelEndReason expected)
    {
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var limit = TimeSpan.FromSeconds(10);
        var host = new ObsPublishChannelHost(new() { Ingest = new() { Port = 0 } })
        {
            TerminationObserver = async point =>
            {
                if (failEndSelection && point == ObsPublishTerminationPoint.BeforeEndSelection)
                    throw new IOException("fixture end selection failure");
                if (point == ObsPublishTerminationPoint.RecordingAdmissionClosed)
                {
                    selected.TrySetResult();
                    await release.Task;
                }
            },
        };
        var runtime = NovaObsPublishingRuntime.ForOwner(host);
        runtime.Start(CancellationToken.None);
        var forceRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopObservation = host.OwnerStopToken.Register(() => forceRequested.TrySetResult());
        Task? dispose = null;
        try
        {
            // No stop has been requested: losing the mandatory child is a failure.
            await host.Fanout.DisposeAsync().AsTask().WaitAsync(limit);
            await selected.Task.WaitAsync(limit);
            dispose = runtime.DisposeAsync().AsTask();
            await forceRequested.Task.WaitAsync(limit);
        }
        finally
        {
            release.TrySetResult();
            await (dispose ?? runtime.DisposeAsync().AsTask()).WaitAsync(limit);
        }
        var result = await runtime.Completion;
        Assert.Equal(expected, result.EndReason);
        Assert.True(result.ShutdownCompleted);
        var status = runtime.Snapshot;
        Assert.Equal(result.EndReason, status.EndReason);
        Assert.False(status.ListenerReady);
        Assert.False(status.HeadAvailable);
        Assert.Null(status.HttpStreamUrl);
        Assert.Equal("配信エラー", Project(status).StateText);
    }

    private static NovaObsPublishingPanel Project(ObsPublishStatusSnapshot status)
    {
        var projection = new NovaObsPublishingProjection();
        projection.SelectGeneration(1, status.SessionId.Value);
        return Assert.IsType<NovaObsPublishingPanel>(projection.Project(1, status));
    }

    private static ObsPublishStatusSnapshot Status() => new(
        new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()), 1,
        ChannelSessionState.Created, true, false, false,
        "rtmp://127.0.0.1:17101/fixture", "fixture", null, null, null, null);

}
