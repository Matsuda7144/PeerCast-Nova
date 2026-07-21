using System.Diagnostics;
using System.Security.Cryptography;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderBackendInstalledRuntimeLocatorTests
{
    [WindowsFact]
    public async Task Locate_FiveFilesRemainHeldUntilLeaseDisposal()
    {
        using var directory = new TemporaryDirectory();
        var files = await WriteRuntimeAsync(directory.Path, 5);
        var result = await LocateAsync(directory.Path, files);

        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.Verified, result.Location);
        var lease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease);
        Assert.Equal(5, lease.FileCount);
        Assert.All(files, file => Assert.True(lease.ContainsFile(file.Name)));
        foreach (var file in files)
        {
            Assert.Throws<IOException>(() => File.Open(
                Path.Combine(directory.Path, file.Name),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None));
        }

        await lease.DisposeAsync();
        Assert.True(lease.IsDisposed);
    }

    [WindowsFact]
    public async Task Locate_TenFileReviewedBinInventoryRemainsHeldUntilLeaseDisposal()
    {
        using var directory = new TemporaryDirectory();
        var files = await WriteRuntimeAsync(directory.Path, 10);
        var result = await LocateAsync(directory.Path, files);

        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.Verified, result.Location);
        var lease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease);
        Assert.Equal(10, lease.FileCount);
        await lease.DisposeAsync();
    }

    [WindowsFact]
    public async Task Locate_CaseVariantUsesWindowsNameComparison()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "RUNTIME.DLL"), bytes);
        var inventory = CreateInventory(("runtime.dll", bytes));

        var result = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(directory.Path, inventory);

        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.Verified, result.Location);
        await Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease).DisposeAsync();
    }

    [WindowsFact]
    public async Task Locate_MissingAndUnexpectedFilesReturnFixedCategories()
    {
        using var directory = new TemporaryDirectory();
        var inventory = CreateInventory(("required.dll", new byte[] { 1 }));

        var missing = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(directory.Path, inventory);
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "required.dll"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "extra.txt"), [1]);
        var unexpected = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(directory.Path, inventory);

        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.MissingFile, missing.Location);
        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.UnexpectedFile,
            unexpected.Location);
    }

    [WindowsFact]
    public async Task Locate_MissingDirectoryAndUncPathAreRejectedWithoutDisclosure()
    {
        using var directory = new TemporaryDirectory();
        var inventory = CreateInventory(("required.dll", new byte[] { 1 }));
        var missing = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(Path.Combine(directory.Path, "private-7144"), inventory);
        var unc = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(@"\\private-7144\runtime", inventory);

        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable,
            missing.Location);
        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.PathRejected, unc.Location);
        Assert.DoesNotContain("private-7144", missing.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-7144", unc.ToString(), StringComparison.Ordinal);
    }

    [WindowsFact]
    public async Task Locate_DirectorySymlinkIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "target");
        var link = Path.Combine(directory.Path, "link");
        Directory.CreateDirectory(target);
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
            CreateNoWindow = true,
            UseShellExecute = false,
        }))
        {
            Assert.NotNull(process);
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        var inventory = CreateInventory(("required.dll", new byte[] { 1 }));

        var result = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(link, inventory);

        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.ReparsePointRejected,
            result.Location);
        Directory.Delete(link);
    }

    [WindowsTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Locate_OpenFailureReleasesEveryPreviouslyOpenedHandle(int failedFile)
    {
        using var directory = new TemporaryDirectory();
        var files = await WriteRuntimeAsync(directory.Path, 5);
        var failedPath = Path.Combine(directory.Path, files[failedFile - 1].Name);
        await using var blocking = new FileStream(
            failedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var result = await LocateAsync(directory.Path, files);

        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.DirectoryUnavailable,
            result.Location);
        Assert.Null(result.Lease);
        for (var index = 0; index < failedFile - 1; index++)
        {
            using var exclusive = File.Open(
                Path.Combine(directory.Path, files[index].Name),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
        }
    }

    [WindowsFact]
    public async Task Lease_DisposeIsIdempotentAndReuseIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var files = await WriteRuntimeAsync(directory.Path, 5);
        var result = await LocateAsync(directory.Path, files);
        var lease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease);

        lease.Dispose();
        lease.Dispose();
        await lease.DisposeAsync();

        Assert.True(lease.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => lease.ContainsFile(files[0].Name));
        using var exclusive = File.Open(
            Path.Combine(directory.Path, files[0].Name),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    [WindowsFact]
    public async Task Lease_PreventsVerifiedFileReplacementAndDeletion()
    {
        using var directory = new TemporaryDirectory();
        var files = await WriteRuntimeAsync(directory.Path, 5);
        var result = await LocateAsync(directory.Path, files);
        var lease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease);
        var path = Path.Combine(directory.Path, files[0].Name);

        Assert.Throws<IOException>(() => File.Delete(path));
        Assert.Throws<IOException>(() => File.Move(
            path,
            Path.Combine(directory.Path, "replacement-target.dll")));

        await lease.DisposeAsync();
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [WindowsFact]
    public async Task Lease_PreventsRuntimeDirectoryPathRebinding()
    {
        using var directory = new TemporaryDirectory();
        var runtime = Path.Combine(directory.Path, "runtime");
        var moved = Path.Combine(directory.Path, "moved");
        Directory.CreateDirectory(runtime);
        var files = await WriteRuntimeAsync(runtime, 5);
        var result = await LocateAsync(runtime, files);
        var lease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease);

        Assert.Throws<IOException>(() => Directory.Move(runtime, moved));

        await lease.DisposeAsync();
        Directory.Move(runtime, moved);
        Directory.Move(moved, runtime);
    }

    [WindowsFact]
    public async Task Locate_PreCancelledCallerEndsWithCancellation()
    {
        using var directory = new TemporaryDirectory();
        var files = await WriteRuntimeAsync(directory.Path, 5);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync(
                directory.Path,
                CreateInventory(files),
                cancellation.Token));
    }

    [WindowsFact]
    public async Task Locate_ResultAndLeaseDoNotExposePathHashManifestOrContent()
    {
        using var directory = new TemporaryDirectory("private-runtime-7144");
        var content = new byte[] { 71, 14, 4, 99 };
        var files = new[] { (Name: "private.dll", Bytes: content) };
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, files[0].Name), content);
        var hash = Convert.ToHexString(SHA256.HashData(content));

        var result = await LocateAsync(directory.Path, files);
        var lease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(result.Lease);
        var diagnostic = result + " " + lease;

        Assert.DoesNotContain(directory.Path, diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hash, diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4714499", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(VerifiedPlaybackDecoderRuntimeLease).GetProperties(),
            property => property.PropertyType == typeof(string));
        await lease.DisposeAsync();
    }

    [NonWindowsFact]
    public async Task Locate_NonWindowsPlatformFailsClosedBeforeFilesystemAccess()
    {
        var secretPath = "/private-runtime-7144/not-enumerated";
        var result = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(
                secretPath,
                CreateInventory(("runtime.dll", new byte[] { 1 })));

        Assert.Equal(PlaybackDecoderBackendInstalledRuntimeLocation.PathRejected, result.Location);
        Assert.Null(result.Lease);
        Assert.DoesNotContain(secretPath, result.ToString(), StringComparison.Ordinal);
    }

    private static async Task<PlaybackDecoderBackendInstalledRuntimeLeaseResult> LocateAsync(
        string path,
        params (string Name, byte[] Bytes)[] files) =>
        await PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync(
            path,
            CreateInventory(files));

    private static async Task<(string Name, byte[] Bytes)[]> WriteRuntimeAsync(
        string path,
        int count)
    {
        var files = Enumerable.Range(1, count)
            .Select(index => ($"runtime-{index}.dll", new byte[] { (byte)index }))
            .ToArray();
        foreach (var file in files)
        {
            await File.WriteAllBytesAsync(Path.Combine(path, file.Item1), file.Item2);
        }

        return files;
    }

    private static PlaybackDecoderBackendRuntimeInventory CreateInventory(
        params (string Name, byte[] Bytes)[] files) =>
        new(files.Select(static file => new PlaybackDecoderBackendRuntimeFile(
            file.Name,
            file.Bytes.Length,
            Convert.ToHexString(SHA256.HashData(file.Bytes)))));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string? suffix = null)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + suffix);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            var resolvedPath = System.IO.Path.GetFullPath(Path);
            var resolvedTemp = System.IO.Path.GetFullPath(
                System.IO.Path.GetTempPath()).TrimEnd(
                System.IO.Path.DirectorySeparatorChar);
            Assert.Equal(resolvedTemp, Directory.GetParent(resolvedPath)!.FullName);
            Directory.Delete(resolvedPath, recursive: true);
        }
    }
}
