using System.Collections.Immutable;
using System.Text.Json;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingSettingsTests
{
    private static PublishingSettingsInput Input => PublishingSettingsInput.From(PublishingChannelPreferences.Default);

    [Fact]
    public void DefaultsAndNormalizedNameProduceFixedFlvMetadata()
    {
        var values = (Input with { ChannelName = "  日本語 🎥  ", BitrateKbps = "2147483647" }).Validate();
        var metadata = values.ToOptions().CreateMetadata();
        Assert.Equal("日本語 🎥", metadata.Name);
        Assert.Equal("FLV", metadata.ContentType);
        Assert.Equal("flv", metadata.StreamExtension);
        Assert.Equal(int.MaxValue, metadata.BitrateKbps);
        Assert.Null(PublishingChannelPreferences.Default.BitrateKbps);
        Assert.Equal("Local publish", PublishingChannelPreferences.Default.ChannelName);
        Assert.Null(metadata.Track);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\u2028b")]
    public void InvalidNamesNeverBecomePreferences(string name) =>
        Assert.Throws<ArgumentException>(() => (Input with { ChannelName = name }).Validate());

    [Fact]
    public void ScalarLengthAndUtf8PayloadAreSeparateLimits()
    {
        Assert.Throws<ArgumentException>(() => (Input with { ChannelName = new string((char)0xd800, 1) }).Validate());
        Assert.Equal(256, (Input with { ChannelName = new string('あ', 256) }).Validate().ChannelName.Length);
        Assert.Equal(256, (Input with { ChannelName = new string('a', 256) + new string(' ', 4096) }).Validate().ChannelName.Length);
        Assert.Throws<ArgumentException>(() => (Input with { ChannelName = new string('a', 257) }).Validate());
        Assert.Equal(256, (Input with { Genre = string.Concat(Enumerable.Repeat("🎥", 128)) }).Validate().Genre.Length);
        Assert.Throws<ArgumentException>(() => (Input with { Genre = new string('あ', 129) }).Validate());
        Assert.Equal(1024, (Input with { Description = new string('あ', 1024) }).Validate().Description.Length);
        Assert.Throws<ArgumentException>(() => (Input with { Description = new string('あ', 1025) }).Validate());
        Assert.Equal(2046, (Input with { Comment = string.Concat(Enumerable.Repeat("🎥", 1023)) }).Validate().Comment.Length);
        Assert.Throws<ArgumentException>(() => (Input with { Comment = string.Concat(Enumerable.Repeat("🎥", 1024)) }).Validate());
        Assert.Throws<ArgumentException>(() => (Input with { Genre = "\udfff" }).Validate());
        Assert.Throws<ArgumentException>(() => (Input with { Comment = "line\rbreak" }).Validate());
        const string prefix = "https://example.invalid/";
        Assert.Equal(2048, (Input with { ContactUrl = prefix + new string('a', 2048 - prefix.Length) }).Validate().ContactUrl.Length);
        Assert.Throws<ArgumentException>(() => (Input with { ContactUrl = prefix + new string('a', 2049 - prefix.Length) }).Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.invalid/日本語")]
    [InlineData("http://example.invalid/path")]
    public void AcceptsOptionalAbsoluteWebUrl(string url) =>
        Assert.Equal(url, (Input with { ContactUrl = url }).Validate().ContactUrl);

    [Theory]
    [InlineData("ftp://example.invalid/")]
    [InlineData("/relative")]
    [InlineData("https://user:password@example.invalid/")]
    [InlineData("https://example.invalid/\n")]
    [InlineData(" https://example.invalid/")]
    public void RejectsInvalidContactUrl(string url) =>
        Assert.Throws<ArgumentException>(() => (Input with { ContactUrl = url }).Validate());

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("2147483648")]
    [InlineData("1.5")]
    [InlineData(" 1")]
    public void RejectsNonDeclaredIntegerBitrate(string bitrate) =>
        Assert.Throws<ArgumentException>(() => (Input with { BitrateKbps = bitrate }).Validate());

    [Fact]
    public async Task AtomicRoundTripPreservesUnknownSupportedPropertiesAndOnlyOwnFile()
    {
        using var directory = new TestDirectory();
        var path = directory.File;
        var store = new JsonPublishingPreferencesStore(path);
        Assert.Equal(PublishingSettingsSnapshot.Default, await store.LoadAsync());
        await File.WriteAllTextAsync(path, """{"SchemaVersion":1,"SettingsRevision":4,"ChannelName":"名前","FutureHint":{"n":7}}""");
        var loaded = await store.LoadAsync();
        var snapshot = loaded with { SettingsRevision = 5, Values = loaded.Values with { Genre = "音楽 🎥", BitrateKbps = 1200 } };
        await store.SaveAsync(snapshot);
        var restored = await store.LoadAsync();
        Assert.Equal(snapshot.Values, restored.Values);
        Assert.Equal(5, restored.SettingsRevision);
        Assert.Equal(7, restored.Extensions["FutureHint"].GetProperty("n").GetInt32());
        Assert.Single(Directory.GetFiles(directory.Path));
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("StreamKey", json);
        Assert.DoesNotContain("Server", json);
    }

    [Theory]
    [InlineData("""{"SchemaVersion":2,"Lan":{"Enabled":false,"Address":null}}""")]
    [InlineData("""{"SchemaVersion":2,"Lan":{"Enabled":false,"InterfaceId":null}}""")]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"SchemaVersion":2,"ChannelName":"future"}""")]
    [InlineData("""{"SchemaVersion":1,"ChannelName":null}""")]
    [InlineData("""{"SchemaVersion":1,"ChannelName":"first","ChannelName":"second"}""")]
    public async Task UntrustedFilesAreRetainedAndStartNeedsExplicitValidatedSave(string json)
    {
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(directory.File, json);
        await using var settings = new PublishingSettingsController(new JsonPublishingPreferencesStore(directory.File));
        await settings.LoadAsync();
        Assert.True(settings.LoadFailed);
        Assert.False(settings.CanStart);
        Assert.Equal(json, await File.ReadAllTextAsync(directory.File));
        using var editor = new PublishingSettingsEditor(settings);
        Assert.Contains("置き換え", editor.Result.Message);
        await editor.SaveAsync();
        Assert.True(editor.Result.Saved);
        Assert.True(settings.CanStart);
        Assert.Equal(1, settings.Saved.SettingsRevision);
    }

    [Fact]
    public async Task OversizedJsonAndExtensionsDoNotOverwriteExistingFile()
    {
        using var directory = new TestDirectory();
        var json = new string(' ', JsonPublishingPreferencesStore.MaximumBytes + 1);
        await File.WriteAllTextAsync(directory.File, json);
        var store = new JsonPublishingPreferencesStore(directory.File);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync().AsTask());
        using var element = JsonDocument.Parse(JsonSerializer.Serialize(new string('a', JsonPublishingPreferencesStore.MaximumBytes)));
        var snapshot = PublishingSettingsSnapshot.Default with
        { Extensions = ImmutableDictionary<string, JsonElement>.Empty.Add("Large", element.RootElement.Clone()) };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(snapshot).AsTask());
        Assert.Equal(json, await File.ReadAllTextAsync(directory.File));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplaceFailureOrCancellationLeavesFileSavedAppliedAndRevisionIntact(bool cancel)
    {
        using var directory = new TestDirectory();
        using var cancellation = new CancellationTokenSource();
        var initial = PublishingSettingsSnapshot.Default with { SettingsRevision = 8 };
        await new JsonPublishingPreferencesStore(directory.File).SaveAsync(initial);
        var bytes = await File.ReadAllBytesAsync(directory.File);
        var store = new JsonPublishingPreferencesStore(directory.File, () =>
        {
            if (cancel) cancellation.Cancel();
            else throw new IOException("replace failed");
        });
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        settings.MarkApplied(settings.Saved);
        var applied = settings.Applied;
        using var editor = new PublishingSettingsEditor(settings);
        editor.Edit(Input with { ChannelName = "draft" });
        await editor.SaveAsync(cancellation.Token);
        Assert.False(editor.Result.Saved);
        Assert.Equal("draft", editor.Input.ChannelName);
        Assert.Equal(initial.Values, settings.Saved.Values);
        Assert.Equal(8, settings.Saved.SettingsRevision);
        Assert.Same(applied, settings.Applied);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(directory.File));
        Assert.Single(Directory.GetFiles(directory.Path));
        Assert.False(settings.CanStart);
        editor.Dispose();
        Assert.True(settings.CanStart);
    }

    [Fact]
    public async Task TempCreationFailureDoesNotCommitSaved()
    {
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(directory.File, "not a directory");
        await using var settings = new PublishingSettingsController(new JsonPublishingPreferencesStore(System.IO.Path.Combine(directory.File, "settings.json")));
        await settings.LoadAsync();
        Assert.False((await settings.SaveAsync(Input)).Saved);
        Assert.Equal(0, settings.Saved.SettingsRevision);
        Assert.False(settings.LoadFailed); // Missing child file loaded defaults; failed Save does not change trust.
    }

    [Fact]
    public async Task ActualMoveFailureRetainsDestinationAndRemovesSameDirectoryTemp()
    {
        using var directory = new TestDirectory();
        Directory.CreateDirectory(directory.File);
        await using var settings = new PublishingSettingsController(new JsonPublishingPreferencesStore(directory.File));
        await settings.LoadAsync();
        Assert.False((await settings.SaveAsync(Input)).Saved);
        Assert.Equal(0, settings.Saved.SettingsRevision);
        Assert.True(Directory.Exists(directory.File));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task ConcurrentSavesSerializeRevisionsAndRetainApplied()
    {
        var store = new MemoryStore { BlockSave = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        settings.MarkApplied(settings.Saved);
        var applied = settings.Applied;
        var first = settings.SaveAsync(Input with { ChannelName = "first" });
        await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = settings.SaveAsync(Input with { ChannelName = "second" });
        Assert.False(settings.CanStart);
        store.ReleaseSave.TrySetResult();
        Assert.All(await Task.WhenAll(first, second), result => Assert.True(result.Saved));
        Assert.Equal(2, settings.Saved.SettingsRevision);
        Assert.Equal("second", settings.Saved.Values.ChannelName);
        Assert.Same(applied, settings.Applied);
    }

    [Fact]
    public async Task DraftCancelValidationFailureSingleEditorAndPendingSaveProtectStart()
    {
        var store = new MemoryStore { BlockSave = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        using var editor = new PublishingSettingsEditor(settings);
        Assert.Throws<InvalidOperationException>(() => new PublishingSettingsEditor(settings));
        editor.Edit(Input with { ChannelName = "" });
        Assert.False(settings.CanStart);
        await editor.SaveAsync();
        Assert.Equal("ChannelName", editor.Result.Field);
        Assert.Equal(0, settings.Saved.SettingsRevision);
        editor.Edit(Input with { ChannelName = "  saved  " });
        var saving = editor.SaveAsync();
        await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(settings.IsSaving);
        Assert.False(settings.TryCaptureForStart(out _));
        Assert.Equal("Local publish", settings.Saved.Values.ChannelName);
        editor.Edit(Input with { ChannelName = "newer draft" });
        store.ReleaseSave.TrySetResult();
        await saving;
        Assert.Equal("saved", settings.Saved.Values.ChannelName);
        Assert.Equal("newer draft", editor.Input.ChannelName);
        Assert.False(settings.CanStart);
        editor.Dispose(); // Cancel and X share this discard/lifetime boundary.
        Assert.True(settings.CanStart);
        using var reopened = new PublishingSettingsEditor(settings);
        Assert.Equal("saved", reopened.Input.ChannelName);
    }

    [Fact]
    public async Task ClosingEditorDuringSaveCannotReopenDraftAndRootJoinsPendingIo()
    {
        var store = new MemoryStore { BlockSave = true };
        var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var editor = new PublishingSettingsEditor(settings);
        editor.Edit(Input with { ChannelName = "saved later" });
        var saving = editor.SaveAsync();
        await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        editor.Dispose();
        var close = settings.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        store.ReleaseSave.TrySetResult();
        await Task.WhenAll(saving, close);
        Assert.False(settings.CanStart);
        Assert.Equal("saved later", settings.Saved.Values.ChannelName);
        Assert.Throws<ObjectDisposedException>(() => new PublishingSettingsEditor(settings));
    }

    [Fact]
    public async Task ReopenedEditorCannotEnableStartWithStaleDraftAfterEarlierSaveCommits()
    {
        var store = new MemoryStore { BlockSave = true };
        await using var settings = new PublishingSettingsController(store);
        await settings.LoadAsync();
        var first = new PublishingSettingsEditor(settings);
        first.Edit(Input with { ChannelName = "earlier save" });
        var saving = first.SaveAsync();
        await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        first.Dispose();
        using var reopened = new PublishingSettingsEditor(settings);
        Assert.Equal("Local publish", reopened.Input.ChannelName);
        store.ReleaseSave.TrySetResult();
        await saving;
        Assert.Equal("earlier save", settings.Saved.Values.ChannelName);
        Assert.Equal("Local publish", reopened.Input.ChannelName);
        Assert.False(settings.CanStart);
        Assert.False(settings.TryCaptureForStart(out _));
        reopened.Dispose();
        Assert.True(settings.CanStart);
        using var latest = new PublishingSettingsEditor(settings);
        Assert.Equal("earlier save", latest.Input.ChannelName);
    }

    internal sealed class MemoryStore : IPublishingPreferencesStore
    {
        internal PublishingSettingsSnapshot Snapshot = PublishingSettingsSnapshot.Default;
        internal bool BlockSave;
        internal TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PublishingSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
        public async ValueTask SaveAsync(PublishingSettingsSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SaveEntered.TrySetResult();
            if (BlockSave) await ReleaseSave.Task.WaitAsync(cancellationToken);
            Snapshot = snapshot;
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NovaPublishingSettingsTests", Guid.NewGuid().ToString("N"));
        internal string File => System.IO.Path.Combine(Path, "publishing-preferences.json");
        internal TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
