namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPipelineIntegrationProbeTests
{
    [Fact]
    public async Task RunAsync_PendingLicenseReviewRefusesBeforePathAccess()
    {
        using var probe = new PlaybackPipelineIntegrationProbe();

        var result = await probe.RunAsync(new()
        {
            RuntimeDirectory = "Z:\\does-not-exist\\runtime",
            DiagnosticMediaFile = "Z:\\does-not-exist\\media.bin",
        });

        Assert.Equal(PlaybackPipelineIntegrationProbeResult.LicenseReviewPending, result);
        Assert.Equal(PlaybackPipelineIntegrationProbeState.Idle, probe.Snapshot.State);
        Assert.Null(probe.Snapshot.CurrentPhase);
        Assert.Null(probe.Snapshot.LastCompletedPhase);
        Assert.Null(probe.Snapshot.LastStage);
    }

    [Fact]
    public void PhasePlan_ContainsEveryBoundaryExactlyOnceInFiveOrderedPhases()
    {
        var expectedPhases = new[]
        {
            PlaybackPipelineIntegrationPhase.RuntimeDecode,
            PlaybackPipelineIntegrationPhase.ManagedDelivery,
            PlaybackPipelineIntegrationPhase.SchedulingTiming,
            PlaybackPipelineIntegrationPhase.OutputLifecycle,
            PlaybackPipelineIntegrationPhase.RecoveryOrchestration,
        };

        var plan = PlaybackPipelineIntegrationProbe.PhasePlan;
        var actualPhases = plan
            .Select(static item => item.Phase)
            .Distinct()
            .ToArray();

        Assert.Equal(expectedPhases, actualPhases);
        Assert.Equal(plan.Count, plan.Select(static item => item.Stage).Distinct().Count());
        Assert.All(plan, static item =>
        {
            Assert.True(Enum.IsDefined(item.Phase));
            Assert.True(Enum.IsDefined(item.Stage));
        });
    }

    [Fact]
    public void WiringOrder_PreservesStageOrderWithinEachPhase()
    {
        var expected = new[]
        {
            PlaybackPipelineIntegrationStage.FfmpegAbiSymbolGate,
            PlaybackPipelineIntegrationStage.DiagnosticFixtureAdapter,
            PlaybackPipelineIntegrationStage.NativeDecoderContextLease,
            PlaybackPipelineIntegrationStage.CodecParameterOwnership,
            PlaybackPipelineIntegrationStage.NativeParameterTransfer,
            PlaybackPipelineIntegrationStage.CodecOpen,
            PlaybackPipelineIntegrationStage.PacketFrameProcessing,
            PlaybackPipelineIntegrationStage.CallbackScopedNativeFrame,
            PlaybackPipelineIntegrationStage.NativeFrameMetadata,
            PlaybackPipelineIntegrationStage.FramePlaneSample,
            PlaybackPipelineIntegrationStage.ManagedFrameCopy,
            PlaybackPipelineIntegrationStage.DecodedOutputAdapter,
            PlaybackPipelineIntegrationStage.ManagedFormatConversion,
            PlaybackPipelineIntegrationStage.AwaitedSinkDelivery,
            PlaybackPipelineIntegrationStage.PolicyClock,
            PlaybackPipelineIntegrationStage.BoundedScheduler,
            PlaybackPipelineIntegrationStage.AvTimingDecision,
            PlaybackPipelineIntegrationStage.TimedSchedulerDispatch,
            PlaybackPipelineIntegrationStage.LateItemDisposition,
            PlaybackPipelineIntegrationStage.LateDispatchCoordination,
            PlaybackPipelineIntegrationStage.LateReservationDisposition,
            PlaybackPipelineIntegrationStage.LateAwareSchedulerDecision,
            PlaybackPipelineIntegrationStage.LateAwareTimedDispatch,
            PlaybackPipelineIntegrationStage.RendererAudioSink,
            PlaybackPipelineIntegrationStage.WindowsAdapterSelection,
            PlaybackPipelineIntegrationStage.WindowsOutputActivation,
            PlaybackPipelineIntegrationStage.WindowsResourceBinding,
            PlaybackPipelineIntegrationStage.WindowsStreamInitialization,
            PlaybackPipelineIntegrationStage.WindowsBufferProcessing,
            PlaybackPipelineIntegrationStage.WindowsSchedulerAdapter,
            PlaybackPipelineIntegrationStage.FiniteContinuousPump,
            PlaybackPipelineIntegrationStage.LateAwareContinuousPump,
            PlaybackPipelineIntegrationStage.AudioRecovery,
            PlaybackPipelineIntegrationStage.RecoveryAwarePumpOrchestrator,
            PlaybackPipelineIntegrationStage.DeadlineAwareRunBudget,
        };

        Assert.Equal(expected, PlaybackPipelineIntegrationProbe.WiringOrder);
    }

    [Fact]
    public void PhasePlan_DoesNotInterleaveCompletedPhaseGroups()
    {
        var plan = PlaybackPipelineIntegrationProbe.PhasePlan;
        var seen = new HashSet<PlaybackPipelineIntegrationPhase>();
        PlaybackPipelineIntegrationPhase? previous = null;

        foreach (var descriptor in plan)
        {
            if (descriptor.Phase == previous)
                continue;

            Assert.DoesNotContain(descriptor.Phase, seen);
            seen.Add(descriptor.Phase);
            previous = descriptor.Phase;
        }
    }

    [Fact]
    public async Task RunAsync_DisposedProbeFailsClosed()
    {
        var probe = new PlaybackPipelineIntegrationProbe();
        probe.Dispose();

        Assert.Equal(
            PlaybackPipelineIntegrationProbeResult.Disposed,
            await probe.RunAsync(new()));
        Assert.Equal(PlaybackPipelineIntegrationProbeState.Disposed, probe.Snapshot.State);
        Assert.Null(probe.Snapshot.CurrentPhase);
    }
}
