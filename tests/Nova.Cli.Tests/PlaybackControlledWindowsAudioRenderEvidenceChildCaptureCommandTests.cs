using System.Diagnostics;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task Valid_render_evidence_is_promoted_without_failure_record()
    {
        using var environment = new CaptureEnvironment();
        var evidence = EvidenceRecord();
        var executor = new RecordingExecutor(
            exitCode: 0,
            Encoding.UTF8.GetBytes(evidence),
            Encoding.UTF8.GetBytes("private stderr"));

        var result = await environment.RunAsync(executor);

        Assert.Equal(0, result);
        Assert.Equal(1, executor.CallCount);
        Assert.False(File.Exists(environment.FailureRecord));
        var bytes = File.ReadAllBytes(environment.EvidenceRecord);
        Assert.Equal(Encoding.UTF8.GetBytes(evidence), bytes);
        Assert.True(
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.TryParse(
                Encoding.UTF8.GetString(bytes),
                out var record));
        Assert.NotNull(record);
        Assert.True(record.PhysicalRunsExecutedByThisCommand);
        Assert.False(record.AudioAcceptanceClaimed);
        Assert.Equal(1, record.PostStopAudioRenderTrace.PreStop.QueryCount);
    }

    [Fact]
    public async Task Invalid_utf8_promotes_only_sanitized_failure_without_raw_output()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            exitCode: 9,
            [0xC3, 0x28],
            Encoding.UTF8.GetBytes("private path C:\\secret and raw error"));

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        Assert.False(File.Exists(environment.EvidenceRecord));
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains("Result: RenderEvidenceRecordNotProduced\n", failure);
        Assert.Contains("Stdout-Bytes: 2\n", failure);
        Assert.Contains("Raw-Stdout-Published: False\n", failure);
        Assert.Contains("Raw-Stderr-Published: False\n", failure);
        Assert.Contains("Physical-Run-Completion-Established: False\n", failure);
        Assert.DoesNotContain("secret", failure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw error", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_owned_record_paths_do_not_start_child()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        var insideRepository = Path.Combine(environment.RepositoryRoot, "evidence.txt");
        var options = environment.Options with
        {
            EvidenceRecordPath = insideRepository,
            FailureRecordPath = Path.Combine(environment.RepositoryRoot, "failure.txt"),
        };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.False(File.Exists(insideRepository));
    }

    [Fact]
    public async Task Same_record_path_does_not_start_child()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        var options = environment.Options with
        {
            FailureRecordPath = environment.EvidenceRecord,
        };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.False(File.Exists(environment.EvidenceRecord));
    }

    [Fact]
    public void Argument_list_preserves_direct_render_evidence_child_shape()
    {
        var options = new PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions(
            @"C:\runtime with spaces",
            @"C:\fixture with spaces\controlled.flv",
            FixtureSha256,
            @"C:\records\render evidence.txt",
            @"C:\records\render failure.txt",
            128,
            128,
            65_536,
            1_000);
        var startInfo = new ProcessStartInfo();

        SystemPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor.AddArguments(
            startInfo,
            @"C:\runner with spaces\runner.dll",
            options);

        Assert.Equal(@"C:\runner with spaces\runner.dll", startInfo.ArgumentList[0]);
        Assert.Contains(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
            startInfo.ArgumentList);
        Assert.Contains(@"C:\runtime with spaces", startInfo.ArgumentList);
        Assert.Contains(@"C:\fixture with spaces\controlled.flv", startInfo.ArgumentList);
        Assert.DoesNotContain(
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand.Argument,
            startInfo.ArgumentList);
        Assert.DoesNotContain(
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task Command_rejects_duplicate_evidence_record_argument_before_executor()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        using var output = new StringWriter();
        var arguments = environment.Arguments.Concat(
            ["--render-evidence-record-path", environment.EvidenceRecord]).ToArray();

        var result =
            await PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand.RunAsync(
                arguments,
                environment.RunnerAssembly,
                environment.RepositoryRoot,
                output,
                executor);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal("Result: InvalidConfiguration" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Failure_record_contains_only_bounded_sanitized_fields()
    {
        var record =
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition
                .SerializeFailureRecord(
                    unchecked((int)0xC0000005),
                    stdoutByteCount: 17,
                    stderrByteCount: 29);

        Assert.Contains("Child-Exit-Code-Signed: -1073741819\n", record);
        Assert.Contains("Child-Exit-Code-Hex: C0000005\n", record);
        Assert.Contains("Stdout-Bytes: 17\n", record);
        Assert.Contains("Stderr-Bytes: 29\n", record);
        Assert.Contains("Audio-Acceptance-Claimed: False\n", record);
        Assert.Contains("External-Binary-Or-Payload-Published: False\n", record);
        Assert.DoesNotContain("Path", record, StringComparison.Ordinal);
        Assert.DoesNotContain("HRESULT", record, StringComparison.OrdinalIgnoreCase);
    }

    private static string EvidenceRecord()
    {
        var holdStart = Checkpoint(includePreStop: false);
        var preCleanup = Checkpoint(includePreStop: false);
        var postStop = Checkpoint(includePreStop: true);
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Completed,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 4,
            DeliveryAttemptCount: 4,
            DeliveredCount: 4,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true)
        {
            HoldStartAudioRenderEvidence = holdStart,
            PreCleanupAudioRenderEvidence = preCleanup,
            PostStopAudioRenderEvidence = postStop,
        };
        return PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
                snapshot,
                FixtureSha256,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                physicalRunsExecutedByThisCommand: true));
    }

    private static PlaybackControlledWindowsAudioRenderEvidenceSnapshot Checkpoint(
        bool includePreStop)
    {
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery = includePreStop
            ? default
            : new(
                Count: 4,
                Overflowed: false,
                First: Delivery(0, 0),
                Second: Delivery(1, 21),
                Third: Delivery(2, 42),
                Fourth: Delivery(3, 64));
        var audio = new PlaybackWindowsAudioRenderTraceSnapshot(
            Count: 4,
            Overflowed: false,
            First: Write(0, startRequested: true),
            Second: Write(1024, startRequested: false),
            Third: Write(2048, startRequested: false),
            Fourth: Write(3072, startRequested: false),
            PreStop: includePreStop
                ? new(
                    QueryCount: 1,
                    Available: true,
                    PaddingFrameCount: 0,
                    Overflowed: false)
                : default);
        return new(delivery, audio, NativeEvidence(audio));
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation Delivery(
        long sequence,
        int presentationMilliseconds) =>
        new(
            sequence,
            PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            TimeSpan.FromMilliseconds(presentationMilliseconds).Ticks,
            HasTimingDecision: true,
            PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);

    private static PlaybackWindowsAudioRenderWriteObservation Write(
        int paddingFrames,
        bool startRequested) =>
        new(
            RequestedFrameCount: 1024,
            RequestedByteCount: 4096,
            HasCapacityFrameCount: true,
            CapacityFrameCount: 4800,
            HasPaddingFrameCount: true,
            PaddingFrameCount: paddingFrames,
            HasAvailableFrameCount: true,
            AvailableFrameCount: 4800 - paddingFrames,
            StartRequested: startRequested,
            GetBufferAcknowledged: true,
            ReleaseBufferAcknowledged: true,
            StartAcknowledged: startRequested,
            PlaybackWindowsAudioRenderWriteResult.Processed);

    private static PlaybackWindowsOutputNativeEvidenceSnapshot NativeEvidence(
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace) =>
        new(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: 0,
            VideoHeight: 0,
            VideoStride: 0,
            VideoGeneration: -1,
            VideoPresentAttemptCount: 0,
            VideoPresentAcknowledgementCount: 0,
            VideoAcceptedByteCount: 0,
            VideoWouldBlockCount: 0,
            VideoResizeCount: 0,
            VideoDeviceLostCount: 0,
            AudioConfigurationConflict: false,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            AudioGeneration: 7,
            AudioWriteAttemptCount: 4,
            AudioWriteAcknowledgementCount: 4,
            AudioAcceptedFrameCount: 4_096,
            AudioAcceptedByteCount: 16_384,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 1,
            AudioDeviceLostCount: 0)
        {
            AudioRenderTrace = audioTrace,
        };

    private sealed class RecordingExecutor(
        int exitCode,
        byte[] standardOutput,
        byte[] standardError) :
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor
    {
        public int CallCount { get; private set; }

        public ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
            string runnerAssemblyPath,
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
            CancellationToken cancellationToken)
        {
            _ = runnerAssemblyPath;
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(new PlaybackControlledWindowsAudioChildProcessResult(
                exitCode,
                standardOutput,
                standardError));
        }
    }

    private sealed class CaptureEnvironment : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"pcn-render-evidence-capture-tests-{Guid.NewGuid():N}");

        public CaptureEnvironment()
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            var external = Path.Combine(_root, "external with spaces");
            RuntimeDirectory = Path.Combine(external, "runtime");
            FixturePath = Path.Combine(external, "fixture.flv");
            EvidenceRecord = Path.Combine(external, "render evidence.txt");
            FailureRecord = Path.Combine(external, "render failure.txt");
            RunnerAssembly = Path.Combine(RepositoryRoot, "runner.dll");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(RuntimeDirectory);
            File.WriteAllText(RunnerAssembly, "runner");
            File.WriteAllText(FixturePath, "fixture");
        }

        public string RepositoryRoot { get; }
        public string RuntimeDirectory { get; }
        public string FixturePath { get; }
        public string EvidenceRecord { get; }
        public string FailureRecord { get; }
        public string RunnerAssembly { get; }

        public PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions Options => new(
            RuntimeDirectory,
            FixturePath,
            FixtureSha256,
            EvidenceRecord,
            FailureRecord,
            128,
            128,
            65_536,
            1_000);

        public string[] Arguments =>
        [
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand.Argument,
            "--runtime-directory",
            RuntimeDirectory,
            "--controlled-fixture",
            FixturePath,
            "--fixture-sha256",
            FixtureSha256,
            "--render-evidence-record-path",
            EvidenceRecord,
            "--failure-record-path",
            FailureRecord,
            "--max-decode-operations",
            "128",
            "--max-delivery-operations",
            "128",
            "--fixture-packet-bytes",
            "65536",
            "--observation-hold-ms",
            "1000",
        ];

        public ValueTask<int> RunAsync(
            IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions? options = null)
        {
            var composition =
                new PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
                    executor,
                    RunnerAssembly,
                    RepositoryRoot);
            return composition.RunAsync(options ?? Options, CancellationToken.None);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
