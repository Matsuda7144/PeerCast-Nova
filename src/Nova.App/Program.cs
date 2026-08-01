using PeerCast.Nova.App;

using var cancellationSource = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    Environment.ExitCode = await NovaApplicationEntrypoint.RunAsync(
        args,
        Console.Out,
        Console.Error,
        static cancellationToken =>
            new NovaWindowsStartupComposition().RunAsync(cancellationToken),
        cancellationSource.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
