using PeerCast.Nova.Playback;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal sealed record NovaPlaybackLaunchOptions(
    string? PlayerExecutablePath,
    string? LibMpvLibraryPath,
    string? Fmp4StreamUrl = null,
    bool LowLatency = false);

internal static class NovaApplicationEntrypoint
{
    private const string DailyUsage =
        "Usage: Nova.App [--help | --about] [--pcrplayer-executable <path>]";
    private const string Usage =
        "Usage: Nova.App [--help | --about] [--pcrplayer-executable <path> | --libmpv-library <path>]";
    private const string LibMpvLibraryOption = "--libmpv-library";
    private const string Fmp4PlaybackOption = "--play-fmp4";
    private const string LowLatencyOption = "--low-latency";
    private const string Fmp4Usage = "Usage: Nova.App --play-fmp4 <loopback-url> --libmpv-library <path> [--low-latency]";
    private const string PlayerExecutableHelp =
        "  --pcrplayer-executable <path>  Configure the external player with an absolute .exe path.";

    internal static async ValueTask<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<NovaPlaybackLaunchOptions, CancellationToken, ValueTask<NovaWindowsStartupResult>> runApplication,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, ValueTask<NovaWindowsStartupResult>>? runValidationApplication = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(runApplication);

        if (args.Length == 2 && args[0] == "--validation-profile")
        {
            if (runValidationApplication is null ||
                !NovaValidationProfilePaths.IsAbsoluteRoot(args[1]))
            {
                await error.WriteLineAsync("An explicit absolute validation profile is required.").ConfigureAwait(false);
                return 2;
            }
            try
            {
                var result = await runValidationApplication(args[1], cancellationToken).ConfigureAwait(false);
                if (result is NovaWindowsStartupResult.Completed or NovaWindowsStartupResult.Cancelled) return 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
            catch (Exception) { }
            await error.WriteLineAsync("Validation profile startup rejected. No production fallback.").ConfigureAwait(false);
            return 1;
        }

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            await output.WriteLineAsync(Fmp4Usage).ConfigureAwait(false);
            await output.WriteLineAsync("  --help   Show this help without starting the application.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --about  Show legal and external-runtime information.")
                .ConfigureAwait(false);
            await output.WriteLineAsync(PlayerExecutableHelp).ConfigureAwait(false);
            await output.WriteLineAsync("  --libmpv-library <path>  Play FLV in a Nova window using an absolute libmpv DLL path.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --play-fmp4 <loopback-url>  Open Nova's local fMP4 stream in an embedded playback window.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --low-latency  Reduce readahead and rebuffering thresholds for --play-fmp4.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --validation-profile <absolute-path>  Isolated network-settings validation; media/router operations blocked.")
                .ConfigureAwait(false);
            return 0;
        }

        if (args.Length == 1 && args[0] == "--about")
        {
            var material = NovaAboutLegalMaterial.Load();
            if (material.Result != NovaAboutLegalMaterialResult.Loaded ||
                material.Model is not { } model)
            {
                await error.WriteLineAsync("About material is unavailable.").ConfigureAwait(false);
                return 1;
            }

            await WriteAboutAsync(output, model).ConfigureAwait(false);
            return 0;
        }

        NovaPlaybackLaunchOptions options;
        if (args is [LibMpvLibraryOption, var libraryPath] &&
            LibMpvPlaybackPlayerLauncher.IsLibraryPath(libraryPath))
        {
            options = new(null, libraryPath);
        }
        else if (args is [Fmp4PlaybackOption, var streamUrl, LibMpvLibraryOption, var fmp4LibraryPath, .. var fmp4Options] &&
            fmp4Options is [] or [LowLatencyOption] &&
            LibMpvPlaybackPlayerLauncher.IsLibraryPath(fmp4LibraryPath) &&
            LibMpvPlaybackPlayerLauncher.IsLoopbackStream(streamUrl, PlaybackMediaContainer.FragmentedMp4))
        {
            options = new(null, fmp4LibraryPath, streamUrl, LowLatency: fmp4Options.Length != 0);
        }
        else if (args.Length != 0 && args[0] == Fmp4PlaybackOption)
        {
            await error.WriteLineAsync(Fmp4Usage).ConfigureAwait(false);
            return 2;
        }
        else if (!NovaDailyPlaybackCommandLine.TryParse(args, out var executablePath, out var applicationArguments) ||
            applicationArguments.Length != 0)
        {
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }
        else
        {
            options = new(executablePath, null);
        }

        var startupResult = await runApplication(options, cancellationToken).ConfigureAwait(false);
        return startupResult switch
        {
            NovaWindowsStartupResult.Completed => 0,
            NovaWindowsStartupResult.Cancelled => 0,
            NovaWindowsStartupResult.PlatformUnsupported => 2,
            _ => 1,
        };
    }

    internal static async ValueTask<NovaWindowsStartupResult> PlayFmp4Async(
        string streamUrl,
        IDailyPlaybackPlayerLauncher launcher,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = new DailyPlaybackPlayerSource(
                streamUrl, string.Empty, null, null, PlaybackMediaContainer.FragmentedMp4);
            await using var playback = await launcher.LaunchAsync(source, cancellationToken).ConfigureAwait(false);
            await playback.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            return NovaWindowsStartupResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NovaWindowsStartupResult.Cancelled;
        }
        catch (LibMpvPlaybackException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return NovaWindowsStartupResult.Faulted;
        }
    }

    internal static async ValueTask<int> RunDailyAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string?, CancellationToken, ValueTask<NovaWindowsStartupResult>> runApplication,
        CancellationToken cancellationToken,
        Func<NovaAcceptanceRelayOptions, TextWriter, CancellationToken, Task<int>>? runAcceptance = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(runApplication);

        if (args.Contains(NovaAcceptanceRelayOptions.Switch, StringComparer.Ordinal))
        {
            if (!NovaAcceptanceRelayOptions.TryParse(args, out var acceptance) || acceptance is null)
            {
                await error.WriteLineAsync(NovaAcceptanceRelayOptions.Usage).ConfigureAwait(false);
                return 2;
            }
            return await (runAcceptance ?? (static (options, writer, token) =>
                NovaAcceptanceRelayHarness.RunAsync(options, writer, token)))(acceptance, output, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!NovaDailyPlaybackCommandLine.TryParse(
                args,
                out var executablePath,
                out var applicationArguments))
        {
            await error.WriteLineAsync(DailyUsage).ConfigureAwait(false);
            return 2;
        }

        if (applicationArguments.Length == 1 &&
            applicationArguments[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(DailyUsage).ConfigureAwait(false);
            await output.WriteLineAsync("  --help   Show this help without starting the application.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --about  Show legal and external-runtime information.")
                .ConfigureAwait(false);
            await output.WriteLineAsync(PlayerExecutableHelp).ConfigureAwait(false);
            await output.WriteLineAsync("  " + NovaAcceptanceRelayOptions.Usage).ConfigureAwait(false);
            return 0;
        }

        if (applicationArguments.Length == 1 && applicationArguments[0] == "--about")
        {
            var material = NovaAboutLegalMaterial.Load();
            if (material.Result != NovaAboutLegalMaterialResult.Loaded ||
                material.Model is not { } model)
            {
                await error.WriteLineAsync("About material is unavailable.").ConfigureAwait(false);
                return 1;
            }

            await WriteAboutAsync(output, model).ConfigureAwait(false);
            return 0;
        }

        if (applicationArguments.Length != 0)
        {
            await error.WriteLineAsync(DailyUsage).ConfigureAwait(false);
            return 2;
        }

        var result = await runApplication(executablePath, cancellationToken)
            .ConfigureAwait(false);
        return result switch
        {
            NovaWindowsStartupResult.Completed => 0,
            NovaWindowsStartupResult.Cancelled => 0,
            NovaWindowsStartupResult.PlatformUnsupported => 2,
            _ => 1,
        };
    }

    private static async ValueTask WriteAboutAsync(
        TextWriter output,
        NovaAboutLegalModel model)
    {
        await output.WriteLineAsync(model.ProductName).ConfigureAwait(false);
        await output.WriteLineAsync($"Project license: {model.ProjectLicense}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"External runtime: {model.ExternalRuntimeName}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"External runtime distribution: {model.ExternalRuntimeDistribution}")
            .ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(model.Notice).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(model.Provenance).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(model.RuntimeReplacement).ConfigureAwait(false);
    }
}
