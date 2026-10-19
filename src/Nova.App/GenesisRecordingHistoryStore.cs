using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingHistoryLoadStatus { Available, MissingFile, Invalid, UnsupportedVersion, Unavailable }
internal enum GenesisRecordingHistoryWriteStatus { None, Saved, Failed, Conflict, Cancelled }
internal enum GenesisRecordingHistoryStoreFailure { None, Malformed, TooLarge, Version, Io, NoPath, Cancelled }
internal sealed record GenesisRecordingHistoryLoadResult(
    GenesisRecordingHistoryLoadStatus Status, GenesisRecordingMetadataSnapshot? Document,
    string? Fingerprint, GenesisRecordingHistoryStoreFailure Failure)
{
    public GenesisRecordingHistorySnapshot? Snapshot => Document?.History;
    public GenesisRecordingLibraryKnownRootSnapshot? KnownRoots => Document?.KnownRoots;
    public bool CanWrite => Status is GenesisRecordingHistoryLoadStatus.Available or GenesisRecordingHistoryLoadStatus.MissingFile;
}
internal sealed record GenesisRecordingHistorySaveResult(
    GenesisRecordingHistoryWriteStatus Status, string? Fingerprint, GenesisRecordingHistoryStoreFailure Failure);

/// <summary>Dedicated bounded History metadata I/O. Never accesses recording media.</summary>
internal sealed class GenesisRecordingHistoryStore
{
    internal const int MaximumByteCount = 8 * 1024 * 1024;
    private readonly string? _path;
    private readonly Func<string, byte[], CancellationToken, Task> _writeTemporary;
    private readonly Action<string, string> _replace;

    internal GenesisRecordingHistoryStore(string? path,
        Func<string, byte[], CancellationToken, Task>? writeTemporary = null,
        Action<string, string>? replace = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        _writeTemporary = writeTemporary ?? WriteTemporaryAsync;
        _replace = replace ?? ((temporary, destination) => File.Move(temporary, destination, overwrite: true));
    }

    internal static string? ResolvePath(string? localApplicationData) => string.IsNullOrWhiteSpace(localApplicationData)
        ? null : Path.Combine(localApplicationData, SystemGenesisGuiNextApplicationState.ApplicationDirectoryName, "recording-history.json");
    internal static GenesisRecordingHistoryStore CreateSystem() => new(ResolvePath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    internal GenesisRecordingHistoryLoadResult Load()
    {
        if (_path is null) return new(GenesisRecordingHistoryLoadStatus.Unavailable, null, null, GenesisRecordingHistoryStoreFailure.NoPath);
        try
        {
            var bytes = ReadBounded();
            if (bytes is null) return new(GenesisRecordingHistoryLoadStatus.MissingFile, null, null, GenesisRecordingHistoryStoreFailure.None);
            var hash = Hash(bytes);
            try
            {
                return new(GenesisRecordingHistoryLoadStatus.Available, GenesisRecordingHistoryJsonCodec.DeserializeDocument(bytes), hash, GenesisRecordingHistoryStoreFailure.None);
            }
            catch (HistoryVersionException)
            {
                return new(GenesisRecordingHistoryLoadStatus.UnsupportedVersion, null, hash, GenesisRecordingHistoryStoreFailure.Version);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return new(GenesisRecordingHistoryLoadStatus.Invalid, null, hash, GenesisRecordingHistoryStoreFailure.Malformed);
            }
        }
        catch (HistorySizeException) { return new(GenesisRecordingHistoryLoadStatus.Invalid, null, null, GenesisRecordingHistoryStoreFailure.TooLarge); }
        catch (Exception exception) when (IsIoFailure(exception))
        { return new(GenesisRecordingHistoryLoadStatus.Unavailable, null, null, GenesisRecordingHistoryStoreFailure.Io); }
    }

    internal async Task<GenesisRecordingHistorySaveResult> SaveAsync(
        GenesisRecordingMetadataSnapshot snapshot, string? expectedFingerprint, CancellationToken token)
    {
        if (_path is null) return new(GenesisRecordingHistoryWriteStatus.Failed, expectedFingerprint, GenesisRecordingHistoryStoreFailure.NoPath);
        string? temporary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var bytes = GenesisRecordingHistoryJsonCodec.Serialize(snapshot);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            // Narrow cooperative store lock, not a single-application guarantee.
            // Keep the empty lock file: unlinking it could split lock ownership.
            using var authority = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!Matches(expectedFingerprint)) return Conflict(expectedFingerprint);
            temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            await _writeTemporary(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Re-check after slow temporary write. A non-cooperating external
            // writer still has a filesystem check/rename race; no CAS claim.
            if (!Matches(expectedFingerprint)) return Conflict(expectedFingerprint);
            token.ThrowIfCancellationRequested();
            _replace(temporary, _path);
            return new(GenesisRecordingHistoryWriteStatus.Saved, Hash(bytes), GenesisRecordingHistoryStoreFailure.None);
        }
        catch (HistorySizeException) { return new(GenesisRecordingHistoryWriteStatus.Failed, expectedFingerprint, GenesisRecordingHistoryStoreFailure.TooLarge); }
        catch (OperationCanceledException) { return new(GenesisRecordingHistoryWriteStatus.Cancelled, expectedFingerprint, GenesisRecordingHistoryStoreFailure.Cancelled); }
        catch (Exception exception) when (IsIoFailure(exception) || exception is JsonException or ArgumentException or InvalidOperationException)
        { return new(GenesisRecordingHistoryWriteStatus.Failed, expectedFingerprint, GenesisRecordingHistoryStoreFailure.Io); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (IsIoFailure(exception)) { }
            }
        }
    }

    private bool Matches(string? fingerprint)
    {
        try { var bytes = ReadBounded(); return (bytes is null ? null : Hash(bytes)) == fingerprint; }
        catch (HistorySizeException) { return false; }
    }

    private byte[]? ReadBounded()
    {
        try
        {
            using var stream = new FileStream(_path!, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumByteCount) throw new HistorySizeException();
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.Length != bytes.Length) throw new IOException("History changed during read.");
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static GenesisRecordingHistorySaveResult Conflict(string? expected) => new(
        GenesisRecordingHistoryWriteStatus.Conflict, expected, GenesisRecordingHistoryStoreFailure.None);
    private static bool IsIoFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException;

    private static async Task WriteTemporaryAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }
}
