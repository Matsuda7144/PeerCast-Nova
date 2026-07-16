using System.Globalization;
using System.Net;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal sealed record PlaybackDecoderLifecycleProbeOptions
{
    private const string Mode =
        "--playback-decoder-lifecycle-probe";

    public string UpstreamHost { get; init; } = "127.0.0.1";

    public int UpstreamPort { get; init; } = 7144;

    public Guid ChannelId { get; init; }

    public long InitialPosition { get; init; }

    public TimeSpan Duration { get; init; } =
        TimeSpan.FromSeconds(15);

    public TimeSpan IdleTimeout { get; init; } =
        TimeSpan.FromSeconds(10);

    public TimeSpan DecoderStopTimeout { get; init; } =
        TimeSpan.FromSeconds(2);

    public int Reconnects { get; init; }

    public int MaxProbeBytes { get; init; } = 64 * 1024;

    public int MaxDiscontinuities { get; init; } = 4;

    public int InputLimit { get; init; } = 4;

    public static PlaybackDecoderLifecycleProbeOptions Parse(
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
            host.Any(character =>
                character is < (char)0x21 or > (char)0x7e))
        {
            throw new ArgumentException(
                "--host must contain 1 to 255 non-whitespace ASCII characters.");
        }

        return new PlaybackDecoderLifecycleProbeOptions
        {
            UpstreamHost = host,
            UpstreamPort = ParseInt(
                values,
                "--port",
                7144,
                1,
                IPEndPoint.MaxPort),
            ChannelId = channelId,
            InitialPosition = ParseLong(
                values,
                "--position",
                0,
                0,
                long.MaxValue),
            Duration = TimeSpan.FromMilliseconds(ParseInt(
                values,
                "--playback-lifecycle-duration-ms",
                15_000,
                1_000,
                120_000)),
            IdleTimeout = TimeSpan.FromMilliseconds(ParseInt(
                values,
                "--playback-idle-timeout-ms",
                10_000,
                1_000,
                120_000)),
            DecoderStopTimeout = TimeSpan.FromMilliseconds(ParseInt(
                values,
                "--playback-decoder-stop-timeout-ms",
                2_000,
                (int)PlaybackDecoderRunnerOptions.MinimumStopTimeout
                    .TotalMilliseconds,
                (int)PlaybackDecoderRunnerOptions.MaximumStopTimeout
                    .TotalMilliseconds)),
            Reconnects = ParseInt(
                values,
                "--playback-reconnects",
                0,
                0,
                10),
            MaxProbeBytes = ParseInt(
                values,
                "--playback-probe-max-bytes",
                64 * 1024,
                PlaybackMediaClassificationOptions.MinimumProbeBytes,
                PlaybackMediaClassificationOptions
                    .MaximumProbeBytesLimit),
            MaxDiscontinuities = ParseInt(
                values,
                "--playback-probe-max-discontinuities",
                4,
                0,
                PlaybackMediaClassificationOptions
                    .MaximumDiscontinuitiesLimit),
            InputLimit = ParseInt(
                values,
                "--playback-lifecycle-inputs",
                4,
                1,
                1_000_000),
        };
    }

    private static Dictionary<string, string> ReadValues(
        IReadOnlyList<string> arguments)
    {
        var allowed = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "--host",
            "--port",
            "--channel",
            "--position",
            "--playback-lifecycle-duration-ms",
            "--playback-idle-timeout-ms",
            "--playback-decoder-stop-timeout-ms",
            "--playback-reconnects",
            "--playback-probe-max-bytes",
            "--playback-probe-max-discontinuities",
            "--playback-lifecycle-inputs",
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
                    $"Unknown playback decoder lifecycle option: {argument}");
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
