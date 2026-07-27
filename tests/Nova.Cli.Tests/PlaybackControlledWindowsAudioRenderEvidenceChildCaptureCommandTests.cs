using System.Diagnostics;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task Valid_render_evidence_is_promoted_without_other_records()
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
        Assert.False(File.Exists(environment.TerminalRecord));
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
    public async Task Valid_terminal_record_is_promoted_without_other_records()
    {
        using var environment = new CaptureEnvironment();
        var terminal = TerminalRecord();
        var executor = new RecordingExecutor(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.RunFailureExitCode,
            Encoding.UTF8.GetBytes(terminal),
            Encoding.UTF8.GetBytes("private stderr"));

        var result = await environment.RunAsync(executor);

        Assert.Equal(1, result);
        Assert.Equal(1, executor.CallCount);
        Assert.False(File.Exists(environment.EvidenceRecord));
        Assert.False(File.Exists(environment.FailureRecord));
        var retained = File.ReadAllText(environment.TerminalRecord);
        Assert.Equal(terminal, retained);
        Assert.True(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                retained,
                out var record));
        Assert.NotNull(record);
        Assert.True(record.PhysicalRunStarted);
        Assert.False(record.PhysicalRunCompleted);
        Assert.False(record.CompletedRenderEvidenceEstablished);
        Assert.False(record.AudioAcceptanceClaimed);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Contradictory_exit_and_record_promote_only_failure(
        int exitCode,
        bool terminalOutput)
    {
        using var environment = new CaptureEnvironment();
        var stdout = terminalOutput ? TerminalRecord() : EvidenceRecord();
        var executor = new RecordingExecutor(
            exitCode,
            Encoding.UTF8.GetBytes(stdout),
            Encoding.UTF8.GetBytes("private stderr"));

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        Assert.False(File.Exists(environment.EvidenceRecord));
        Assert.False(File.Exists(environment.TerminalRecord));
        Assert.True(File.Exists(environment.FailureRecord));
    }

    [Fact]
    public async Task Missing_optional_terminal_path_preserves_legacy_failure_behavior()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.RunFailureExitCode,
            Encoding.UTF8.GetBytes(TerminalRecord()),
            []);
        var options = environment.Options with { TerminalRecordPath = null };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(2, result);
        Assert.False(File.Exists(environment.EvidenceRecord));
        Assert.False(File.Exists(environment.TerminalRecord));
        Assert.True(File.Exists(environment.FailureRecord));
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
        Assert.False(File.Exists(environment.TerminalRecord));
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
    public async Task Child_failure_retains_only_last_allowlisted_native_stage()
    {
        using var environment = new CaptureEnvironment();
        var stderr = string.Join(
            Environment.NewLine,
            "private path C:\\secret",
            "PCN-AUDIO-NATIVE-STAGE: StartCompleted",
            "PCN-AUDIO-NATIVE-STAGE: ProductionCleanupEntering",
            "PCN-AUDIO-NATIVE-STAGE: PrivateStage");
        var executor = new RecordingExecutor(
            unchecked((int)0xC0000374),
            [],
            Encoding.UTF8.GetBytes(stderr));

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains(
            "Last-Native-Audio-Stage: ProductionCleanupEntering\n",
            failure);
        Assert.DoesNotContain("StartCompleted", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateStage", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Child_failure_retains_only_exact_allowlisted_controlled_run_result()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.RunFailureExitCode,
            Encoding.UTF8.GetBytes("Result: DeliveryRejected\r\n"),
            Encoding.UTF8.GetBytes("private stderr"));

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains("Controlled-Run-Result: DeliveryRejected\n", failure);
        Assert.DoesNotContain("private stderr", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Child_failure_retains_only_exact_allowlisted_terminal_record_rejection()
    {
        using var environment = new CaptureEnvironment();
        var stderr = string.Join(
            Environment.NewLine,
            "private stderr",
            "PCN-AUDIO-TERMINAL-RECORD-REJECTION: TraceCounterConsistencyRejected");
        var executor = new RecordingExecutor(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.RunFailureExitCode,
            Encoding.UTF8.GetBytes("Result: DeliveryRejected\r\n"),
            Encoding.UTF8.GetBytes(stderr));

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains(
            "Terminal-Record-Rejection: TraceCounterConsistencyRejected\n",
            failure);
        Assert.DoesNotContain("private stderr", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_exception_promotes_only_sanitized_failure()
    {
        using var environment = new CaptureEnvironment();
        var executor = new ThrowingExecutor();

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        Assert.False(File.Exists(environment.EvidenceRecord));
        Assert.False(File.Exists(environment.TerminalRecord));
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains("Child-Exit-Code-Available: False\n", failure);
        Assert.Contains("Child-Process-Result-Captured: False\n", failure);
        Assert.Contains("Stdout-Bytes: 0\n", failure);
        Assert.Contains("Stderr-Bytes: 0\n", failure);
        Assert.DoesNotContain("secret", failure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outcome_promotion_failure_falls_back_to_sanitized_failure()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            exitCode: 0,
            Encoding.UTF8.GetBytes(EvidenceRecord()),
            []);
        var promoter = new FirstPromotionFailsPromoter();

        var result = await environment.RunAsync(executor, promoter);

        Assert.Equal(2, result);
        Assert.Equal(2, promoter.CallCount);
        Assert.False(File.Exists(environment.EvidenceRecord));
        Assert.False(File.Exists(environment.TerminalRecord));
        Assert.True(File.Exists(environment.FailureRecord));
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
            TerminalRecordPath = Path.Combine(environment.RepositoryRoot, "terminal.txt"),
            FailureRecordPath = Path.Combine(environment.RepositoryRoot, "failure.txt"),
        };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.False(File.Exists(insideRepository));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Same_record_path_does_not_start_child(int duplicateKind)
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        var options = duplicateKind switch
        {
            0 => environment.Options with
            {
                TerminalRecordPath = environment.EvidenceRecord,
            },
            1 => environment.Options with
            {
                FailureRecordPath = environment.EvidenceRecord,
            },
            _ => environment.Options with
            {
                FailureRecordPath = environment.TerminalRecord,
            },
        };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.False(File.Exists(environment.EvidenceRecord));
        Assert.False(File.Exists(environment.TerminalRecord));
    }

    [Fact]
    public async Task Existing_terminal_record_does_not_start_child()
    {
        using var environment = new CaptureEnvironment();
        File.WriteAllText(environment.TerminalRecord, "existing");
        var executor = new RecordingExecutor(0, [], []);

        var result = await environment.RunAsync(executor);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal("existing", File.ReadAllText(environment.TerminalRecord));
    }

    [Fact]
    public void Argument_list_preserves_direct_render_evidence_child_shape()
    {
        var options = new PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions(
            @"C:\runtime with spaces",
            @"C:\fixture with spaces\controlled.flv",
            FixtureSha256,
            @"C:\records\render evidence.txt",
            @"C:\records\terminal render.txt",
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
        Assert.DoesNotContain(options.EvidenceRecordPath, startInfo.ArgumentList);
        Assert.DoesNotContain(options.TerminalRecordPath!, startInfo.ArgumentList);
        Assert.DoesNotContain(options.FailureRecordPath, startInfo.ArgumentList);
        Assert.DoesNotContain(
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand.Argument,
            startInfo.ArgumentList);
        Assert.DoesNotContain(
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task Command_rejects_duplicate_terminal_record_argument_before_executor()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        using var output = new StringWriter();
        var arguments = environment.Arguments.Concat(
            ["--terminal-render-record-path", environment.TerminalRecord]).ToArray();

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
    public async Task Command_reports_terminal_record_production()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.RunFailureExitCode,
            Encoding.UTF8.GetBytes(TerminalRecord()),
            []);
        using var output = new StringWriter();

        var result =
            await PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand.RunAsync(
                environment.Arguments,
                environment.RunnerAssembly,
                environment.RepositoryRoot,
                output,
                executor);

        Assert.Equal(1, result);
        Assert.Equal(
            "Result: TerminalRenderRecordProduced" + Environment.NewLine,
            output.ToString());
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

        Assert.StartsWith(
            "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 5\n",
            record,
            StringComparison.Ordinal);
        Assert.Contains("Milestone: 0.0.10d Phase 6\n", record);
        Assert.Contains("Child-Exit-Code-Available: True\n", record);
        Assert.Contains("Child-Exit-Code-Signed: -1073741819\n", record);
        Assert.Contains("Child-Exit-Code-Hex: C0000005\n", record);
        Assert.Contains("Stdout-Bytes: 17\n", record);
        Assert.Contains("Stderr-Bytes: 29\n", record);
        Assert.Contains("Last-Native-Audio-Stage: None\n", record);
        Assert.Contains("Controlled-Run-Result: None\n", record);
        Assert.Contains("Terminal-Record-Rejection: None\n", record);
        Assert.Contains("Canonical-Terminal-Render-Record-Produced: False\n", record);
        Assert.Contains("Child-Process-Result-Captured: True\n", record);
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

    private static string TerminalRecord()
    {
        var preCleanupAudio = default(PlaybackWindowsAudioRenderTraceSnapshot);
        var postStopAudio = preCleanupAudio with
        {
            PreStop = new(
                QueryCount: 1,
                Available: false,
                PaddingFrameCount: 0,
                Overflowed: false),
        };
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Faulted,
            PlaybackControlledWindowsPhysicalRunResult.DecodeRejected,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 1,
            DeliveryAttemptCount: 0,
            DeliveredCount: 0,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            EvidenceResult: default,
            BorrowedWindowPreserved: false,
            BorrowedDispatcherPreserved: false)
        {
            PreCleanupAudioRenderEvidence = new(
                DeliveryTrace: default,
                preCleanupAudio,
                EmptyNativeEvidence(preCleanupAudio)),
            PostStopAudioRenderEvidence = new(
                DeliveryTrace: default,
                postStopAudio,
                EmptyNativeEvidence(postStopAudio)),
        };
        return PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Create(
                snapshot,
                FixtureSha256,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                physicalRunStarted: true));
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

    private static PlaybackWindowsOutputNativeEvidenceSnapshot EmptyNativeEvidence(
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace) =>
        new(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            VideoPixelFormat: default,
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
            AudioSampleFormat: default,
            AudioSampleRate: 0,
            AudioChannelCount: 0,
            AudioGeneration: -1,
            AudioWriteAttemptCount: 0,
            AudioWriteAcknowledgementCount: 0,
            AudioAcceptedFrameCount: 0,
            AudioAcceptedByteCount: 0,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 0,
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

    private sealed class ThrowingExecutor :
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor
    {
        public ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
            string runnerAssemblyPath,
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"private InvalidOperationException path {runnerAssemblyPath} {options.FixturePath}");
    }

    private sealed class FirstPromotionFailsPromoter :
        IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter
    {
        private readonly SystemPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter _inner =
            new();

        public int CallCount { get; private set; }

        public ValueTask PromoteAsync(
            string destination,
            string content,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new IOException("private outcome promotion failure");
            }
            return _inner.PromoteAsync(destination, content, cancellationToken);
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
            TerminalRecord = Path.Combine(external, "terminal render.txt");
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
        public string TerminalRecord { get; }
        public string FailureRecord { get; }
        public string RunnerAssembly { get; }

        public PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions Options => new(
            RuntimeDirectory,
            FixturePath,
            FixtureSha256,
            EvidenceRecord,
            TerminalRecord,
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
            "--terminal-render-record-path",
            TerminalRecord,
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

        public ValueTask<int> RunAsync(
            IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
            IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter promoter,
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions? options = null)
        {
            var composition =
                new PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
                    executor,
                    promoter,
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
