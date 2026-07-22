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
    var result = await PlaybackControlledWindowsPhysicalRunCommand.RunAsync(
        args,
        Console.Out,
        cancellationSource.Token);
    Environment.ExitCode = result.ExitCode;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
