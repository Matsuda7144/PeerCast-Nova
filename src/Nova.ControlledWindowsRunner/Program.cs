using PeerCast.Nova.Cli;

using var cancellationSource = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var evidence =
            await PlaybackControlledWindowsAudioRenderEvidenceCommand.RunAsync(
                args,
                Console.Out,
                cancellationSource.Token);
        Environment.ExitCode = evidence.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsDeliveryDiagnosticChildCaptureCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        Environment.ExitCode =
            await PlaybackControlledWindowsDeliveryDiagnosticChildCaptureCommand.RunAsync(
                args,
                System.Reflection.Assembly.GetEntryAssembly()?.Location ??
                    throw new InvalidOperationException(
                        "The controlled runner assembly path is unavailable."),
                Console.Out,
                cancellationSource.Token);
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsDeliveryDiagnosticCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var diagnostic =
            await PlaybackControlledWindowsDeliveryDiagnosticCommand.RunAsync(
                args,
                Console.Out,
                cancellationSource.Token);
        Environment.ExitCode = diagnostic.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsAudioChildCaptureCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        Environment.ExitCode =
            await PlaybackControlledWindowsAudioChildCaptureCommand.RunAsync(
                args,
                System.Reflection.Assembly.GetEntryAssembly()?.Location ??
                    throw new InvalidOperationException(
                        "The controlled runner assembly path is unavailable."),
                Console.Out,
                cancellationSource.Token);
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsDecodedAudioSliceProbeCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var probe =
            await PlaybackControlledWindowsDecodedAudioSliceProbeCommand.RunAsync(
                args,
                Console.Out,
                cancellationSource.Token);
        Environment.ExitCode = probe.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsKnownAudioToneProbeCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var probe = await PlaybackControlledWindowsKnownAudioToneProbeCommand.RunAsync(
            args,
            Console.Out,
            cancellationSource.Token);
        Environment.ExitCode = probe.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsTextureCopyProbeCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var probe = await PlaybackControlledWindowsTextureCopyProbeCommand.RunAsync(
            args,
            Console.Out,
            cancellationSource.Token);
        Environment.ExitCode = probe.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsKnownPatternProbeCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var probe = await PlaybackControlledWindowsKnownPatternProbeCommand.RunAsync(
            args,
            Console.Out,
            cancellationSource.Token);
        Environment.ExitCode = probe.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var preflight =
            await PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.RunAsync(
                args,
                Console.Out,
                cancellationSource.Token);
        Environment.ExitCode = preflight.ExitCode;
    }
    else if (Array.Exists(
            args,
            argument => string.Equals(
                argument,
                PlaybackControlledWindowsIndependentRunSetCommand.Argument,
                StringComparison.OrdinalIgnoreCase)))
    {
        var verification = await PlaybackControlledWindowsIndependentRunSetCommand.RunAsync(
            args,
            Console.Out,
            cancellationSource.Token);
        Environment.ExitCode = verification.ExitCode;
    }
    else
    {
        var run = await PlaybackControlledWindowsPhysicalRunCommand.RunAsync(
            args,
            Console.Out,
            cancellationSource.Token);
        Environment.ExitCode = run.ExitCode;
    }
}
catch (Exception)
{
    Console.Error.WriteLine(
        "PCN-CONTROLLED-RUNNER-FAILURE: ManagedUnhandledException");
    Console.Error.Flush();
    Environment.ExitCode = 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
