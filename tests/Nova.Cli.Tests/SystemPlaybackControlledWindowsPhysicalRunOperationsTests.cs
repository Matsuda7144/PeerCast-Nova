using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class SystemPlaybackControlledWindowsPhysicalRunOperationsTests
{
    [Theory]
    [InlineData(
        PlaybackControlledWindowsOutputValidationKind.Video,
        PlaybackSchedulerInitialOutputKind.Video)]
    [InlineData(
        PlaybackControlledWindowsOutputValidationKind.Audio,
        PlaybackSchedulerInitialOutputKind.Audio)]
    public void CreateLifecycleOptions_MapsControlledKindAndPreservesBoundedDropOldest(
        PlaybackControlledWindowsOutputValidationKind kind,
        PlaybackSchedulerInitialOutputKind expectedInitialOutputKind)
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
    }

    [Fact]
    public void ProductionSchedulerDefaultsRemainVideoAndDropIncoming()
    {
        var productionDefault = new PlaybackSchedulerOptions();
        Assert.Equal(
            PlaybackSchedulerInitialOutputKind.Video,
            productionDefault.InitialOutputKind);
        Assert.Equal(PlaybackSchedulerFullPolicy.DropIncoming, productionDefault.FullPolicy);
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

    private static void ReportStageByReflection(
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions options,
        string stageName)
    {
        var property = options.GetType().GetProperty(
            "AudioNativeStageReporter",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        var reporter = property.GetValue(options);
        Assert.NotNull(reporter);
        var method = reporter.GetType().GetMethod("Report");
        Assert.NotNull(method);
        var stageType = method.GetParameters()[0].ParameterType;
        var stage = Enum.Parse(stageType, stageName);
        method.Invoke(reporter, [stage]);
    }
}
