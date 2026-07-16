using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderBackendInstalledRuntimeLocation
{
    Verified,
    PathRejected,
    DirectoryUnavailable,
    ReparsePointRejected,
    InventoryRejected,
    MissingFile,
    UnexpectedFile,
    SizeMismatch,
    DigestMismatch,
}

public sealed class PlaybackDecoderBackendInstalledRuntimeLeaseResult
{
    internal PlaybackDecoderBackendInstalledRuntimeLeaseResult(
        PlaybackDecoderBackendInstalledRuntimeLocation location,
        VerifiedPlaybackDecoderRuntimeLease? lease = null)
    {
        if (!Enum.IsDefined(location))
        {
            throw new ArgumentException(
                "The installed runtime result category is undefined.",
                nameof(location));
        }

        if ((location == PlaybackDecoderBackendInstalledRuntimeLocation.Verified) !=
            (lease is not null))
        {
            throw new ArgumentException(
                "The installed runtime result is inconsistent.",
                nameof(lease));
        }

        Location = location;
        Lease = lease;
    }

    public PlaybackDecoderBackendInstalledRuntimeLocation Location { get; }

    public VerifiedPlaybackDecoderRuntimeLease? Lease { get; }
}

/// <summary>
/// Owns the five verified runtime handles. The handles deny write and delete
/// sharing, so their files cannot be replaced while the lease remains alive.
/// </summary>
public sealed class VerifiedPlaybackDecoderRuntimeLease : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private Dictionary<string, FileStream>? _files;
    private Dictionary<string, string>? _paths;
    private List<SafeFileHandle>? _directoryGuards;

    internal VerifiedPlaybackDecoderRuntimeLease(
        Dictionary<string, FileStream> files,
        Dictionary<string, string> paths,
        List<SafeFileHandle> directoryGuards)
    {
        if (files.Count is <= 0 or > PlaybackDecoderBackendInstalledRuntimeLocator.MaximumFiles)
        {
            throw new ArgumentException(
                "The verified runtime handle count is invalid.",
                nameof(files));
        }

        _files = files;
        _paths = paths;
        _directoryGuards = directoryGuards;
        FileCount = files.Count;
    }

    public int FileCount { get; }

    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _files is null;
            }
        }
    }

    public bool ContainsFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        lock (_sync)
        {
            var files = _files ??
                throw CreateDisposedException();
            return files.ContainsKey(fileName);
        }
    }

    internal TResult UseVerifiedPath<TResult>(
        string fileName,
        Func<string, TResult> guardedOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(guardedOperation);
        lock (_sync)
        {
            var files = _files ??
                throw CreateDisposedException();
            if (!files.ContainsKey(fileName) ||
                _paths is not { } paths ||
                !paths.TryGetValue(fileName, out var path))
            {
                throw new ArgumentException(
                    "The requested runtime file is not part of the verified lease.",
                    nameof(fileName));
            }

            return guardedOperation(path);
        }
    }

    internal TResult UseVerifiedRuntime<TResult>(
        Func<IReadOnlyDictionary<string, VerifiedPlaybackDecoderRuntimeFile>, TResult>
            guardedOperation)
    {
        ArgumentNullException.ThrowIfNull(guardedOperation);
        lock (_sync)
        {
            var files = _files ?? throw CreateDisposedException();
            var paths = _paths ?? throw CreateDisposedException();
            var access = files.ToDictionary(
                static entry => entry.Key,
                entry => new VerifiedPlaybackDecoderRuntimeFile(
                    paths[entry.Key],
                    entry.Value),
                StringComparer.OrdinalIgnoreCase);
            return guardedOperation(access);
        }
    }

    public void Dispose()
    {
        Dictionary<string, FileStream>? files;
        Dictionary<string, string>? paths;
        List<SafeFileHandle>? directoryGuards;
        lock (_sync)
        {
            files = _files;
            paths = _paths;
            directoryGuards = _directoryGuards;
            _files = null;
            _paths = null;
            _directoryGuards = null;
        }

        if (files is null)
        {
            return;
        }

        foreach (var stream in files.Values)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // Read-only cleanup is idempotent and never exposes an OS path.
            }
        }


        paths?.Clear();
        if (directoryGuards is not null)
        {
            foreach (var guard in directoryGuards)
            {
                guard.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static ObjectDisposedException CreateDisposedException() =>
        new(
            nameof(VerifiedPlaybackDecoderRuntimeLease),
            "The verified runtime lease is no longer available.");
}

internal readonly record struct VerifiedPlaybackDecoderRuntimeFile(
    string Path,
    Stream Stream);

/// <summary>
/// Resolves one explicitly supplied local fixed-drive directory. It never
/// searches PATH, the registry, or conventional installation locations.
/// </summary>
public static class PlaybackDecoderBackendInstalledRuntimeLocator
{
    internal const int MaximumFiles = 5;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagSequentialScan = 0x08000000;

    public static async ValueTask<PlaybackDecoderBackendInstalledRuntimeLeaseResult>
        LocateAndVerifyAsync(
            string runtimeDirectory,
            PlaybackDecoderBackendRuntimeInventory inventory,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(inventory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Result(PlaybackDecoderBackendInstalledRuntimeLocation.PathRejected);
        }

        if (inventory.Files.Count is <= 0 or > MaximumFiles)
        {
            return Result(PlaybackDecoderBackendInstalledRuntimeLocation.InventoryRejected);
        }

        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(runtimeDirectory);
            if (!Path.IsPathFullyQualified(fullDirectory) ||
                fullDirectory.StartsWith("\\\\", StringComparison.Ordinal))
            {
                return Result(PlaybackDecoderBackendInstalledRuntimeLocation.PathRejected);
            }

            var root = Path.GetPathRoot(fullDirectory);
            if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType != DriveType.Fixed)
            {
                return Result(PlaybackDecoderBackendInstalledRuntimeLocation.PathRejected);
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            IOException or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            return Result(PlaybackDecoderBackendInstalledRuntimeLocation.PathRejected);
        }

        var directoryGuards = new List<SafeFileHandle>();
        string[] actualPaths;
        try
        {
            if (!Directory.Exists(fullDirectory))
            {
                return Result(
                    PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable);
            }

            if (HasReparsePointInPath(fullDirectory))
            {
                return Result(
                    PlaybackDecoderBackendInstalledRuntimeLocation.ReparsePointRejected);
            }

            var guardResult = TryGuardDirectoryChain(fullDirectory, directoryGuards);
            if (guardResult is not null)
            {
                return Result(guardResult.Value);
            }

            actualPaths = Directory
                .EnumerateFiles(fullDirectory, "*", SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            DisposeHandles(directoryGuards);
            return Result(PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            DisposeHandles(directoryGuards);
            cancellationToken.ThrowIfCancellationRequested();
        }
        var actualByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var actualPath in actualPaths)
        {
            try
            {
                if ((File.GetAttributes(actualPath) & FileAttributes.ReparsePoint) != 0)
                {
                    return Reject(
                        PlaybackDecoderBackendInstalledRuntimeLocation.ReparsePointRejected,
                        directoryGuards);
                }
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                NotSupportedException)
            {
                return Reject(
                    PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable,
                    directoryGuards);
            }

            if (!actualByName.TryAdd(Path.GetFileName(actualPath), actualPath))
            {
                return Reject(
                    PlaybackDecoderBackendInstalledRuntimeLocation.UnexpectedFile,
                    directoryGuards);
            }
        }

        if (actualByName.Count < inventory.Files.Count ||
            inventory.Files.Any(expected => !actualByName.ContainsKey(expected.Name)))
        {
            return Reject(
                PlaybackDecoderBackendInstalledRuntimeLocation.MissingFile,
                directoryGuards);
        }

        if (actualByName.Count > inventory.Files.Count)
        {
            return Reject(
                PlaybackDecoderBackendInstalledRuntimeLocation.UnexpectedFile,
                directoryGuards);
        }

        var streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var expected in inventory.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var entryGuard = CreateFileW(
                        actualByName[expected.Name],
                        desiredAccess: 0,
                        FileShare.Read,
                        IntPtr.Zero,
                        OpenExisting,
                        FileFlagOpenReparsePoint | FileFlagSequentialScan,
                        IntPtr.Zero);
                    if (entryGuard.IsInvalid)
                    {
                        return Result(
                            PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable);
                    }

                    if (!GetFileInformationByHandle(entryGuard, out var information))
                    {
                        return Result(
                            PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable);
                    }

                    if ((information.FileAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return Result(
                            PlaybackDecoderBackendInstalledRuntimeLocation.ReparsePointRejected);
                    }

                    streams.Add(
                        expected.Name,
                        new FileStream(
                            actualByName[expected.Name],
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan));
                }
                catch (Exception exception) when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    NotSupportedException)
                {
                    return Result(
                        PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable);
                }
            }

            PlaybackDecoderBackendRuntimeInventoryVerification verification;
            try
            {
                verification =
                    await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                        inventory,
                        streams.ToDictionary(
                            static entry => entry.Key,
                            static entry => (Stream)entry.Value,
                            StringComparer.OrdinalIgnoreCase),
                        cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                NotSupportedException)
            {
                return Result(
                    PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable);
            }

            if (verification != PlaybackDecoderBackendRuntimeInventoryVerification.Verified)
            {
                return Result(MapVerification(verification));
            }

            var lease = new VerifiedPlaybackDecoderRuntimeLease(
                streams,
                inventory.Files.ToDictionary(
                    static file => file.Name,
                    file => actualByName[file.Name],
                    StringComparer.OrdinalIgnoreCase),
                directoryGuards);
            streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
            directoryGuards = [];
            return new PlaybackDecoderBackendInstalledRuntimeLeaseResult(
                PlaybackDecoderBackendInstalledRuntimeLocation.Verified,
                lease);
        }
        finally
        {
            foreach (var stream in streams.Values)
            {
                try
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is
                    IOException or
                    ObjectDisposedException)
                {
                    // Failure cleanup must not disclose the caller path.
                }
            }


            DisposeHandles(directoryGuards);
        }
    }

    private static bool HasReparsePointInPath(string fullDirectory)
    {
        for (var current = new DirectoryInfo(fullDirectory);
             current is not null;
             current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static PlaybackDecoderBackendInstalledRuntimeLocation? TryGuardDirectoryChain(
        string fullDirectory,
        List<SafeFileHandle> guards)
    {
        var directories = new Stack<string>();
        for (var current = new DirectoryInfo(fullDirectory);
             current is not null;
             current = current.Parent)
        {
            directories.Push(current.FullName);
        }

        foreach (var directory in directories)
        {
            var handle = CreateFileW(
                directory,
                desiredAccess: 0,
                FileShare.Read,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                DisposeHandles(guards);
                return PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable;
            }

            if (!GetFileInformationByHandle(handle, out var information))
            {
                handle.Dispose();
                DisposeHandles(guards);
                return PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable;
            }

            if ((information.FileAttributes & FileAttributes.ReparsePoint) != 0)
            {
                handle.Dispose();
                DisposeHandles(guards);
                return PlaybackDecoderBackendInstalledRuntimeLocation.ReparsePointRejected;
            }

            guards.Add(handle);
        }

        return null;
    }

    private static void DisposeHandles(List<SafeFileHandle> handles)
    {
        foreach (var handle in handles)
        {
            handle.Dispose();
        }

        handles.Clear();
    }

    private static PlaybackDecoderBackendInstalledRuntimeLocation MapVerification(
        PlaybackDecoderBackendRuntimeInventoryVerification verification) =>
        verification switch
        {
            PlaybackDecoderBackendRuntimeInventoryVerification.MissingFile =>
                PlaybackDecoderBackendInstalledRuntimeLocation.MissingFile,
            PlaybackDecoderBackendRuntimeInventoryVerification.UnexpectedFile =>
                PlaybackDecoderBackendInstalledRuntimeLocation.UnexpectedFile,
            PlaybackDecoderBackendRuntimeInventoryVerification.SizeMismatch =>
                PlaybackDecoderBackendInstalledRuntimeLocation.SizeMismatch,
            PlaybackDecoderBackendRuntimeInventoryVerification.DigestMismatch =>
                PlaybackDecoderBackendInstalledRuntimeLocation.DigestMismatch,
            _ => PlaybackDecoderBackendInstalledRuntimeLocation.InventoryRejected,
        };

    private static PlaybackDecoderBackendInstalledRuntimeLeaseResult Result(
        PlaybackDecoderBackendInstalledRuntimeLocation location) => new(location);

    private static PlaybackDecoderBackendInstalledRuntimeLeaseResult Reject(
        PlaybackDecoderBackendInstalledRuntimeLocation location,
        List<SafeFileHandle> directoryGuards)
    {
        DisposeHandles(directoryGuards);
        return Result(location);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public FileAttributes FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
