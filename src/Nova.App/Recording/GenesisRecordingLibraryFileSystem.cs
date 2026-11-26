using System.IO.Enumeration;

namespace PeerCast.Nova.App;

internal sealed record GenesisRecordingLibraryDirectoryEntry(string Path, FileAttributes Attributes);

/// <summary>Only direct enumeration and exact-path metadata; no media streams or mutations.</summary>
internal interface IGenesisRecordingLibraryFileSystem
{
    IEnumerable<GenesisRecordingLibraryDirectoryEntry> EnumerateDirectEntries(string root, CancellationToken token);
    GenesisRecordingLibraryFileMetadata ReadFileMetadata(string path, CancellationToken token);
}

internal sealed class SystemGenesisRecordingLibraryFileSystem : IGenesisRecordingLibraryFileSystem
{
    public IEnumerable<GenesisRecordingLibraryDirectoryEntry> EnumerateDirectEntries(string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entries = new FileSystemEnumerable<GenesisRecordingLibraryDirectoryEntry>(root,
            (ref FileSystemEntry entry) => new(entry.ToFullPath(), entry.Attributes),
            new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 });
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public GenesisRecordingLibraryFileMetadata ReadFileMetadata(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // GetAttributes throws on absence/access errors, unlike Exists which
        // conflates them. Check type before reading regular-file metadata.
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            return new(attributes, 0, default, null);
        var info = new FileInfo(path);
        var length = info.Length; // Throws for a file disappearing after attributes.
        return new(attributes, length, info.LastWriteTimeUtc, info.CreationTimeUtc);
    }
}
