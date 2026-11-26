using System.Globalization;

namespace PeerCast.Nova.App;

/// <summary>Explicit, bounded inputs for the listener acceptance path only.</summary>
internal sealed record NovaAcceptanceRelayOptions(string RunId, TimeSpan Duration, int Port, bool AutoPortMapping)
{
    internal const string Switch = "--acceptance-headless-relay";
    internal const string Usage = "Nova.App --acceptance-headless-relay --duration <1..300>s --port <0..65535> --auto-port-mapping <on|off> --run-id <id>";

    internal static bool TryParse(string[] args, out NovaAcceptanceRelayOptions? options)
    {
        options = null;
        if (args.Length != 9 || args[0] != Switch)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (args[index] is not ("--duration" or "--port" or "--auto-port-mapping" or "--run-id") ||
                !values.TryAdd(args[index], args[index + 1]))
            {
                return false;
            }
        }

        if (values.Count != 4 || !values.TryGetValue("--duration", out var duration) ||
            !duration.EndsWith('s') ||
            !int.TryParse(duration.AsSpan(0, duration.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds is < 1 or > 300 ||
            !int.TryParse(values["--port"], NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 0 or > 65535 ||
            values["--auto-port-mapping"] is not ("on" or "off"))
        {
            return false;
        }

        var runId = values["--run-id"];
        if (runId.Length is < 1 or > 96 || runId.Any(static ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_' or '.')))
        {
            return false;
        }

        options = new(runId, TimeSpan.FromSeconds(seconds), port, values["--auto-port-mapping"] == "on");
        return true;
    }
}
