using PeerCast.Nova.App;
using PeerCast.Nova.Foundation.Logging;

UnhandledExceptionEventHandler unhandledHandler = (_, eventArgs) =>
{
    FileNovaLogger.Default.Log(
        LogLevel.Critical,
        $"Unhandled AppDomain exception (IsTerminating={eventArgs.IsTerminating}).",
        eventArgs.ExceptionObject as Exception);
};
EventHandler<UnobservedTaskExceptionEventArgs> unobservedHandler = (_, eventArgs) =>
{
    FileNovaLogger.Default.Log(
        LogLevel.Error,
        "Unobserved task exception.",
        eventArgs.Exception);
};
AppDomain.CurrentDomain.UnhandledException += unhandledHandler;
TaskScheduler.UnobservedTaskException += unobservedHandler;

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
catch (Exception exception)
{
    FileNovaLogger.Default.Log(LogLevel.Critical, "Fatal exception in Program entrypoint.", exception);
    throw;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
    AppDomain.CurrentDomain.UnhandledException -= unhandledHandler;
    TaskScheduler.UnobservedTaskException -= unobservedHandler;
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
            new LibMpvPlaybackPlayerLauncher(libraryPath, lowLatency: playback.LowLatency),
            Console.Error,
            cancellationToken).ConfigureAwait(false);
    }

    return await new NovaWindowsStartupComposition(new()
    {
        PlayerExecutablePath = playback.PlayerExecutablePath,
        LibMpvLibraryPath = playback.LibMpvLibraryPath,
    }).RunGenesisAsync(cancellationToken).ConfigureAwait(false);
}
