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
    var composition = new NovaWindowsStartupComposition();
    var result = await composition.RunAsync(cancellationSource.Token);
    Environment.ExitCode = result switch
    {
        NovaWindowsStartupResult.Completed => 0,
        NovaWindowsStartupResult.Cancelled => 0,
        NovaWindowsStartupResult.PlatformUnsupported => 2,
        _ => 1,
    };
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
