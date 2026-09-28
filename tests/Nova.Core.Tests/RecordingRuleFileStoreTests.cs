using System.Text;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingRuleFileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-RecordingRuleFileStoreTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsRuleSet()
    {
        var path = GetPath("rules.json");
        var rule = CreateRule("Primary", priority: 50);
        var original = new RecordingRuleSet([rule]);

        await RecordingRuleFileStore.SaveAsync(
            path,
            original);

        var restored = await RecordingRuleFileStore.LoadAsync(path);

        var restoredRule = Assert.Single(restored.Rules);
        Assert.Equal(rule.Id, restoredRule.Id);
        Assert.Equal(rule.Name, restoredRule.Name);
        Assert.Equal(rule.Priority, restoredRule.Priority);
    }

    [Fact]
    public async Task Save_OverwritesExistingFileAndLeavesNoTemporaryFile()
    {
        var path = GetPath("rules.json");

        var first = new RecordingRuleSet(
            [CreateRule("First")]);
        var second = new RecordingRuleSet(
            [CreateRule("Second")]);

        await RecordingRuleFileStore.SaveAsync(path, first);
        await RecordingRuleFileStore.SaveAsync(path, second);

        var restored = await RecordingRuleFileStore.LoadAsync(path);

        Assert.Equal(
            "Second",
            Assert.Single(restored.Rules).Name);

        var files = Directory.GetFiles(directory);

        Assert.Equal([path], files);
    }

    [Fact]
    public async Task Save_PreCancelled_DoesNotCreateOrReplaceFile()
    {
        var path = GetPath("rules.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            path,
            "original",
            Encoding.UTF8);

        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await RecordingRuleFileStore.SaveAsync(
                path,
                new RecordingRuleSet([CreateRule("Replacement")]),
                source.Token));

        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(path));

        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task LoadIfExists_MissingFile_ReturnsNull()
    {
        var result = await RecordingRuleFileStore.LoadIfExistsAsync(
            GetPath("missing.json"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Load_EmptyFile_IsRejected()
    {
        var path = GetPath("empty.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(path, []);

        await Assert.ThrowsAsync<FormatException>(
            async () => await RecordingRuleFileStore.LoadAsync(path));
    }

    [Fact]
    public async Task Load_InvalidUtf8_IsRejected()
    {
        var path = GetPath("invalid-utf8.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            path,
            [0xC3, 0x28]);

        await Assert.ThrowsAsync<FormatException>(
            async () => await RecordingRuleFileStore.LoadAsync(path));
    }

    [Fact]
    public async Task Load_OversizedFile_IsRejectedBeforeJsonParsing()
    {
        var path = GetPath("oversized.json");
        Directory.CreateDirectory(directory);

        await using (var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
            stream.SetLength(
                RecordingRuleFileStore.MaximumFileByteCount + 1L);
        }

        await Assert.ThrowsAsync<FormatException>(
            async () => await RecordingRuleFileStore.LoadAsync(path));
    }

    [Fact]
    public async Task Load_MalformedJson_PreservesCodecFailure()
    {
        var path = GetPath("malformed.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            path,
            "{ not-json",
            new UTF8Encoding(false));

        await Assert.ThrowsAsync<FormatException>(
            async () => await RecordingRuleFileStore.LoadAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(
                directory,
                recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string GetPath(string fileName)
    {
        return Path.Combine(directory, fileName);
    }

    private static RecordingRule CreateRule(
        string name,
        int priority = 0)
    {
        return new RecordingRule(
            Guid.NewGuid(),
            name,
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(
                    "Nova",
                    RecordingTextMatchMode.Contains)),
            priority);
    }
}
