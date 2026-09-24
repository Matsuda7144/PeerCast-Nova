namespace PeerCast.Nova.App;

internal static class NovaApplicationEntrypoint
{
    private const string Usage =
        "Usage: Nova.App [--help | --about] [--pcrplayer-executable <path>]";

    internal static async ValueTask<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<CancellationToken, ValueTask<NovaWindowsStartupResult>> runApplication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(runApplication);

        if (args.Length == 0)
        {
            var result = await runApplication(cancellationToken).ConfigureAwait(false);
            return result switch
            {
                NovaWindowsStartupResult.Completed => 0,
                NovaWindowsStartupResult.Cancelled => 0,
                NovaWindowsStartupResult.PlatformUnsupported => 2,
                _ => 1,
            };
        }

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            await output.WriteLineAsync("  --help   Show this help without starting the application.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --about  Show legal and external-runtime information.")
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

        await error.WriteLineAsync(Usage).ConfigureAwait(false);
        return 2;
    }

    internal static async ValueTask<int> RunDailyAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string?, CancellationToken, ValueTask<NovaWindowsStartupResult>> runApplication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(runApplication);

        if (!NovaDailyPlaybackCommandLine.TryParse(
                args,
                out var executablePath,
                out var applicationArguments))
        {
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        if (applicationArguments.Length == 1 &&
            applicationArguments[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            await output.WriteLineAsync("  --help   Show this help without starting the application.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("  --about  Show legal and external-runtime information.")
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                    "  --pcrplayer-executable <path>  Configure PCRPlayer64.exe.")
                .ConfigureAwait(false);
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
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
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
