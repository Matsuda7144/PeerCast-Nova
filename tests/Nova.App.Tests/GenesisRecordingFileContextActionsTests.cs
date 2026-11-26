using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingFileContextActionsTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid ChannelGuid = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid SessionGuid = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly string SampleRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nova-rec-menu-tests"));

    [Fact]
    public void TryCaptureResolvesLibraryAndHistoryRowsAndRejectsOthers()
    {
        var libraryFile = Path.Combine(SampleRoot, "sample-lib.flv");
        var libraryEntry = new GenesisRecordingLibraryEntry(
            libraryFile,
            GenesisRecordingLibraryAvailability.Available,
            GenesisRecordingLibraryFailure.None,
            GenesisRecordingLibraryContainer.FlvCandidate,
            new(FileAttributes.Normal, 1024, Time, Time),
            SampleRoot,
            [],
            null);
        var libraryRow = new GenesisGuiNextNativeTabRow(
            new(string.Empty, string.Empty),
            null,
            RecordingLibrary: GenesisGuiNextRecordingLibraryProjection.CreateRow(libraryEntry));

        Assert.True(GenesisRecordingFileContextActions.TryCapture(libraryRow, out var libraryTarget));
        Assert.NotNull(libraryTarget);
        Assert.Equal(libraryFile, libraryTarget.FilePath);
        Assert.Equal("sample-lib.flv", libraryTarget.DisplayName);
        Assert.True(libraryTarget.IsLibraryRow);

        var historyFile = Path.Combine(SampleRoot, "sample-hist.flv");
        var historyFact = new RecordingSessionTerminalFact(
            RecordingSinkSession.Manual(new(SessionGuid), new(ChannelGuid)),
            "テストチャンネル",
            "FLV",
            "flv",
            Time,
            Time.AddMinutes(5),
            RecordingSinkSessionLifecycleKind.Completed,
            RecordingFileWriteResult.FromFinalizedFacts(historyFile, 2048, RecordingFileWriteCompletionKind.EndOfStream));
        var historySnapshot = new GenesisRecordingHistorySnapshot(1, [historyFact], 0, 0);
        var emptyLibrary = new GenesisRecordingLibrarySnapshot(0, GenesisRecordingLibraryObservationState.NotObservedYet, [], [], 0, false, 0, false);
        var historyProjectionRow = GenesisGuiNextRecordingHistoryProjection.CreateRows(historySnapshot, emptyLibrary).Single();
        var historyRow = new GenesisGuiNextNativeTabRow(
            new(string.Empty, string.Empty),
            null,
            RecordingHistory: historyProjectionRow);

        Assert.True(GenesisRecordingFileContextActions.TryCapture(historyRow, out var historyTarget));
        Assert.NotNull(historyTarget);
        Assert.Equal(historyFile, historyTarget.FilePath);
        Assert.Equal("sample-hist.flv", historyTarget.DisplayName);
        Assert.False(historyTarget.IsLibraryRow);

        var factWithoutPath = new RecordingSessionTerminalFact(
            RecordingSinkSession.Manual(new(SessionGuid), new(ChannelGuid)),
            "テストチャンネル",
            "FLV",
            "flv",
            Time,
            Time.AddMinutes(5),
            RecordingSinkSessionLifecycleKind.NoDecodableVideoStart,
            null);
        var noPathRow = new GenesisGuiNextNativeTabRow(
            new(string.Empty, string.Empty),
            null,
            RecordingHistory: GenesisGuiNextRecordingHistoryProjection.CreateRows(
                new GenesisRecordingHistorySnapshot(1, [factWithoutPath], 0, 0),
                emptyLibrary).Single());

        Assert.False(GenesisRecordingFileContextActions.TryCapture(noPathRow, out _));
        Assert.False(GenesisRecordingFileContextActions.TryCapture(
            new GenesisGuiNextNativeTabRow(new("Channel", "pcp://example/ch"), null),
            out _));
        Assert.False(GenesisRecordingFileContextActions.TryCapture(null, out _));
    }

    [Fact]
    public async Task ExecuteAsyncDispatchesPlayRevealCopyAndDeleteWithRefresh()
    {
        var path = Path.Combine(SampleRoot, "video.flv");
        var target = new GenesisRecordingFileContextTarget(path, "video.flv", IsLibraryRow: true);
        var effects = new FakeEffects { Exists = true, ConfirmDeleteResult = true };
        var refreshes = 0;

        Assert.True(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.PlayFile,
            effects,
            Refresh));
        Assert.Equal([path], effects.Played);

        Assert.True(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.RevealInExplorer,
            effects,
            Refresh));
        Assert.Equal([path], effects.Revealed);

        Assert.True(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.CopyPath,
            effects,
            Refresh));
        Assert.Equal([path], effects.Copied);
        Assert.Equal(0, refreshes);

        Assert.True(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.DeleteFile,
            effects,
            Refresh));
        Assert.Equal([path], effects.Deleted);
        Assert.Equal(1, refreshes);

        Task Refresh()
        {
            refreshes++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteAsyncSkipsDeletionWhenCancelledAndReportsMissingFile()
    {
        var path = Path.Combine(SampleRoot, "missing.flv");
        var target = new GenesisRecordingFileContextTarget(path, "missing.flv", IsLibraryRow: false);
        var refreshes = 0;

        var cancelledEffects = new FakeEffects { Exists = true, ConfirmDeleteResult = false };
        Assert.False(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.DeleteFile,
            cancelledEffects,
            () =>
            {
                refreshes++;
                return Task.CompletedTask;
            }));
        Assert.Empty(cancelledEffects.Deleted);
        Assert.Equal(0, refreshes);
        Assert.Empty(cancelledEffects.Errors);

        var missingEffects = new FakeEffects { Exists = false, ConfirmDeleteResult = true };
        Assert.False(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.PlayFile,
            missingEffects));
        Assert.False(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.RevealInExplorer,
            missingEffects));
        Assert.False(await GenesisRecordingFileContextActions.ExecuteAsync(
            target,
            GenesisRecordingFileContextCommand.DeleteFile,
            missingEffects));

        Assert.Empty(missingEffects.Played);
        Assert.Empty(missingEffects.Revealed);
        Assert.Empty(missingEffects.Deleted);
        Assert.Equal(3, missingEffects.Errors.Count);
    }

    private sealed class FakeEffects : IGenesisRecordingFileContextEffects
    {
        public bool Exists { get; init; }
        public bool ConfirmDeleteResult { get; init; }
        public List<string> Played { get; } = [];
        public List<string> Revealed { get; } = [];
        public List<string> Copied { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<(string Title, string Message)> Errors { get; } = [];

        public bool FileExists(string filePath) => Exists;
        public void PlayFile(string filePath) => Played.Add(filePath);
        public void RevealInExplorer(string filePath) => Revealed.Add(filePath);
        public void CopyPath(string filePath) => Copied.Add(filePath);
        public bool ConfirmDelete(string displayName, string filePath) => ConfirmDeleteResult;
        public void DeleteFile(string filePath) => Deleted.Add(filePath);
        public void ReportError(string title, string message) => Errors.Add((title, message));
    }
}
