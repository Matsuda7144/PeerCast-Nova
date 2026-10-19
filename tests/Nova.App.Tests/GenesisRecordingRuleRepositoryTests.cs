using System.Security.Cryptography;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleRepositoryTests : IDisposable
{
    private static readonly string[] ConcurrentRuleNames = ["First", "Second"];

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCastNova-recording-rule-repository-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingRuleFileReturnsAvailableEmptySnapshot()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);

        var snapshot = await repository.GetSnapshotAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.MissingFile, snapshot.Status);
        Assert.True(snapshot.IsAvailable);
        Assert.Empty(Assert.IsType<RecordingRuleSet>(snapshot.Rules).Rules);
        Assert.Equal(GenesisRecordingRuleRevision.Missing, snapshot.Revision);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ExistingRulesLoadInPersistedOrderWithRevision()
    {
        var path = GetPath("recording-rules.json");
        var rules = new RecordingRuleSet([
            CreateRule("Zulu", priority: 3, isEnabled: false),
            CreateRule("Alpha", priority: -5, action: RecordingRuleAction.Ignore),
        ]);
        await RecordingRuleFileStore.SaveAsync(path, rules);
        var bytes = await File.ReadAllBytesAsync(path);
        await using var repository = new GenesisRecordingRuleRepository(path);

        var first = await repository.GetSnapshotAsync();
        var second = await repository.GetSnapshotAsync();
        var restored = Assert.IsType<RecordingRuleSet>(first.Rules);

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, first.Status);
        Assert.True(first.IsAvailable);
        Assert.Equal(GenesisRecordingRuleRevision.FromBytes(bytes), first.Revision);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(rules.Rules.Select(static rule => rule.Id),
            restored.Rules.Select(static rule => rule.Id));
        Assert.Equal(rules.Rules.Select(static rule => rule.Name),
            restored.Rules.Select(static rule => rule.Name));
        Assert.Equal(rules.Rules.Select(static rule => rule.Priority),
            restored.Rules.Select(static rule => rule.Priority));
        Assert.Equal(rules.Rules.Select(static rule => rule.Action),
            restored.Rules.Select(static rule => rule.Action));
        Assert.Equal(rules.Rules.Select(static rule => rule.IsEnabled),
            restored.Rules.Select(static rule => rule.IsEnabled));
    }

    [Fact]
    public async Task UnchangedRevisionSaveAppliesAndReturnsNewRevision()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var missing = await repository.GetSnapshotAsync();
        var proposed = new RecordingRuleSet([CreateRule("Saved")]);

        var result = await repository.SaveAsync(missing, proposed);

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Applied, result.Status);
        Assert.True(result.Succeeded);
        var saved = Assert.IsType<GenesisRecordingRuleRepositorySnapshot>(result.Snapshot);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, saved.Status);
        Assert.Equal(proposed.Rules.Select(static rule => rule.Id),
            Assert.IsType<RecordingRuleSet>(saved.Rules)
                .Rules.Select(static rule => rule.Id));
        Assert.NotEqual(GenesisRecordingRuleRevision.Missing, saved.Revision);

        var loaded = await RecordingRuleFileStore.LoadAsync(path);
        Assert.Equal(proposed.Rules.Select(static rule => rule.Id),
            loaded.Rules.Select(static rule => rule.Id));
    }

    [Fact]
    public async Task ExternalFileChangeReturnsConflictWithoutOverwrite()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var initial = await repository.GetSnapshotAsync();
        var first = new RecordingRuleSet([CreateRule("First")]);
        Assert.Equal(
            GenesisRecordingRuleRepositoryWriteStatus.Applied,
            (await repository.SaveAsync(initial, first)).Status);
        var snapshotA = await repository.GetSnapshotAsync();
        var external = new RecordingRuleSet([CreateRule("External")]);
        await RecordingRuleFileStore.SaveAsync(path, external);
        var externalBytes = await File.ReadAllBytesAsync(path);

        var result = await repository.SaveAsync(
            snapshotA,
            new RecordingRuleSet([CreateRule("Proposed")]));

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status);
        Assert.Equal("External", Assert.Single(
            Assert.IsType<RecordingRuleSet>(result.Snapshot!.Rules).Rules).Name);
        Assert.Equal(externalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ExternalFileCreatedAfterMissingSnapshotReturnsConflict()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var missing = await repository.GetSnapshotAsync();
        var external = new RecordingRuleSet([CreateRule("External")]);
        await RecordingRuleFileStore.SaveAsync(path, external);
        var externalBytes = await File.ReadAllBytesAsync(path);

        var result = await repository.SaveAsync(
            missing,
            new RecordingRuleSet([CreateRule("Proposed")]));

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status);
        Assert.Equal(externalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ExternalFileDeletedAfterLoadReturnsConflict()
    {
        var path = GetPath("recording-rules.json");
        await RecordingRuleFileStore.SaveAsync(
            path,
            new RecordingRuleSet([CreateRule("Existing")]));
        await using var repository = new GenesisRecordingRuleRepository(path);
        var existing = await repository.GetSnapshotAsync();
        File.Delete(path);

        var result = await repository.SaveAsync(
            existing,
            new RecordingRuleSet([CreateRule("Proposed")]));

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.MissingFile, result.Snapshot!.Status);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task FormattingOnlyExternalChangeReturnsConflict()
    {
        var path = GetPath("recording-rules.json");
        var originalRules = new RecordingRuleSet([CreateRule("Same")]);
        await RecordingRuleFileStore.SaveAsync(path, originalRules);
        await using var repository = new GenesisRecordingRuleRepository(path);
        var originalSnapshot = await repository.GetSnapshotAsync();
        var originalBytes = await File.ReadAllBytesAsync(path);
        var json = Encoding.UTF8.GetString(originalBytes);
        await File.WriteAllTextAsync(
            path,
            json.Replace("\n", "", StringComparison.Ordinal),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var formattedBytes = await File.ReadAllBytesAsync(path);
        var current = await repository.GetSnapshotAsync();

        var result = await repository.SaveAsync(
            originalSnapshot,
            new RecordingRuleSet([CreateRule("Proposed")]));

        Assert.NotEqual(originalSnapshot.Revision, current.Revision);
        Assert.Equal(
            GenesisRecordingRuleRevision.FromBytes(formattedBytes),
            current.Revision);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status);
        Assert.Equal(formattedBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ConcurrentAppWritesAreSerializedAndStaleWriterConflicts()
    {
        var path = GetPath("recording-rules.json");
        var firstSaveEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;
        await using var repository = new GenesisRecordingRuleRepository(
            path,
            async (savePath, rules, cancellationToken) =>
            {
                if (Interlocked.Increment(ref saveCount) == 1)
                {
                    firstSaveEntered.TrySetResult();
                    await releaseFirstSave.Task.WaitAsync(cancellationToken);
                }

                await RecordingRuleFileStore.SaveAsync(savePath, rules, cancellationToken);
            });
        var snapshot = await repository.GetSnapshotAsync();
        var firstRules = new RecordingRuleSet([CreateRule("First")]);
        var secondRules = new RecordingRuleSet([CreateRule("Second")]);

        var firstWrite = repository.SaveAsync(snapshot, firstRules).AsTask();
        await firstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondWrite = repository.SaveAsync(snapshot, secondRules).AsTask();
        Assert.Equal(1, Volatile.Read(ref saveCount));
        releaseFirstSave.TrySetResult();
        var results = await Task.WhenAll(firstWrite, secondWrite);

        Assert.Single(
            results,
            static result =>
                result.Status == GenesisRecordingRuleRepositoryWriteStatus.Applied);
        Assert.Single(
            results,
            static result =>
                result.Status == GenesisRecordingRuleRepositoryWriteStatus.Conflict);
        var persisted = await RecordingRuleFileStore.LoadAsync(path);
        Assert.Single(persisted.Rules);
        Assert.Contains(persisted.Rules[0].Name, ConcurrentRuleNames);
    }

    [Fact]
    public async Task SavePreservesRuleOrderAndPriority()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var missing = await repository.GetSnapshotAsync();
        var proposed = new RecordingRuleSet([
            CreateRule("Zulu", priority: 100),
            CreateRule("Alpha", priority: -100),
        ]);

        var saved = await repository.SaveAsync(missing, proposed);

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Applied, saved.Status);
        var loaded = await repository.GetSnapshotAsync();
        var restored = Assert.IsType<RecordingRuleSet>(loaded.Rules);
        Assert.Equal(proposed.Rules.Select(static rule => rule.Id),
            restored.Rules.Select(static rule => rule.Id));
        Assert.Equal(proposed.Rules.Select(static rule => rule.Priority),
            restored.Rules.Select(static rule => rule.Priority));
    }

    [Fact]
    public async Task SavePreservesUnsupportedByGuiButValidCoreCriteria()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var missing = await repository.GetSnapshotAsync();
        var channelId = new ChannelId(Guid.NewGuid());
        var criteria = new RecordingRuleCriteria(
            channelId: channelId,
            name: new RecordingTextPattern("Nova", RecordingTextMatchMode.Exact),
            genre: new RecordingTextPattern("music", RecordingTextMatchMode.Contains),
            description: new RecordingTextPattern("live show", RecordingTextMatchMode.Contains),
            contentType: new RecordingTextPattern("audio/test", RecordingTextMatchMode.Exact),
            streamType: new RecordingTextPattern("FLV", RecordingTextMatchMode.Regex),
            comment: new RecordingTextPattern("live", RecordingTextMatchMode.Regex),
            minimumBitrateKbps: 128,
            maximumBitrateKbps: 320);
        var proposed = new RecordingRuleSet([
            new(Guid.NewGuid(), "Preserve metadata", RecordingRuleAction.Record, criteria),
        ]);

        var saved = await repository.SaveAsync(missing, proposed);
        var loaded = await repository.GetSnapshotAsync();
        var restored = Assert.Single(Assert.IsType<RecordingRuleSet>(loaded.Rules).Rules);

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Applied, saved.Status);
        Assert.Equal(channelId, restored.Criteria.ChannelId);
        Assert.Equal("Nova", restored.Criteria.Name!.Value);
        Assert.Equal(RecordingTextMatchMode.Exact, restored.Criteria.Name.Mode);
        Assert.Equal("music", restored.Criteria.Genre!.Value);
        Assert.Equal(RecordingTextMatchMode.Contains, restored.Criteria.Genre.Mode);
        Assert.Equal("live show", restored.Criteria.Description!.Value);
        Assert.Equal(RecordingTextMatchMode.Contains, restored.Criteria.Description.Mode);
        Assert.Equal("audio/test", restored.Criteria.ContentType!.Value);
        Assert.Equal(RecordingTextMatchMode.Exact, restored.Criteria.ContentType.Mode);
        Assert.Equal("FLV", restored.Criteria.StreamType!.Value);
        Assert.Equal(RecordingTextMatchMode.Regex, restored.Criteria.StreamType.Mode);
        Assert.Equal("live", restored.Criteria.Comment!.Value);
        Assert.Equal(RecordingTextMatchMode.Regex, restored.Criteria.Comment.Mode);
        Assert.Equal(128, restored.Criteria.MinimumBitrateKbps);
        Assert.Equal(320, restored.Criteria.MaximumBitrateKbps);
    }

    [Fact]
    public async Task SavePreservesExplicitMatchAll()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var missing = await repository.GetSnapshotAsync();
        var proposed = new RecordingRuleSet([
            new(Guid.NewGuid(), "Explicit global", RecordingRuleAction.Ignore,
                RecordingRuleCriteria.MatchAll),
        ]);

        var saved = await repository.SaveAsync(missing, proposed);
        var loaded = await repository.GetSnapshotAsync();

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Applied, saved.Status);
        Assert.True(Assert.Single(
            Assert.IsType<RecordingRuleSet>(loaded.Rules).Rules).Criteria.IsMatchAll);
    }

    [Fact]
    public async Task MalformedFileIsUnavailableAndCannotBeImplicitlyOverwritten()
    {
        var path = GetPath("recording-rules.json");
        Directory.CreateDirectory(_directory);
        var malformed = Encoding.UTF8.GetBytes("{ malformed");
        await File.WriteAllBytesAsync(path, malformed);
        await using var repository = new GenesisRecordingRuleRepository(path);

        var snapshot = await repository.GetSnapshotAsync();
        var result = await repository.SaveAsync(
            snapshot,
            new RecordingRuleSet([CreateRule("Replacement")]));

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Invalid, snapshot.Status);
        Assert.False(snapshot.IsAvailable);
        Assert.Null(snapshot.Rules);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Unavailable, result.Status);
        Assert.Equal(malformed, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task UnsupportedVersionIsUnavailableAndCannotBeImplicitlyOverwritten()
    {
        var path = GetPath("recording-rules.json");
        Directory.CreateDirectory(_directory);
        var unsupported = Encoding.UTF8.GetBytes("{\"version\":99,\"rules\":[]}");
        await File.WriteAllBytesAsync(path, unsupported);
        await using var repository = new GenesisRecordingRuleRepository(path);

        var snapshot = await repository.GetSnapshotAsync();
        var result = await repository.SaveAsync(
            snapshot,
            new RecordingRuleSet([CreateRule("Replacement")]));

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.UnsupportedVersion, snapshot.Status);
        Assert.Equal(
            GenesisRecordingRuleRepositoryFailureKind.UnsupportedVersion,
            snapshot.FailureKind);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Unavailable, result.Status);
        Assert.Equal(unsupported, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task OversizedFileIsUnavailableAndPreserved()
    {
        var path = GetPath("recording-rules.json");
        Directory.CreateDirectory(_directory);
        var oversized = new byte[RecordingRuleFileStore.MaximumFileByteCount + 1];
        await File.WriteAllBytesAsync(path, oversized);
        var originalHash = SHA256.HashData(oversized);
        await using var repository = new GenesisRecordingRuleRepository(path);

        var snapshot = await repository.GetSnapshotAsync();
        var result = await repository.SaveAsync(
            snapshot,
            new RecordingRuleSet([CreateRule("Replacement")]));

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Invalid, snapshot.Status);
        Assert.Equal(GenesisRecordingRuleRepositoryFailureKind.TooLarge, snapshot.FailureKind);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Unavailable, result.Status);
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task ReadIoFailureReturnsUnavailableSnapshot()
    {
        var path = GetPath("directory-instead-of-file");
        Directory.CreateDirectory(path);
        await using var repository = new GenesisRecordingRuleRepository(path);

        var snapshot = await repository.GetSnapshotAsync();

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Unavailable, snapshot.Status);
        Assert.False(snapshot.IsAvailable);
        Assert.Null(snapshot.Rules);
        Assert.True(snapshot.FailureKind is
            GenesisRecordingRuleRepositoryFailureKind.Io or
            GenesisRecordingRuleRepositoryFailureKind.Permission);
    }

    [Fact]
    public async Task OversizedProposalIsInvalidAndDoesNotCreateFile()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        var missing = await repository.GetSnapshotAsync();
        var longPattern = new string('x', RecordingRuleFileStore.MaximumFileByteCount);
        var oversizedRule = new RecordingRule(
            Guid.NewGuid(),
            "Oversized",
            RecordingRuleAction.Record,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(longPattern, RecordingTextMatchMode.Contains)));

        var result = await repository.SaveAsync(
            missing,
            new RecordingRuleSet([oversizedRule]));

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Invalid, result.Status);
        Assert.Equal(GenesisRecordingRuleRepositoryFailureKind.TooLarge, result.FailureKind);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SaveFailureDoesNotPublishProposedSnapshot()
    {
        var path = GetPath("recording-rules.json");
        var existing = new RecordingRuleSet([CreateRule("Existing")]);
        await RecordingRuleFileStore.SaveAsync(path, existing);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var repository = new GenesisRecordingRuleRepository(
            path,
            static (_, _, _) => ValueTask.FromException(new IOException("synthetic")));
        await using (repository)
        {
            var snapshot = await repository.GetSnapshotAsync();
            var proposed = new RecordingRuleSet([CreateRule("Proposed")]);

            var result = await repository.SaveAsync(snapshot, proposed);

            Assert.Equal(
                GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed,
                result.Status);
            Assert.Equal("Existing", Assert.Single(
                Assert.IsType<RecordingRuleSet>(result.Snapshot!.Rules).Rules).Name);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
    }

    [Fact]
    public async Task CancelledReadAndSaveReturnBoundedResults()
    {
        var path = GetPath("recording-rules.json");
        await using var repository = new GenesisRecordingRuleRepository(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var read = await repository.GetSnapshotAsync(cancellation.Token);
        var save = await repository.SaveAsync(
            await repository.GetSnapshotAsync(),
            new RecordingRuleSet([CreateRule("Cancelled")]),
            cancellation.Token);

        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Cancelled, read.Status);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, save.Status);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SystemRulePathUsesSharedApplicationDirectory()
    {
        var local = Path.Combine(Path.GetTempPath(), "nova-local");

        Assert.Equal(
            Path.Combine(local, "PeerCast Nova", "recording-rules.json"),
            GenesisRecordingRuleFilePath.Resolve(local));
        Assert.Null(GenesisRecordingRuleFilePath.Resolve(null));
        Assert.Null(GenesisRecordingRuleFilePath.Resolve("  "));
    }

    [Fact]
    public async Task RepositoryShutdownDrainsInFlightWriteAndRejectsNewWrites()
    {
        var path = GetPath("recording-rules.json");
        var saveEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new GenesisRecordingRuleRepository(
            path,
            async (_, _, cancellationToken) =>
            {
                saveEntered.TrySetResult();
                await releaseSave.Task.WaitAsync(cancellationToken);
            });
        var missing = await repository.GetSnapshotAsync();
        var saveTask = repository.SaveAsync(
            missing,
            new RecordingRuleSet([CreateRule("In flight")])).AsTask();
        await saveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeTask = repository.DisposeAsync().AsTask();
        var saveResult = await saveTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        var afterShutdown = await repository.SaveAsync(
            missing,
            new RecordingRuleSet([CreateRule("After shutdown")]));
        var afterShutdownRead = await repository.GetSnapshotAsync();

        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, saveResult.Status);
        Assert.Equal(
            GenesisRecordingRuleRepositoryWriteStatus.Cancelled,
            afterShutdown.Status);
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Cancelled, afterShutdownRead.Status);
        Assert.Equal(
            GenesisRecordingRuleRepositoryFailureKind.ShuttingDown,
            afterShutdownRead.FailureKind);
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string GetPath(string fileName) => Path.Combine(_directory, fileName);

    private static RecordingRule CreateRule(
        string name,
        int priority = 0,
        bool isEnabled = true,
        RecordingRuleAction action = RecordingRuleAction.Record) =>
        new(
            Guid.NewGuid(),
            name,
            action,
            new RecordingRuleCriteria(
                name: new RecordingTextPattern(name, RecordingTextMatchMode.Contains)),
            priority,
            isEnabled);
}
