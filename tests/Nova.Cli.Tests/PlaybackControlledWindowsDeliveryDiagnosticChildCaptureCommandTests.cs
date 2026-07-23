using System.Diagnostics;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsDeliveryDiagnosticChildCaptureCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task Valid_failed_run_diagnostic_is_promoted_as_utf8_without_failure_record()
    {
        using var environment = new CaptureEnvironment();
        var diagnostic = DiagnosticRecord(
            PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected,
            [
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult
                    .RetainedAudioRecoveryRequired,
            ]);
        var executor = new RecordingExecutor(
            exitCode: 1,
            Encoding.UTF8.GetBytes(diagnostic),
            Encoding.UTF8.GetBytes("private stderr"));

        var result = await environment.RunAsync(executor);

        Assert.Equal(1, result);
        Assert.Equal(1, executor.CallCount);
        Assert.False(File.Exists(environment.FailureRecord));
        var bytes = File.ReadAllBytes(environment.DiagnosticRecord);
        Assert.Equal(Encoding.UTF8.GetBytes(diagnostic), bytes);
        Assert.True(
            PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.TryParse(
                Encoding.UTF8.GetString(bytes),
                out var record));
        Assert.NotNull(record);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult
                .RetainedAudioRecoveryRequired,
            record.DeliveryResults[1]);
        Assert.False(record.AudioAcceptanceClaimed);
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
        Assert.False(File.Exists(environment.DiagnosticRecord));
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains("Result: DiagnosticRecordNotProduced\n", failure);
        Assert.Contains("Stdout-Bytes: 2\n", failure);
        Assert.Contains("Raw-Stdout-Published: False\n", failure);
        Assert.Contains("Raw-Stderr-Published: False\n", failure);
        Assert.DoesNotContain("secret", failure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw error", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_owned_record_paths_do_not_start_child()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        var insideRepository = Path.Combine(environment.RepositoryRoot, "diagnostic.txt");
        var options = environment.Options with
        {
            DiagnosticRecordPath = insideRepository,
            FailureRecordPath = Path.Combine(environment.RepositoryRoot, "failure.txt"),
        };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.False(File.Exists(insideRepository));
    }

    [Fact]
    public void Argument_list_preserves_exact_diagnostic_child_shape()
    {
        var options = new PlaybackControlledWindowsDeliveryDiagnosticChildCaptureOptions(
            @"C:\runtime with spaces",
            @"C:\fixture with spaces\controlled.flv",
            FixtureSha256,
            @"C:\records\delivery diagnostic.txt",
            @"C:\records\delivery failure.txt",
            128,
            128,
            65_536,
            1_000);
        var startInfo = new ProcessStartInfo();

        SystemPlaybackControlledWindowsDeliveryDiagnosticChildProcessExecutor.AddArguments(
            startInfo,
            @"C:\runner with spaces\runner.dll",
            options);

        Assert.Equal(@"C:\runner with spaces\runner.dll", startInfo.ArgumentList[0]);
        Assert.Contains(
            PlaybackControlledWindowsDeliveryDiagnosticCommand.Argument,
            startInfo.ArgumentList);
        Assert.Contains(
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
            startInfo.ArgumentList);
        Assert.Contains(@"C:\runtime with spaces", startInfo.ArgumentList);
        Assert.Contains(@"C:\fixture with spaces\controlled.flv", startInfo.ArgumentList);
        Assert.DoesNotContain(
            PlaybackControlledWindowsDeliveryDiagnosticChildCaptureCommand.Argument,
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task Command_rejects_duplicate_record_argument_before_executor()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, [], []);
        using var output = new StringWriter();
        var arguments = environment.Arguments.Concat(
            ["--diagnostic-record-path", environment.DiagnosticRecord]).ToArray();

        var result =
            await PlaybackControlledWindowsDeliveryDiagnosticChildCaptureCommand.RunAsync(
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
            PlaybackControlledWindowsDeliveryDiagnosticChildCaptureComposition
                .SerializeFailureRecord(
                    unchecked((int)0xC0000005),
                    stdoutByteCount: 17,
                    stderrByteCount: 29);

        Assert.Contains("Child-Exit-Code-Signed: -1073741819\n", record);
        Assert.Contains("Child-Exit-Code-Hex: C0000005\n", record);
        Assert.Contains("Stdout-Bytes: 17\n", record);
        Assert.Contains("Stderr-Bytes: 29\n", record);
        Assert.Contains("External-Binary-Or-Payload-Published: False\n", record);
        Assert.DoesNotContain("Path", record, StringComparison.Ordinal);
        Assert.DoesNotContain("HRESULT", record, StringComparison.OrdinalIgnoreCase);
    }

    private static string DiagnosticRecord(
        PlaybackControlledWindowsPhysicalRunResult runResult,
        IReadOnlyList<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> results)
    {
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Faulted,
            runResult,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: results.Count + 1,
            DeliveryAttemptCount: results.Count,
            DeliveredCount: results.Count(value =>
                value == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered),
            RetainedWouldBlockCount: results.Count(value =>
                value == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock),
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            PlaybackControlledWindowsOutputEvidenceResult.None,
            BorrowedWindowPreserved: false,
            BorrowedDispatcherPreserved: false);
        return PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.Serialize(
            PlaybackControlledWindowsDeliveryDiagnosticRecordCodec.Create(
                snapshot,
                FixtureSha256,
                results,
                deliveryResultOverflowed: false));
    }

    private sealed class RecordingExecutor(
        int exitCode,
        byte[] standardOutput,
        byte[] standardError) :
        IPlaybackControlledWindowsDeliveryDiagnosticChildProcessExecutor
    {
        public int CallCount { get; private set; }

        public ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
            string runnerAssemblyPath,
            PlaybackControlledWindowsDeliveryDiagnosticChildCaptureOptions options,
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
            $"pcn-delivery-diagnostic-capture-tests-{Guid.NewGuid():N}");

        public CaptureEnvironment()
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            var external = Path.Combine(_root, "external with spaces");
            RuntimeDirectory = Path.Combine(external, "runtime");
            FixturePath = Path.Combine(external, "fixture.flv");
            DiagnosticRecord = Path.Combine(external, "delivery diagnostic.txt");
            FailureRecord = Path.Combine(external, "delivery failure.txt");
            RunnerAssembly = Path.Combine(RepositoryRoot, "runner.dll");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(RuntimeDirectory);
            File.WriteAllText(RunnerAssembly, "runner");
            File.WriteAllText(FixturePath, "fixture");
        }

        public string RepositoryRoot { get; }
        public string RuntimeDirectory { get; }
        public string FixturePath { get; }
        public string DiagnosticRecord { get; }
        public string FailureRecord { get; }
        public string RunnerAssembly { get; }

        public PlaybackControlledWindowsDeliveryDiagnosticChildCaptureOptions Options => new(
            RuntimeDirectory,
            FixturePath,
            FixtureSha256,
            DiagnosticRecord,
            FailureRecord,
            128,
            128,
            65_536,
            1_000);

        public string[] Arguments =>
        [
            PlaybackControlledWindowsDeliveryDiagnosticChildCaptureCommand.Argument,
            "--runtime-directory",
            RuntimeDirectory,
            "--controlled-fixture",
            FixturePath,
            "--fixture-sha256",
            FixtureSha256,
            "--diagnostic-record-path",
            DiagnosticRecord,
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
            IPlaybackControlledWindowsDeliveryDiagnosticChildProcessExecutor executor,
            PlaybackControlledWindowsDeliveryDiagnosticChildCaptureOptions? options = null)
        {
            var composition =
                new PlaybackControlledWindowsDeliveryDiagnosticChildCaptureComposition(
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
