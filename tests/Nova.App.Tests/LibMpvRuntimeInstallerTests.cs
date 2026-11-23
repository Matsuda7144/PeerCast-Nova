using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class LibMpvRuntimeInstallerTests
{
    [Fact]
    public async Task ProvisionAsyncFetchesVerifiesSha256AndExtractsLibrary()
    {
        var zipBytes = CreateZipBytes(
            ("libmpv-2.dll", "fake-libmpv-binary"u8.ToArray()),
            ("LICENSE.LGPL", "LGPL text"u8.ToArray()),
            ("docs/ignored.txt", "ignored"u8.ToArray()));
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        var tempDir = Directory.CreateTempSubdirectory("nova-libmpv-setup-");
        try
        {
            using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });
            var provisioner = new LibMpvRuntimeProvisioner("https://example.test/libmpv.zip", expectedSha256, handler);
            var stages = new List<LibMpvSetupStage>();
            var progress = new SyncProgress(p => stages.Add(p.Stage));

            var result = await provisioner.ProvisionAsync(tempDir.FullName, progress, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.False(result.AlreadyPresent);
            Assert.Equal(Path.Combine(tempDir.FullName, "libmpv-2.dll"), result.LibraryPath);
            Assert.True(File.Exists(result.LibraryPath));
            Assert.True(File.Exists(Path.Combine(tempDir.FullName, "LICENSE.LGPL")));
            Assert.False(File.Exists(Path.Combine(tempDir.FullName, "ignored.txt")));
            Assert.Contains(LibMpvSetupStage.Fetching, stages);
            Assert.Contains(LibMpvSetupStage.Verifying, stages);
            Assert.Contains(LibMpvSetupStage.Extracting, stages);

            var second = await provisioner.ProvisionAsync(tempDir.FullName, progress, CancellationToken.None);
            Assert.True(second.Succeeded);
            Assert.True(second.AlreadyPresent);
            Assert.Equal(result.LibraryPath, second.LibraryPath);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ProvisionAsyncRejectsSha256MismatchAndCleansUpTempFiles()
    {
        var zipBytes = CreateZipBytes(("libmpv-2.dll", "tampered"u8.ToArray()));
        var tempDir = Directory.CreateTempSubdirectory("nova-libmpv-mismatch-");
        try
        {
            using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });
            var provisioner = new LibMpvRuntimeProvisioner(
                "https://example.test/libmpv.zip",
                new string('0', 64),
                handler);

            var result = await provisioner.ProvisionAsync(tempDir.FullName, null, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("SHA-256", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(tempDir.FullName));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ProvisionAsyncRejectsZipSlipTraversalEntries()
    {
        var zipBytes = CreateZipBytes(("../evil.dll", "evil"u8.ToArray()), ("libmpv-2.dll", "ok"u8.ToArray()));
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        var tempDir = Directory.CreateTempSubdirectory("nova-libmpv-slip-");
        try
        {
            using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });
            var provisioner = new LibMpvRuntimeProvisioner("https://example.test/libmpv.zip", expectedSha256, handler);

            var result = await provisioner.ProvisionAsync(tempDir.FullName, null, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("不正なパス", result.ErrorMessage, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(tempDir.FullName, "libmpv-2.dll")));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryFindAvailableLibraryPathPrefersAppDirectoryThenRuntimeDirectory()
    {
        var appDir = Directory.CreateTempSubdirectory("nova-appdir-");
        var runtimeDir = Directory.CreateTempSubdirectory("nova-runtimedir-");
        try
        {
            Assert.Null(LibMpvRuntimeProvisioner.TryFindAvailableLibraryPath(appDir.FullName, runtimeDir.FullName));

            var runtimeDll = Path.Combine(runtimeDir.FullName, "libmpv-2.dll");
            File.WriteAllBytes(runtimeDll, [1]);
            Assert.Equal(runtimeDll, LibMpvRuntimeProvisioner.TryFindAvailableLibraryPath(appDir.FullName, runtimeDir.FullName));

            var appDll = Path.Combine(appDir.FullName, "libmpv-2.dll");
            File.WriteAllBytes(appDll, [2]);
            Assert.Equal(appDll, LibMpvRuntimeProvisioner.TryFindAvailableLibraryPath(appDir.FullName, runtimeDir.FullName));
        }
        finally
        {
            appDir.Delete(recursive: true);
            runtimeDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void DescribeAvailabilityDistinguishesInstalledDetectedAndMissingStates()
    {
        var installed = LibMpvRuntimeProvisioner.DescribeAvailability(
            draftPath: @"C:\mpv\libmpv-2.dll",
            savedPath: @"C:\mpv\libmpv-2.dll",
            detectedPath: null,
            fileExists: static p => p == @"C:\mpv\libmpv-2.dll");
        Assert.True(installed.ConfiguredExists);
        Assert.Contains("導入済み", installed.FieldLabel, StringComparison.Ordinal);
        Assert.Contains("導入済み", installed.ButtonText, StringComparison.Ordinal);
        Assert.Equal("導入済み", installed.SavedStateBadge);

        var detected = LibMpvRuntimeProvisioner.DescribeAvailability(
            draftPath: "",
            savedPath: "",
            detectedPath: @"C:\runtime\libmpv-2.dll",
            fileExists: static p => p == @"C:\runtime\libmpv-2.dll");
        Assert.False(detected.ConfiguredExists);
        Assert.True(detected.DetectedPresent);
        Assert.Contains("導入済み", detected.FieldLabel, StringComparison.Ordinal);
        Assert.Contains("適用", detected.ButtonText, StringComparison.Ordinal);

        var missing = LibMpvRuntimeProvisioner.DescribeAvailability(
            draftPath: "",
            savedPath: "",
            detectedPath: null,
            fileExists: static _ => false);
        Assert.False(missing.ConfiguredExists);
        Assert.False(missing.DetectedPresent);
        Assert.Contains("未導入", missing.FieldLabel, StringComparison.Ordinal);
        Assert.Contains("自動導入", missing.ButtonText, StringComparison.Ordinal);
        Assert.Equal("未導入", missing.SavedStateBadge);
    }

    private static byte[] CreateZipBytes(params (string EntryName, byte[] Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return memory.ToArray();
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class SyncProgress(Action<LibMpvSetupProgress> onReport) : IProgress<LibMpvSetupProgress>
    {
        public void Report(LibMpvSetupProgress value) => onReport(value);
    }
}
