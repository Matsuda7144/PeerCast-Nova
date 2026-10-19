namespace PeerCast.Nova.App;

internal enum GenesisRecordingOutputDirectorySaveResult
{
    Saved,
    Invalid,
    Unavailable,
}

internal sealed record GenesisRecordingOutputDirectoryPresentation(
    string? ConfiguredDirectory,
    string EffectiveDirectory,
    string CurrentRuntimeDirectory,
    bool UsesDefault,
    string Notice,
    string? Error);

/// <summary>
/// App-global destination settings. The runtime snapshot is immutable; writes
/// affect only the next application start, never an existing writer or owner.
/// </summary>
internal sealed class GenesisRecordingOutputDirectorySettings
{
    internal const string NextStartNotice = "録画保存先の変更は次回起動時から有効になります。";
    private readonly NovaSettingsStore _store;
    private readonly string _defaultDirectory;
    private readonly object _gate = new();
    private string? _configuredDirectory;
    private string? _error;

    internal GenesisRecordingOutputDirectorySettings(NovaSettingsStore store, string defaultDirectory)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!TryNormalize(defaultDirectory, out var normalizedDefault))
        {
            throw new ArgumentException("The default recording directory must be absolute.", nameof(defaultDirectory));
        }

        _store = store;
        _defaultDirectory = normalizedDefault!;
        try
        {
            var configured = store.Load().RecordingOutputDirectory;
            if (configured is not null)
            {
                if (TryNormalize(configured, out var normalized))
                {
                    _configuredDirectory = normalized;
                }
                else
                {
                    _error = "保存済みの録画保存先が無効です。今回は既定の保存先を使用します。";
                }
            }
        }
        catch (Exception exception) when (IsSettingsFailure(exception))
        {
            _error = "設定を読み込めませんでした。今回は既定の録画保存先を使用します。";
        }

        CurrentRuntimeDirectory = _configuredDirectory ?? _defaultDirectory;
    }

    public string CurrentRuntimeDirectory { get; }

    internal string DefaultDirectory => _defaultDirectory;

    public GenesisRecordingOutputDirectoryPresentation Presentation
    {
        get
        {
            lock (_gate)
            {
                return new(_configuredDirectory, _configuredDirectory ?? _defaultDirectory,
                    CurrentRuntimeDirectory, _configuredDirectory is null, NextStartNotice, _error);
            }
        }
    }

    internal static GenesisRecordingOutputDirectorySettings CreateSystem() =>
        new(NovaSettingsStore.CreateDefault(), ResolveDefaultDirectory());

    internal static string ResolveDefaultDirectory() => ResolveDefaultDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string ResolveDefaultDirectory(string videos, string localApplicationData) =>
        Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(videos) ? localApplicationData : videos,
            "PeerCast Nova", "Recordings"));

    internal static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var candidate = value.Trim();
            if (!Path.IsPathFullyQualified(candidate) || candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                // Ordinary absolute drive/UNC paths only. Device paths and
                // invalid component characters are not destination settings.
                if (candidate.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                    candidate.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                    candidate.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 ||
                    candidate[Path.GetPathRoot(candidate)!.Length..].Contains(':'))
                {
                    return false;
                }
                foreach (var component in candidate[Path.GetPathRoot(candidate)!.Length..]
                    .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (component is "." or "..") continue;
                    var deviceName = component.Split('.')[0].ToUpperInvariant();
                    if (component.EndsWith('.') || component.EndsWith(' ') ||
                        deviceName is "CON" or "PRN" or "AUX" or "NUL" ||
                        (deviceName.Length == 4 && deviceName[3] is >= '1' and <= '9' &&
                            (deviceName.StartsWith("COM", StringComparison.Ordinal) ||
                             deviceName.StartsWith("LPT", StringComparison.Ordinal))))
                    {
                        return false;
                    }
                }
            }

            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public GenesisRecordingOutputDirectorySaveResult Save(string? value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            lock (_gate)
            {
                _error = "有効な絶対パスを入力してください。設定は変更されていません。";
            }
            return GenesisRecordingOutputDirectorySaveResult.Invalid;
        }

        return Persist(normalized);
    }

    public GenesisRecordingOutputDirectorySaveResult ResetToDefault() => Persist(null);

    private GenesisRecordingOutputDirectorySaveResult Persist(string? configured)
    {
        lock (_gate)
        {
            try
            {
                // Read the current document rather than writing a startup
                // snapshot over unrelated settings (including NAT options).
                _store.UpdateAsync(current => current with { RecordingOutputDirectory = configured })
                    .GetAwaiter().GetResult();
                _configuredDirectory = configured;
                _error = null;
                return GenesisRecordingOutputDirectorySaveResult.Saved;
            }
            catch (Exception exception) when (IsSettingsFailure(exception))
            {
                _error = "設定を保存できませんでした。保存先の設定は変更されていません。";
                return GenesisRecordingOutputDirectorySaveResult.Unavailable;
            }
        }
    }

    private static bool IsSettingsFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or
            NotSupportedException or ArgumentException;
}
