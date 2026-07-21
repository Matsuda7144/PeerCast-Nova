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
        new("avcodec-62.dll", 70875136, "4936D6DD5C1615AC4166017A9843F4D8DC415EF6D3156A4138B84C2A4419818C"),
        new("avdevice-62.dll", 3699712, "3C19667DAAB10CB42C0870D8199DA87AD64CFDFC692D00C5FD1D2191E495EBA8"),
        new("avfilter-11.dll", 29816320, "FF4E9FF9ACB4B051D73A2561FDF724DE6295D60374DF5D309A714ADF699343D2"),
        new("avformat-62.dll", 22070784, "198B582EE199BA16C973A438FA125A2448772755BC2E95CC01B28AB434270290"),
        new("avutil-60.dll", 2937856, "419513335F9209499243C3C7F2EA46C0B3F477281D80DE90E696E2E1062D7452"),
        new("ffmpeg.exe", 542208, "02E08DE9538CFA13EDD8E4FD81F897C2308594DCBEB22FE3A7AD6E1A5937FA2A"),
        new("ffplay.exe", 17902592, "2657F43E5C459B1655DA778922FE69C85C280D9419237AB3A146BA7835FC9758"),
        new("ffprobe.exe", 227840, "2CC6C3D9C49028355E030B46208AC0E630A2A7A84381659D935183B1EA205E47"),
        new("swresample-6.dll", 723968, "344CA8558DCED8431D54E692A171F5167510E940E860292F33CD77460DB0E83B"),
        new("swscale-9.dll", 12570624, "7EBE9B9B4B8B23DA6E86631573E85AE484392CB7B3D0ECF506F621BDC1222229"),
    ]);

    public static PlaybackDecoderBackendRuntimeInventory SupersededCandidate20260715
    { get; } = new([
        new("avcodec-62.dll", 70881792, "0C5904AE70E56FEDD9A48E3A51C085DE136AA673CDB8220CAEC834A81E24566E"),
        new("avformat-62.dll", 22070784, "0403E7473A2E2165CA80A31305B6FCC6CCDA2437482188E26EBD46858388637D"),
        new("avutil-60.dll", 2937856, "597FBF082124905EF7652247C09EDF226739C915DB51B686D45297C5D67F6D46"),
        new("swresample-6.dll", 723968, "1386CB8F61EA9C525B6D08A18A6AD71BF08311B95493963BD818E1DF888F209A"),
        new("swscale-9.dll", 12570624, "5FE2519E5CFDCDAB9815DE553BEB7A33B97D5DD4D9715AFB331F147EC2E44AD1"),
    ]);
}
