using System.Globalization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsTextureCopyProbeResult
{
    Completed,
    InvalidConfiguration,
    RunnerRejected,
    OutputInitializationRejected,
    PresentRejected,
    PreservationRejected,
    Cancelled,
    Faulted,
}

public readonly record struct PlaybackControlledWindowsTextureCopyProbeCommandResult(
    int ExitCode,
    PlaybackControlledWindowsTextureCopyProbeResult Result);

/// <summary>
/// Diagnostic comparison for the black-output boundary. It presents the same
/// generated BGRA quadrants through CreateTexture2D + CopyResource + Present,
/// bypassing the production UpdateSubresource upload call.
/// </summary>
public static class PlaybackControlledWindowsTextureCopyProbeCommand
{
    public const string Argument = "--controlled-windows-texture-copy-probe";
    private const int DefaultObservationHoldMilliseconds = 3_000;
    private const int MaximumObservationHoldMilliseconds = 30_000;

    public static async ValueTask<PlaybackControlledWindowsTextureCopyProbeCommandResult> RunAsync(
        string[] arguments,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var hold))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(2, PlaybackControlledWindowsTextureCopyProbeResult.InvalidConfiguration);
        }

        var runnerOptions = new PlaybackWindowsPhysicalRunnerOptions
        {
            ClientWidth = PlaybackControlledWindowsKnownPattern.Width,
            ClientHeight = PlaybackControlledWindowsKnownPattern.Height,
            Title = "PeerCast Nova Texture Copy Output Probe",
        };
        await using var runner = new PlaybackWindowsPhysicalRunnerHost(runnerOptions);
        PlaybackWindowsOutputActivationBoundary? activation = null;
        PlaybackWindowsOutputResourceBindingBoundary? binding = null;
        PlaybackWindowsOutputStreamInitializationBoundary? streams = null;
        PlaybackWindowsOutputTextureCopyProbeBoundary? textureCopy = null;
        var result = PlaybackControlledWindowsTextureCopyProbeResult.Faulted;
        var presentAttempts = 0L;
        var presentAcknowledgements = 0L;
        var acceptedBytes = 0L;
        var wouldBlock = 0L;
        var resize = 0L;
        var deviceLost = 0L;
        var outputCleanupCompleted = false;
        var borrowedWindowPreserved = false;
        var borrowedDispatcherPreserved = false;
        long windowToken = 0;

        try
        {
            var started = await runner.StartAsync(cancellationToken).ConfigureAwait(false);
            if (started != PlaybackWindowsPhysicalRunnerResult.Started)
            {
                result = started == PlaybackWindowsPhysicalRunnerResult.Cancelled
                    ? PlaybackControlledWindowsTextureCopyProbeResult.Cancelled
                    : PlaybackControlledWindowsTextureCopyProbeResult.RunnerRejected;
                return await CompleteAsync().ConfigureAwait(false);
            }

            var running = runner.Snapshot;
            windowToken = running.WindowToken;
            var initialized = await runner.InvokeAsync(
                async token =>
                {
                    if (!runner.HasAccess || runner.Snapshot.WindowToken <= 0)
                    {
                        return false;
                    }

                    activation = new PlaybackWindowsOutputActivationBoundary();
                    if (activation.Activate() != PlaybackWindowsOutputActivationResult.Activated)
                    {
                        return false;
                    }

                    binding = new PlaybackWindowsOutputResourceBindingBoundary(activation);
                    var bound = await binding.ConfigureAsync(
                        new(
                            PlaybackDecodedVideoPixelFormat.Bgra32,
                            PlaybackControlledWindowsKnownPattern.Width,
                            PlaybackControlledWindowsKnownPattern.Height,
                            PlaybackControlledWindowsKnownPattern.Stride,
                            1),
                        new(
                            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                            48_000,
                            2,
                            1),
                        token).ConfigureAwait(false);
                    if (bound != PlaybackWindowsOutputBindingResult.Bound)
                    {
                        return false;
                    }

                    streams = new PlaybackWindowsOutputStreamInitializationBoundary(
                        activation,
                        binding);
                    var stream = await streams.InitializeAsync(
                        new(
                            runner.Snapshot.WindowToken,
                            PlaybackControlledWindowsKnownPattern.Width,
                            PlaybackControlledWindowsKnownPattern.Height,
                            PlaybackDecodedVideoPixelFormat.Bgra32,
                            1),
                        new(
                            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                            48_000,
                            2,
                            TimeSpan.FromMilliseconds(100),
                            1),
                        token).ConfigureAwait(false);
                    if (stream != PlaybackWindowsOutputStreamInitializationResult.Initialized)
                    {
                        return false;
                    }

                    textureCopy = new PlaybackWindowsOutputTextureCopyProbeBoundary(
                        activation,
                        binding,
                        streams);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (initialized.Result != PlaybackWindowsOutputDispatcherInvocationResult.Completed ||
                !initialized.Value)
            {
                result = initialized.Result ==
                    PlaybackWindowsOutputDispatcherInvocationResult.Cancelled
                    ? PlaybackControlledWindowsTextureCopyProbeResult.Cancelled
                    : PlaybackControlledWindowsTextureCopyProbeResult.OutputInitializationRejected;
                return await CompleteAsync().ConfigureAwait(false);
            }

            presentAttempts = 1;
            var presented = await runner.InvokeAsync(
                token => textureCopy!.PresentAsync(
                    PlaybackControlledWindowsKnownPattern.Create(1),
                    token),
                cancellationToken).ConfigureAwait(false);
            if (presented.Result != PlaybackWindowsOutputDispatcherInvocationResult.Completed)
            {
                result = presented.Result == PlaybackWindowsOutputDispatcherInvocationResult.Cancelled
                    ? PlaybackControlledWindowsTextureCopyProbeResult.Cancelled
                    : PlaybackControlledWindowsTextureCopyProbeResult.PresentRejected;
                return await CompleteAsync().ConfigureAwait(false);
            }

            switch (presented.Value)
            {
                case PlaybackWindowsOutputTextureCopyProbeResult.Presented:
                    presentAcknowledgements = 1;
                    acceptedBytes = PlaybackControlledWindowsKnownPattern.ByteCount;
                    break;
                case PlaybackWindowsOutputTextureCopyProbeResult.WouldBlock:
                    wouldBlock = 1;
                    result = PlaybackControlledWindowsTextureCopyProbeResult.PresentRejected;
                    return await CompleteAsync().ConfigureAwait(false);
                case PlaybackWindowsOutputTextureCopyProbeResult.ResizeRequired:
                    resize = 1;
                    result = PlaybackControlledWindowsTextureCopyProbeResult.PresentRejected;
                    return await CompleteAsync().ConfigureAwait(false);
                case PlaybackWindowsOutputTextureCopyProbeResult.DeviceLost:
                    deviceLost = 1;
                    result = PlaybackControlledWindowsTextureCopyProbeResult.PresentRejected;
                    return await CompleteAsync().ConfigureAwait(false);
                case PlaybackWindowsOutputTextureCopyProbeResult.Cancelled:
                    result = PlaybackControlledWindowsTextureCopyProbeResult.Cancelled;
                    return await CompleteAsync().ConfigureAwait(false);
                default:
                    result = PlaybackControlledWindowsTextureCopyProbeResult.PresentRejected;
                    return await CompleteAsync().ConfigureAwait(false);
            }

            await Task.Delay(hold, cancellationToken).ConfigureAwait(false);
            await CleanupOutputAsync().ConfigureAwait(false);

            var before = runner.Snapshot;
            var preservation = await runner.InvokeAsync(
                _ => ValueTask.FromResult(runner.HasAccess),
                cancellationToken).ConfigureAwait(false);
            var after = runner.Snapshot;
            borrowedWindowPreserved = before.State == PlaybackWindowsPhysicalRunnerState.Running &&
                after.State == PlaybackWindowsPhysicalRunnerState.Running &&
                before.WindowToken == windowToken && after.WindowToken == windowToken &&
                windowToken > 0 && before.HasMessageLoop && after.HasMessageLoop &&
                !after.AdmissionClosed;
            borrowedDispatcherPreserved = preservation.Result ==
                PlaybackWindowsOutputDispatcherInvocationResult.Completed && preservation.Value;
            if (!outputCleanupCompleted || !borrowedWindowPreserved ||
                !borrowedDispatcherPreserved)
            {
                result = PlaybackControlledWindowsTextureCopyProbeResult.PreservationRejected;
                return await CompleteAsync().ConfigureAwait(false);
            }

            result = PlaybackControlledWindowsTextureCopyProbeResult.Completed;
            return await CompleteAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = PlaybackControlledWindowsTextureCopyProbeResult.Cancelled;
            return await CompleteAsync().ConfigureAwait(false);
        }
        catch
        {
            result = PlaybackControlledWindowsTextureCopyProbeResult.Faulted;
            return await CompleteAsync().ConfigureAwait(false);
        }

        async ValueTask<PlaybackControlledWindowsTextureCopyProbeCommandResult> CompleteAsync()
        {
            await CleanupOutputAsync().ConfigureAwait(false);
            await runner.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await WriteRecordAsync(
                output,
                result,
                presentAttempts,
                presentAcknowledgements,
                acceptedBytes,
                wouldBlock,
                resize,
                deviceLost,
                runner.Snapshot.State,
                outputCleanupCompleted,
                borrowedWindowPreserved,
                borrowedDispatcherPreserved).ConfigureAwait(false);
            var exit = result == PlaybackControlledWindowsTextureCopyProbeResult.Completed ? 0 : 1;
            return new(exit, result);
        }

        async ValueTask CleanupOutputAsync()
        {
            if (textureCopy is null && streams is null && binding is null && activation is null)
            {
                return;
            }

            var cleanup = await runner.InvokeAsync(
                _ =>
                {
                    textureCopy?.Dispose();
                    streams?.Dispose();
                    binding?.Dispose();
                    activation?.Dispose();
                    textureCopy = null;
                    streams = null;
                    binding = null;
                    activation = null;
                    return ValueTask.FromResult(true);
                },
                CancellationToken.None).ConfigureAwait(false);
            outputCleanupCompleted = cleanup.Result ==
                PlaybackWindowsOutputDispatcherInvocationResult.Completed && cleanup.Value;
        }
    }

    internal static bool TryParse(string[] arguments, out TimeSpan hold)
    {
        hold = TimeSpan.FromMilliseconds(DefaultObservationHoldMilliseconds);
        var optionIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(
                argument,
                "--observation-hold-ms",
                StringComparison.OrdinalIgnoreCase));
        if (optionIndex < 0)
        {
            return true;
        }

        if (optionIndex + 1 >= arguments.Length ||
            !int.TryParse(
                arguments[optionIndex + 1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var milliseconds) ||
            milliseconds < 0 || milliseconds > MaximumObservationHoldMilliseconds)
        {
            return false;
        }

        hold = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private static async ValueTask WriteRecordAsync(
        TextWriter output,
        PlaybackControlledWindowsTextureCopyProbeResult result,
        long presentAttempts,
        long presentAcknowledgements,
        long acceptedBytes,
        long wouldBlock,
        long resize,
        long deviceLost,
        PlaybackWindowsPhysicalRunnerState runnerState,
        bool outputCleanupCompleted,
        bool borrowedWindowPreserved,
        bool borrowedDispatcherPreserved)
    {
        await output.WriteLineAsync(
            "PEERCAST-NOVA-CONTROLLED-WINDOWS-TEXTURE-COPY-PROBE 1").ConfigureAwait(false);
        await output.WriteLineAsync("Milestone: 0.0.10d Phase 5").ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Pattern: {PlaybackControlledWindowsKnownPattern.Id}").ConfigureAwait(false);
        await output.WriteLineAsync("Upload-Path: CreateTexture2D+CopyResource").ConfigureAwait(false);
        await output.WriteLineAsync($"Width: {PlaybackControlledWindowsKnownPattern.Width}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Height: {PlaybackControlledWindowsKnownPattern.Height}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Stride: {PlaybackControlledWindowsKnownPattern.Stride}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Non-Black-Pixels: {PlaybackControlledWindowsKnownPattern.NonBlackPixelCount}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Present-Attempts: {presentAttempts}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Present-Acknowledgements: {presentAcknowledgements}").ConfigureAwait(false);
        await output.WriteLineAsync($"Accepted-Bytes: {acceptedBytes}").ConfigureAwait(false);
        await output.WriteLineAsync($"Present-WouldBlock: {wouldBlock}").ConfigureAwait(false);
        await output.WriteLineAsync($"Resize-Count: {resize}").ConfigureAwait(false);
        await output.WriteLineAsync($"Device-Lost-Count: {deviceLost}").ConfigureAwait(false);
        await output.WriteLineAsync($"Runner-State: {runnerState}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Output-Cleanup-Completed: {outputCleanupCompleted}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Borrowed-Window-Preserved: {borrowedWindowPreserved}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Borrowed-Dispatcher-Preserved: {borrowedDispatcherPreserved}").ConfigureAwait(false);
        await output.WriteLineAsync("FFmpeg-Runtime-Used: False").ConfigureAwait(false);
        await output.WriteLineAsync("Production-Upload-Path-Used: False").ConfigureAwait(false);
        await output.WriteLineAsync("External-Media-Or-Payload-Published: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-TEXTURE-COPY-PROBE")
            .ConfigureAwait(false);
    }
}
