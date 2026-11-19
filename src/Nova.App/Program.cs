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
        static (playerPath, cancellationToken) =>
            new NovaWindowsStartupComposition(new() { PlayerExecutablePath = playerPath }).RunGenesisAsync(cancellationToken),
        cancellationSource.Token,
        static async (root, token) =>
        {
            await using var profile = await NovaValidationProfile.CreateSystemAsync(root, token);
            return await new NovaWindowsStartupComposition(new()
            {
                ValidationProfile = profile,
                ChannelCatalog = EmptyGenesisChannelCatalog.Instance,
                Host = new()
                {
                    ClientWidth = 1_120,
                    ClientHeight = 620,
                    Title = "PeerCast Nova — 検証専用（実ルーター・録画・再生なし）"
                },
            }).RunGenesisAsync(token);
        });
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
