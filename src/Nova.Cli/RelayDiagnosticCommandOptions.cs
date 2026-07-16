using System.Globalization;
using System.Net;

namespace PeerCast.Nova.Cli;

internal sealed record RelayDiagnosticCommandOptions
{
    private const string Mode = "--pcp-relay-diagnostics";

    public string UpstreamHost { get; init; } = "127.0.0.1";

    public int UpstreamPort { get; init; } = 7144;

    public Guid ChannelId { get; init; }

    public long InitialPosition { get; init; }

    public int ListenPort { get; init; } = 17145;

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);

    public int MinimumPackets { get; init; } = 4;

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public int Reconnects { get; init; } = 2;

    public static RelayDiagnosticCommandOptions Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = ReadValues(arguments);
        var channelText = GetRequired(values, "--channel");

        if (!Guid.TryParseExact(channelText, "N", out var channelId))
        {
            throw new ArgumentException(
                "--channel must be a 32-digit hexadecimal channel ID.");
        }

        var host = Get(values, "--host", "127.0.0.1");

        if (string.IsNullOrWhiteSpace(host) ||
            host.Length > 255 ||
            host.Any(character => character is < (char)0x21 or > (char)0x7e))
        {
            throw new ArgumentException(
                "--host must contain 1 to 255 non-whitespace ASCII characters.");
        }

        var upstreamPort = ParseInt(
            values,
            "--port",
            defaultValue: 7144,
            minimum: 1,
            IPEndPoint.MaxPort);
        var initialPosition = ParseLong(
            values,
            "--position",
            defaultValue: 0,
            minimum: 0,
            long.MaxValue);
        var listenPort = ParseInt(
            values,
            "--relay-listen-port",
            defaultValue: 17145,
            minimum: 1,
            IPEndPoint.MaxPort);
        var durationMilliseconds = ParseInt(
            values,
            "--relay-duration-ms",
            defaultValue: 30_000,
            minimum: 1_000,
            maximum: 600_000);
        var minimumPackets = ParseInt(
            values,
            "--relay-min-packets",
            defaultValue: 4,
            minimum: 1,
            maximum: 1_000_000);
        var idleTimeoutMilliseconds = ParseInt(
            values,
            "--relay-idle-timeout-ms",
            defaultValue: 15_000,
            minimum: 1,
            maximum: 600_000);
        var reconnects = ParseInt(
            values,
            "--relay-reconnects",
            defaultValue: 2,
            minimum: 0,
            maximum: 10);

        return new RelayDiagnosticCommandOptions
        {
            UpstreamHost = host,
            UpstreamPort = upstreamPort,
            ChannelId = channelId,
            InitialPosition = initialPosition,
            ListenPort = listenPort,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            MinimumPackets = minimumPackets,
            IdleTimeout = TimeSpan.FromMilliseconds(idleTimeoutMilliseconds),
            Reconnects = reconnects,
        };
    }

    private static Dictionary<string, string> ReadValues(
        IReadOnlyList<string> arguments)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--host",
            "--port",
            "--channel",
            "--position",
            "--relay-listen-port",
            "--relay-duration-ms",
            "--relay-min-packets",
            "--relay-idle-timeout-ms",
            "--relay-reconnects",
        };
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (string.Equals(
                argument,
                Mode,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!allowed.Contains(argument))
            {
                throw new ArgumentException(
                    $"Unknown relay diagnostic option: {argument}");
            }

            if (index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{argument} requires a value.");
            }

            if (!values.TryAdd(argument, arguments[++index]))
            {
                throw new ArgumentException(
                    $"{argument} cannot be specified more than once.");
            }
        }

        return values;
    }

    private static string GetRequired(
        Dictionary<string, string> values,
        string name)
    {
        return values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException(
                $"{name} requires a value.");
    }

    private static string Get(
        Dictionary<string, string> values,
        string name,
        string defaultValue)
    {
        return values.TryGetValue(name, out var value)
            ? value
            : defaultValue;
    }

    private static int ParseInt(
        Dictionary<string, string> values,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var text = Get(
            values,
            name,
            defaultValue.ToString(CultureInfo.InvariantCulture));

        if (!int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentException(
                $"{name} must be between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        return value;
    }

    private static long ParseLong(
        Dictionary<string, string> values,
        string name,
        long defaultValue,
        long minimum,
        long maximum)
    {
        var text = Get(
            values,
            name,
            defaultValue.ToString(CultureInfo.InvariantCulture));

        if (!long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentException(
                $"{name} is outside the supported range.");
        }

        return value;
    }
}
