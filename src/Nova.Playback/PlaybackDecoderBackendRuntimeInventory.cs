namespace PeerCast.Nova.Playback;

public sealed record PlaybackDecoderBackendRuntimeFile
{
    public PlaybackDecoderBackendRuntimeFile(
        string name,
        long length,
        string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 64 || name.Any(static value =>
                !(char.IsAsciiLetterOrDigit(value) || value is '.' or '-')))
        {
            throw new ArgumentException(
                "Runtime file names must be bounded portable file names.",
                nameof(name));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (sha256.Length != 64 || sha256.Any(static value => !char.IsAsciiHexDigit(value)))
        {
            throw new ArgumentException(
                "Runtime file digests must be 64-character SHA-256 hexadecimal values.",
                nameof(sha256));
        }

        Name = name;
        Length = length;
        Sha256 = sha256.ToUpperInvariant();
    }

    public string Name { get; }

    public long Length { get; }

    public string Sha256 { get; }
}

public sealed class PlaybackDecoderBackendRuntimeInventory
{
    private readonly PlaybackDecoderBackendRuntimeFile[] _files;

    public PlaybackDecoderBackendRuntimeInventory(
        IEnumerable<PlaybackDecoderBackendRuntimeFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files
            .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_files.Length == 0)
        {
            throw new ArgumentException("At least one runtime file is required.", nameof(files));
        }

        if (_files.GroupBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Runtime file names must be unique.", nameof(files));
        }

        Files = Array.AsReadOnly(_files);
    }

    public IReadOnlyList<PlaybackDecoderBackendRuntimeFile> Files { get; }
}

public static class PlaybackFfmpegBackendRuntimeInventory
{
    public static PlaybackDecoderBackendRuntimeInventory PinnedCandidate { get; } = new([
        new("avcodec-62.dll", 70881792, "0C5904AE70E56FEDD9A48E3A51C085DE136AA673CDB8220CAEC834A81E24566E"),
        new("avformat-62.dll", 22070784, "0403E7473A2E2165CA80A31305B6FCC6CCDA2437482188E26EBD46858388637D"),
        new("avutil-60.dll", 2937856, "597FBF082124905EF7652247C09EDF226739C915DB51B686D45297C5D67F6D46"),
        new("swresample-6.dll", 723968, "1386CB8F61EA9C525B6D08A18A6AD71BF08311B95493963BD818E1DF888F209A"),
        new("swscale-9.dll", 12570624, "5FE2519E5CFDCDAB9815DE553BEB7A33B97D5DD4D9715AFB331F147EC2E44AD1"),
    ]);
}
