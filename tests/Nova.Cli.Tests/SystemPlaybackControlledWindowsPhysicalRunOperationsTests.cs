using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class SystemPlaybackControlledWindowsPhysicalRunOperationsTests
{
    [Theory]
    [InlineData(
        PlaybackControlledWindowsOutputValidationKind.Video,
        PlaybackSchedulerInitialOutputKind.Video,
        PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioAndVideo,
        false)]
    [InlineData(
        PlaybackControlledWindowsOutputValidationKind.Audio,
        PlaybackSchedulerInitialOutputKind.Audio,
        PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioOnly,
        true)]
    public void CreateLifecycleOptions_MapsControlledKindAndPreservesBoundedDropOldest(
        PlaybackControlledWindowsOutputValidationKind kind,
        PlaybackSchedulerInitialOutputKind expectedInitialOutputKind,
        PlaybackPcpFlvAwaitedDecodeMediaSelection expectedMediaSelection,
        bool expectedOutputRestriction)
    {
        var options = SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
            TimeSpan.FromMilliseconds(100),
            kind);

        Assert.Equal(
            PlaybackSchedulerOptions.DefaultCapacity,
            options.Generation.Scheduler.Capacity);
        Assert.Equal(
            PlaybackSchedulerOptions.DefaultMaximumOwnedBytes,
            options.Generation.Scheduler.MaximumOwnedBytes);
        Assert.Equal(
            PlaybackSchedulerFullPolicy.DropOldest,
            options.Generation.Scheduler.FullPolicy);
        Assert.Equal(expectedInitialOutputKind, options.Generation.Scheduler.InitialOutputKind);
        Assert.Equal(
            expectedOutputRestriction,
            options.Generation.Scheduler.RestrictOutputToInitialKind);
        Assert.Equal(expectedMediaSelection, options.Production.MediaSelection);
    }

    [Fact]
    public void ProductionSchedulerDefaultsRemainVideoAndDropIncoming()
    {
        var productionDefault = new PlaybackSchedulerOptions();
        Assert.Equal(
            PlaybackSchedulerInitialOutputKind.Video,
            productionDefault.InitialOutputKind);
        Assert.False(productionDefault.RestrictOutputToInitialKind);
        Assert.Equal(PlaybackSchedulerFullPolicy.DropIncoming, productionDefault.FullPolicy);
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioAndVideo,
            new PlaybackPcpFlvAwaitedDecodeOptions().MediaSelection);
    }

    [Fact]
    public void AudioStageReporterIsNoOpUnlessControlledDiagnosticModeIsExplicit()
    {
        var output = new StringWriter();
        var normal = SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
            TimeSpan.FromMilliseconds(100),
            PlaybackControlledWindowsOutputValidationKind.Audio,
            diagnoseAudioNativeStages: false,
            diagnosticOutput: output);
        ReportStageByReflection(normal, "GetBufferEntering");
        Assert.Equal(string.Empty, output.ToString());

        var diagnostic =
            SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
                TimeSpan.FromMilliseconds(100),
                PlaybackControlledWindowsOutputValidationKind.Audio,
                diagnoseAudioNativeStages: true,
                diagnosticOutput: output);
        ReportStageByReflection(diagnostic, "GetBufferEntering");
        Assert.Equal(
            "PCN-AUDIO-NATIVE-STAGE: GetBufferEntering" + Environment.NewLine,
            output.ToString());
    }

    [Fact]
    public void RenderEvidenceCanCaptureNativeStagesWithoutDiagnosticCommandFlag()
    {
        var output = new StringWriter();
        var options = SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
            TimeSpan.FromMilliseconds(100),
            PlaybackControlledWindowsOutputValidationKind.Audio,
            diagnoseAudioNativeStages: false,
            diagnosticOutput: output,
            captureAudioNativeStages: true);

        ReportStageByReflection(options, "ProductionCleanupEntering");

        Assert.Equal(
            "PCN-AUDIO-NATIVE-STAGE: ProductionCleanupEntering" + Environment.NewLine,
            output.ToString());
    }

    [Fact]
    public void AudioRenderTraceConfigurationIsAbsentByDefaultAndExplicitWhenRequested()
    {
        var normal = SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
            TimeSpan.FromMilliseconds(100),
            PlaybackControlledWindowsOutputValidationKind.Audio);
        var normalReporter = GetReporterByReflection(normal);
        Assert.Null(GetRenderTraceConfigurationInterface(normalReporter));

        var configured =
            SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
                TimeSpan.FromMilliseconds(100),
                PlaybackControlledWindowsOutputValidationKind.Audio,
                captureAudioRenderTrace: true,
                capturePreStopPadding: true);
        var configuredReporter = GetReporterByReflection(configured);
        var configuration = GetRenderTraceConfigurationInterface(configuredReporter);
        Assert.NotNull(configuration);
        Assert.True(ReadBooleanProperty(
            configuredReporter,
            configuration!,
            "CaptureAudioRenderTrace"));
        Assert.True(ReadBooleanProperty(
            configuredReporter,
            configuration!,
            "CapturePreStopPadding"));
    }

    [Fact]
    public void PreStopPaddingCaptureRequiresAudioRenderTrace()
    {
        Assert.Throws<ArgumentException>(() =>
            SystemPlaybackControlledWindowsPhysicalRunOperations.CreateLifecycleOptions(
                TimeSpan.FromMilliseconds(100),
                PlaybackControlledWindowsOutputValidationKind.Audio,
                captureAudioRenderTrace: false,
                capturePreStopPadding: true));

        var options = new PlaybackControlledWindowsPhysicalRunOptions();
        Assert.Throws<ArgumentException>(() =>
            new SystemPlaybackControlledWindowsPhysicalRunOperations(
                options,
                captureAudioRenderTrace: false,
                capturePreStopPadding: true));
    }

    [Fact]
    public async Task ResumeControlledAudioTimingAsync_ResumesRetainedWaitToDelivery()
    {
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult[] results =
        [
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait,
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
        ];
        var resultIndex = 0;
        var deliveryCalls = 0;
        var delays = new List<TimeSpan>();

        var result = await SystemPlaybackControlledWindowsPhysicalRunOperations
            .ResumeControlledAudioTimingAsync(
                _ =>
                {
                    deliveryCalls++;
                    return ValueTask.FromResult(results[resultIndex++]);
                },
                (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(21),
                maximumResumeCount: 2,
                CancellationToken.None);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
            result);
        Assert.Equal(2, deliveryCalls);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromMilliseconds(21), delays[0]);
    }

    [Fact]
    public async Task ResumeControlledAudioTimingAsync_DoesNotResumeWouldBlock()
    {
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult[] results =
        [
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait,
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock,
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
        ];
        var resultIndex = 0;
        var deliveryCalls = 0;
        var delayCalls = 0;

        var result = await SystemPlaybackControlledWindowsPhysicalRunOperations
            .ResumeControlledAudioTimingAsync(
                _ =>
                {
                    deliveryCalls++;
                    return ValueTask.FromResult(results[resultIndex++]);
                },
                (_, _) =>
                {
                    delayCalls++;
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(21),
                maximumResumeCount: 2,
                CancellationToken.None);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock,
            result);
        Assert.Equal(2, deliveryCalls);
        Assert.Equal(1, delayCalls);
    }

    [Fact]
    public async Task ResumeControlledAudioTimingAsync_StopsAtFixedResumeLimit()
    {
        var deliveryCalls = 0;
        var delayCalls = 0;

        var result = await SystemPlaybackControlledWindowsPhysicalRunOperations
            .ResumeControlledAudioTimingAsync(
                _ =>
                {
                    deliveryCalls++;
                    return ValueTask.FromResult(
                        PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait);
                },
                (_, _) =>
                {
                    delayCalls++;
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(21),
                maximumResumeCount: 2,
                CancellationToken.None);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait,
            result);
        Assert.Equal(3, deliveryCalls);
        Assert.Equal(2, delayCalls);
    }

    private static void ReportStageByReflection(
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions options,
        string stageName)
    {
        var reporter = GetReporterByReflection(options);
        var method = reporter.GetType().GetMethod("Report");
        Assert.NotNull(method);
        var stageType = method.GetParameters()[0].ParameterType;
        var stage = Enum.Parse(stageType, stageName);
        method.Invoke(reporter, [stage]);
    }

    private static object GetReporterByReflection(
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions options)
    {
        var property = options.GetType().GetProperty(
            "AudioNativeStageReporter",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        var reporter = property.GetValue(options);
        Assert.NotNull(reporter);
        return reporter!;
    }

    private static Type? GetRenderTraceConfigurationInterface(object reporter) =>
        reporter.GetType().GetInterface(
            "PeerCast.Nova.Playback.IPlaybackWindowsAudioRenderTraceConfiguration");

    private static bool ReadBooleanProperty(
        object instance,
        Type interfaceType,
        string name)
    {
        var property = interfaceType.GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property.GetValue(instance));
    }
}
