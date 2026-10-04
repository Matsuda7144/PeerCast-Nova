using System.Text.Json;
using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App;

internal sealed class NovaSettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static NovaSettingsStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeerCast", "Nova", "settings.json"));

    public NovaOptions Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<NovaOptions>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Nova settings contain no document.")
        : new NovaOptions();

    public async Task SaveAsync(NovaOptions settings)
    {
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
