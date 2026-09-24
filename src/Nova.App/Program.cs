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
    Environment.ExitCode = await NovaApplicationEntrypoint.RunDailyAsync(
        args,
        Console.Out,
        Console.Error,
        static (playerPath, cancellationToken) =>
            new NovaDailyPlaybackStartupComposition(playerPath).RunAsync(cancellationToken),
        cancellationSource.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
