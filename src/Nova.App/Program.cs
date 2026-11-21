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
        RunApplicationAsync,
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

static async ValueTask<NovaWindowsStartupResult> RunApplicationAsync(
    NovaPlaybackLaunchOptions playback,
    CancellationToken cancellationToken)
{
    if (playback is { Fmp4StreamUrl: { } streamUrl, LibMpvLibraryPath: { } libraryPath })
    {
        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("Embedded libmpv playback requires Windows.").ConfigureAwait(false);
            return NovaWindowsStartupResult.PlatformUnsupported;
        }

        return await NovaApplicationEntrypoint.PlayFmp4Async(
            streamUrl,
            new LibMpvPlaybackPlayerLauncher(libraryPath),
            Console.Error,
            cancellationToken).ConfigureAwait(false);
    }

    return await new NovaWindowsStartupComposition(new()
    {
        PlayerExecutablePath = playback.PlayerExecutablePath,
        LibMpvLibraryPath = playback.LibMpvLibraryPath,
    }).RunGenesisAsync(cancellationToken).ConfigureAwait(false);
}
