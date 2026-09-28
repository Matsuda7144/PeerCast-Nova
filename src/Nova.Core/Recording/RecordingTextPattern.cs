using System.Text;
using System.Text.RegularExpressions;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Validated text matcher used by recording rule criteria.
/// </summary>
/// <remarks>
/// A regex evaluation timeout is treated as a non-match so a pathological
/// pattern cannot open a recording side effect.
/// </remarks>
public sealed record RecordingTextPattern
{
    private static readonly TimeSpan RegexTimeout =
        TimeSpan.FromMilliseconds(100);

    private readonly Regex? regex;

    public RecordingTextPattern(
        string value,
        RecordingTextMatchMode mode = RecordingTextMatchMode.Contains)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Value = value;
        Mode = mode;

        if (mode == RecordingTextMatchMode.Regex)
        {
            try
            {
                regex = new Regex(
                    value,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout);
            }
            catch (RegexParseException exception)
            {
                throw new ArgumentException(
                    "Recording regex pattern is invalid.",
                    nameof(value),
                    exception);
            }
        }
    }

    public string Value { get; }

    public RecordingTextMatchMode Mode { get; }

    internal bool IsMatch(string? candidate)
    {
        candidate ??= string.Empty;

        return Mode switch
        {
            RecordingTextMatchMode.Exact =>
                string.Equals(
                    Normalize(candidate),
                    Normalize(Value),
                    StringComparison.OrdinalIgnoreCase),

            RecordingTextMatchMode.Contains =>
                Normalize(candidate).Contains(
                    Normalize(Value),
                    StringComparison.OrdinalIgnoreCase),

            RecordingTextMatchMode.Regex =>
                IsRegexMatch(candidate),

            _ => throw new InvalidOperationException(
                $"Unsupported recording text match mode: {Mode}."),
        };
    }

    private bool IsRegexMatch(string candidate)
    {
        try
        {
            return regex!.IsMatch(candidate);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string Normalize(string value)
    {
        return value.Normalize(NormalizationForm.FormKC);
    }
}
