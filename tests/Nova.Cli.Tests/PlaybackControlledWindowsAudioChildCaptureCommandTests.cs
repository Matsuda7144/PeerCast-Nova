using System.Diagnostics;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioChildCaptureCommandTests
{
    private const string RuntimeBuild =
        "btbn.ffmpeg.20260721.win64.lgpl-shared";
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task CanonicalSuccessPromotesOnlyAudioRecord()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            0,
            CanonicalRecord(),
            "private stderr must not escape");

        var result = await environment.RunAsync(executor);

        Assert.Equal(0, result);
        Assert.Equal(1, executor.CallCount);
        Assert.Equal(CanonicalRecord(), File.ReadAllText(environment.AudioRecord));
        Assert.False(File.Exists(environment.FailureRecord));
    }

    [Fact]
    public async Task CanonicalChildFailurePromotesAudioRecordAndReturnsOne()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(7, CanonicalRecord(), string.Empty);

        var result = await environment.RunAsync(executor);

        Assert.Equal(1, result);
        Assert.True(File.Exists(environment.AudioRecord));
        Assert.False(File.Exists(environment.FailureRecord));
    }

    [Fact]
    public async Task AccessViolationWithoutStdoutPromotesOnlySanitizedFailure()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            unchecked((int)0xC0000005),
            string.Empty,
            "PCN-AUDIO-NATIVE-STAGE: GetBufferEntering\nprivate raw stderr");

        var result = await environment.RunAsync(executor);

        Assert.Equal(2, result);
        Assert.False(File.Exists(environment.AudioRecord));
        var failure = File.ReadAllText(environment.FailureRecord);
        Assert.Contains("Child-Exit-Code-Signed: -1073741819\n", failure);
        Assert.Contains("Child-Exit-Code-Hex: C0000005\n", failure);
        Assert.Contains("Stdout-Bytes: 0\n", failure);
        Assert.Contains("Last-Native-Audio-Stage: GetBufferEntering\n", failure);
        Assert.DoesNotContain("private raw stderr", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidOrRepositoryOwnedPathsNeverStartChild()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, CanonicalRecord(), string.Empty);
        var insideRepository = Path.Combine(
            environment.RepositoryRoot,
            "record.txt");
        var options = environment.Options with
        {
            AudioRecordPath = insideRepository,
            FailureRecordPath = insideRepository,
        };

        var result = await environment.RunAsync(executor, options);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.False(File.Exists(insideRepository));
    }

    [Fact]
    public async Task FailedAtomicPromotionLeavesNoIncompleteFinalRecord()
    {
        using var environment = new CaptureEnvironment();
        Directory.CreateDirectory(environment.AudioRecord);
        var executor = new RecordingExecutor(0, CanonicalRecord(), string.Empty);

        var result = await environment.RunAsync(executor);

        Assert.Equal(3, result);
        Assert.False(File.Exists(environment.AudioRecord));
        Assert.Empty(
            Directory.GetFiles(
                Path.GetDirectoryName(environment.AudioRecord)!,
                ".pcn-audio-capture-*.tmp"));
    }

    [Fact]
    public void ArgumentListPreservesPathsWithWhitespaceAndFixedChildShape()
    {
        var options = new PlaybackControlledWindowsAudioChildCaptureOptions(
            @"C:\runtime with spaces",
            @"C:\fixture with spaces\controlled.flv",
            FixtureSha256,
            @"C:\records\audio record.txt",
            @"C:\records\failure record.txt",
            128,
            128,
            65_536,
            1_000);
        var startInfo = new ProcessStartInfo();

        SystemPlaybackControlledWindowsAudioChildProcessExecutor.AddArguments(
            startInfo,
            @"C:\runner with spaces\runner.dll",
            options);

        Assert.Equal(@"C:\runner with spaces\runner.dll", startInfo.ArgumentList[0]);
        Assert.Contains(
            @"C:\runtime with spaces",
            startInfo.ArgumentList);
        Assert.Contains(
            @"C:\fixture with spaces\controlled.flv",
            startInfo.ArgumentList);
        Assert.Contains(
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
            startInfo.ArgumentList);
        Assert.Equal(
            1,
            startInfo.ArgumentList.Count(argument =>
                string.Equals(
                    argument,
                    PlaybackControlledWindowsPhysicalRunCommand.Argument,
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CommandRejectsMissingMandatoryInputsBeforeExecutor()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(0, CanonicalRecord(), string.Empty);
        using var output = new StringWriter();

        var result = await PlaybackControlledWindowsAudioChildCaptureCommand.RunAsync(
            [PlaybackControlledWindowsAudioChildCaptureCommand.Argument],
            environment.RunnerAssembly,
            environment.RepositoryRoot,
            output,
            executor);

        Assert.Equal(3, result);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal($"Result: InvalidConfiguration{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task CommandDispatchRetainsSanitizedFailureAndReturnsTwo()
    {
        using var environment = new CaptureEnvironment();
        var executor = new RecordingExecutor(
            9,
            string.Empty,
            PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .ManagedUnhandledExceptionMarker);
        using var output = new StringWriter();

        var result = await PlaybackControlledWindowsAudioChildCaptureCommand.RunAsync(
            environment.Arguments,
            environment.RunnerAssembly,
            environment.RepositoryRoot,
            output,
            executor);

        Assert.Equal(2, result);
        Assert.Equal(1, executor.CallCount);
        Assert.False(File.Exists(environment.AudioRecord));
        Assert.True(File.Exists(environment.FailureRecord));
        Assert.Equal(
            $"Result: SanitizedFailureRecordProduced{Environment.NewLine}",
            output.ToString());
    }

    [Fact]
    public async Task RedirectedLargeOutputOnBothStreamsDoesNotDeadlock()
    {
        var startInfo = CreateLargeOutputProcessStartInfo();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result =
            await SystemPlaybackControlledWindowsAudioChildProcessExecutor
                .ExecuteProcessAsync(startInfo, timeout.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StandardOutput.LongLength >= 1_000_000);
        Assert.True(result.StandardError.LongLength >= 1_000_000);
    }

    private static string CanonicalRecord() =>
        PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(new(
            1,
            "0.0.10d Phase 5",
            PlaybackControlledWindowsOutputValidationKind.Audio,
            RuntimeBuild,
            FixtureSha256,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            66_939,
            2,
            2,
            1,
            1,
            0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            true,
            true,
            false));

    private static ProcessStartInfo CreateLargeOutputProcessStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "powershell.exe";
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "[Console]::Error.Write(('e' * 1000000));" +
                "[Console]::Out.Write(('o' * 1000000))");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                "head -c 1000000 /dev/zero >&2; head -c 1000000 /dev/zero");
        }

        return startInfo;
    }

    private sealed class RecordingExecutor(
        int exitCode,
        string standardOutput,
        string standardError) :
        IPlaybackControlledWindowsAudioChildProcessExecutor
    {
        public int CallCount { get; private set; }

        public ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
            string runnerAssemblyPath,
            PlaybackControlledWindowsAudioChildCaptureOptions options,
            CancellationToken cancellationToken)
        {
            _ = runnerAssemblyPath;
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(new PlaybackControlledWindowsAudioChildProcessResult(
                exitCode,
                Encoding.UTF8.GetBytes(standardOutput),
                Encoding.UTF8.GetBytes(standardError)));
        }
    }

    private sealed class CaptureEnvironment : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"pcn-audio-capture-tests-{Guid.NewGuid():N}");

        public CaptureEnvironment()
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            var external = Path.Combine(_root, "external with spaces");
            RuntimeDirectory = Path.Combine(external, "runtime");
            FixturePath = Path.Combine(external, "fixture.flv");
            AudioRecord = Path.Combine(external, "audio record.txt");
            FailureRecord = Path.Combine(external, "failure record.txt");
            RunnerAssembly = Path.Combine(RepositoryRoot, "runner.dll");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(RuntimeDirectory);
            File.WriteAllText(RunnerAssembly, "runner");
            File.WriteAllText(FixturePath, "fixture");
        }

        public string RepositoryRoot { get; }
        public string RuntimeDirectory { get; }
        public string FixturePath { get; }
        public string AudioRecord { get; }
        public string FailureRecord { get; }
        public string RunnerAssembly { get; }

        public PlaybackControlledWindowsAudioChildCaptureOptions Options => new(
            RuntimeDirectory,
            FixturePath,
            FixtureSha256,
            AudioRecord,
            FailureRecord,
            128,
            128,
            65_536,
            1_000);

        public string[] Arguments =>
        [
            PlaybackControlledWindowsAudioChildCaptureCommand.Argument,
            "--runtime-directory",
            RuntimeDirectory,
            "--controlled-fixture",
            FixturePath,
            "--fixture-sha256",
            FixtureSha256,
            "--audio-record-path",
            AudioRecord,
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
            IPlaybackControlledWindowsAudioChildProcessExecutor executor,
            PlaybackControlledWindowsAudioChildCaptureOptions? options = null)
        {
            var composition = new PlaybackControlledWindowsAudioChildCaptureComposition(
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
