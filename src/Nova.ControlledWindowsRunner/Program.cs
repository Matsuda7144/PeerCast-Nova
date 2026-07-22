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
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
