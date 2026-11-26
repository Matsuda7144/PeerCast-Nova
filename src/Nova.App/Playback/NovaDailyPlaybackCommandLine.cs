namespace PeerCast.Nova.App;

internal static class NovaDailyPlaybackCommandLine
{
    internal const string ExecutablePathOption = "--pcrplayer-executable";

    internal static bool TryParse(
        string[] args,
        out string? executablePath,
        out string[] applicationArguments)
    {
        ArgumentNullException.ThrowIfNull(args);
        executablePath = null;
        var forwarded = new List<string>();
        var executablePathCount = 0;
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], ExecutablePathOption, StringComparison.Ordinal))
            {
                forwarded.Add(args[index]);
                continue;
            }

            executablePathCount++;
            if (index + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                applicationArguments = [.. forwarded, .. args[(index + 1)..]];
                return false;
            }

            executablePath = args[++index];
        }

        applicationArguments = [.. forwarded];
        return executablePathCount <= 1;
    }
}
