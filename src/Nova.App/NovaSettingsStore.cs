using System.Text.Json;
using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App;

internal sealed class NovaSettingsStore(string path, Action? checkAccess = null,
    Action<NovaOptions>? validate = null, string? legacyFallbackPath = null)
{
    private static readonly SemaphoreSlim Writes = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal string FilePath => path;

    public static NovaSettingsStore CreateDefault() => new(
        NovaAppDataPaths.GetDefaultSettingsFilePath(),
        legacyFallbackPath: NovaAppDataPaths.GetLegacySettingsFilePath());

    public NovaOptions Load()
    {
        checkAccess?.Invoke();
        var sourcePath = File.Exists(path)
            ? path
            : !string.IsNullOrWhiteSpace(legacyFallbackPath) && File.Exists(legacyFallbackPath)
                ? legacyFallbackPath
                : null;
        var value = sourcePath is not null
            ? JsonSerializer.Deserialize<NovaOptions>(File.ReadAllText(sourcePath))
                ?? throw new InvalidDataException("Nova settings contain no document.")
            : new NovaOptions();
        validate?.Invoke(value);
        return value;
    }

    public async Task UpdateAsync(Func<NovaOptions, NovaOptions> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        await Writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(update(Load())).ConfigureAwait(false);
        }
        finally
        {
            Writes.Release();
        }
    }

    public async Task SaveAsync(NovaOptions settings)
    {
        await Writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            Writes.Release();
        }
    }

    private async Task SaveCoreAsync(NovaOptions settings)
    {
        checkAccess?.Invoke();
        validate?.Invoke(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".nova-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions)).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
