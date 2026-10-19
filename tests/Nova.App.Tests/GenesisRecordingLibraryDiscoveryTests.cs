using System.Security;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingLibraryDiscoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Nova-library-discovery-tests", Guid.NewGuid().ToString("N"));
    private readonly GenesisRecordingLibraryKnownRootRegistry _roots = new();
    private readonly GenesisRecordingHistoryReadModel _history = new();
    private readonly FakeFileSystem _files = new();
    private string Root => Path.Combine(_directory, "root");
    private string OtherRoot => Path.Combine(_directory, "other");
    private static readonly DateTimeOffset Time = new(2026, 10, 7, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid Channel = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Session = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Rule = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static string CanonicalFilename => $"20261007_110000_name_with_underscores_{Channel:N}_{Session:N}.flv";
    private GenesisRecordingLibraryDiscovery Discovery() => new(_roots.GetSnapshot, _history.GetSnapshot, _files);

    [Fact]
    public async Task InitialStateAndObservedEmptyAreDistinctAndIdenticalRefreshIsNoOp()
    {
        AddRoot(Root);
        var discovery = Discovery();
        var initial = discovery.GetSnapshot();
        Assert.Equal(GenesisRecordingLibraryObservationState.NotObservedYet, initial.State);
        Assert.Equal(0, initial.Revision);
        Assert.False(initial.CoverageComplete);
        Assert.Equal(0, _files.EnumerationCount);
        var first = await discovery.RefreshAsync();
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Published, first.Status);
        Assert.Equal(GenesisRecordingLibraryObservationState.Observed, first.Snapshot.State);
        Assert.Empty(first.Snapshot.Entries);
        Assert.True(first.Snapshot.CoverageComplete);
        Assert.Equal(1, first.Snapshot.Revision);
        var next = await discovery.RefreshAsync();
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Unchanged, next.Status);
        Assert.Same(first.Snapshot, next.Snapshot);
    }

    [Theory]
    [InlineData("missing", nameof(GenesisRecordingLibraryAvailability.Missing), nameof(GenesisRecordingLibraryFailure.None))]
    [InlineData("access", nameof(GenesisRecordingLibraryAvailability.Unavailable), nameof(GenesisRecordingLibraryFailure.AccessDenied))]
    [InlineData("security", nameof(GenesisRecordingLibraryAvailability.Unavailable), nameof(GenesisRecordingLibraryFailure.AccessDenied))]
    [InlineData("io", nameof(GenesisRecordingLibraryAvailability.Unavailable), nameof(GenesisRecordingLibraryFailure.Io))]
    [InlineData("invalid", nameof(GenesisRecordingLibraryAvailability.Unavailable), nameof(GenesisRecordingLibraryFailure.InvalidPath))]
    public async Task RootErrorsAreTypedAndOtherRootResultsSurvive(string kind,
        string availability, string failure)
    {
        AddRoot(Root); AddRoot(OtherRoot);
        var valid = AddFile(OtherRoot, "valid.flv");
        _files.Enumerate = (root, _) => root == Root ? throw Failure(kind) : _files.Entries[root];
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(valid, Assert.Single(snapshot.Entries).Path);
        var failed = snapshot.Roots.Single(x => x.Path == Root);
        Assert.Equal(availability, failed.Status.ToString());
        Assert.Equal(failure, failed.Failure.ToString());
        Assert.False(snapshot.AllRegisteredRootsFullyObserved);
        Assert.Equal(availability == nameof(GenesisRecordingLibraryAvailability.Unavailable), snapshot.HasUnavailableRoots);
        Assert.DoesNotContain("sensitive", failed.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, snapshot.Roots.Single(x => x.Path == OtherRoot).IncludedEntryCount);
    }

    [Theory]
    [InlineData("regular.flv", FileAttributes.Normal, true)]
    [InlineData("regular.bin", FileAttributes.Normal, true)]
    [InlineData("unfinished.flv.partial", FileAttributes.Normal, false)]
    [InlineData("data.txt", FileAttributes.Normal, false)]
    [InlineData("data.json", FileAttributes.Normal, false)]
    [InlineData("data.exe", FileAttributes.Normal, false)]
    [InlineData("data.lnk", FileAttributes.Normal, false)]
    [InlineData("metadata.lock", FileAttributes.Normal, false)]
    [InlineData("history.tmp", FileAttributes.Normal, false)]
    [InlineData("directory.flv", FileAttributes.Directory, false)]
    [InlineData("link.flv", FileAttributes.ReparsePoint, false)]
    [InlineData("junction.flv", FileAttributes.Directory | FileAttributes.ReparsePoint, false)]
    public async Task OnlyDirectRegularFinalCandidatesAreIncluded(string filename, FileAttributes attributes, bool expected)
    {
        AddRoot(Root);
        var path = AddFile(Root, filename, attributes: attributes);
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(expected ? 1 : 0, snapshot.Entries.Count);
        Assert.Equal(expected ? 1 : 0, _files.ProbeCount);
        if (expected) Assert.Equal(path, Assert.Single(snapshot.Entries).Path);
    }

    [Fact]
    public async Task ChildAndPrefixSiblingEntriesCannotEscapeDirectRootBoundary()
    {
        AddRoot(Root);
        _files.Entries[Root].Add(new(Path.Combine(Root, "child", "nested.flv"), FileAttributes.Normal));
        _files.Entries[Root].Add(new(Path.Combine(Root + "-sibling", "outside.flv"), FileAttributes.Normal));
        Assert.Empty((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal(0, _files.ProbeCount);
    }

    [Theory]
    [InlineData("legacy.flv", nameof(GenesisRecordingLibraryContainer.FlvCandidate))]
    [InlineData("legacy.bin", nameof(GenesisRecordingLibraryContainer.UnknownContainerCandidate))]
    [InlineData("20269999_bad.flv", nameof(GenesisRecordingLibraryContainer.FlvCandidate))]
    public async Task LegacyContainerAndUnknownMetadataStayTruthful(string filename, string container)
    {
        AddRoot(Root); AddFile(Root, filename);
        var entry = Assert.Single((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal(container, entry.Container.ToString());
        Assert.Equal(GenesisRecordingLibraryLink.LegacyOrUnlinked, entry.Link);
        Assert.Empty(entry.HistoryFacts);
        Assert.Null(entry.FilenameClaims);
        Assert.Equal(123, entry.CurrentFileLength);
        Assert.False(entry.LengthMismatch);
        Assert.Equal(Time, entry.FileMetadata!.LastWriteTimeUtc);
        Assert.Equal(Time.AddDays(-1), entry.FileMetadata.CreatedTimeUtc);
        Assert.Empty(_history.GetSnapshot().Entries);
    }

    [Theory]
    [InlineData("flv")]
    [InlineData("bin")]
    [InlineData("mp4")]
    public void CanonicalAllocatorOutputParsesOnlyUnverifiedClaims(string extension)
    {
        var candidate = new RecordingCandidate(new(Channel), new ChannelMetadataSnapshot(name: "name_with_underscores", streamExtension: extension));
        var allocator = new DefaultRecordingFilePathProvider(Root, () => Time);
        var path = allocator.GetPath(RecordingSinkStartRequest.Manual(candidate), new(Session));
        Assert.True(GenesisRecordingLibraryFilenameParser.TryParse(Path.GetFileName(path), out var claims));
        Assert.Equal(Time, claims!.AllocationTimestampClaimUtc);
        Assert.Equal("name_with_underscores", claims.SanitizedChannelNameClaim);
        Assert.Equal(Channel, claims.ChannelIdClaim);
        Assert.Equal(Session, claims.SessionIdClaim);
        Assert.Empty(_history.GetSnapshot().Entries);
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("timestamp")]
    [InlineData("channel")]
    [InlineData("session")]
    [InlineData("emptyChannel")]
    [InlineData("emptySession")]
    [InlineData("missingName")]
    [InlineData("extension")]
    [InlineData("longExtension")]
    [InlineData("unsafeName")]
    [InlineData("empty")]
    public void MalformedFilenameReturnsNoClaims(string kind)
    {
        var filename = kind switch
        {
            "timestamp" => CanonicalFilename.Replace("20261007", "20261399", StringComparison.Ordinal),
            "channel" => CanonicalFilename.Replace(Channel.ToString("N"), "g" + Channel.ToString("N")[1..], StringComparison.Ordinal),
            "session" => CanonicalFilename.Replace(Session.ToString("N"), "bad", StringComparison.Ordinal),
            "emptyChannel" => CanonicalFilename.Replace(Channel.ToString("N"), Guid.Empty.ToString("N"), StringComparison.Ordinal),
            "emptySession" => CanonicalFilename.Replace(Session.ToString("N"), Guid.Empty.ToString("N"), StringComparison.Ordinal),
            "missingName" => CanonicalFilename.Replace("name_with_underscores", "", StringComparison.Ordinal),
            "extension" => CanonicalFilename.Replace(".flv", ".fl-v", StringComparison.Ordinal),
            "longExtension" => CanonicalFilename.Replace(".flv", ".abcdefghijkl", StringComparison.Ordinal),
            "unsafeName" => CanonicalFilename.Replace("name_with_underscores", "bad:name", StringComparison.Ordinal),
            _ => "",
        };
        Assert.False(GenesisRecordingLibraryFilenameParser.TryParse(filename, out var claims));
        Assert.Null(claims);
    }

    [Theory]
    [InlineData("dot")]
    [InlineData("case")]
    [InlineData("separator")]
    public async Task NormalizedPathIsIdentityAndPlatformCaseRulesArePreserved(string variation)
    {
        AddRoot(Root);
        var path = AddFile(Root, "identity.flv");
        var variant = variation switch
        {
            "dot" => Path.Combine(Root, "child", "..", "identity.flv"),
            "case" => path.Replace("identity.flv", "IDENTITY.flv", StringComparison.Ordinal),
            _ => path + Path.DirectorySeparatorChar,
        };
        _files.Entries[Root].Add(new(variant, FileAttributes.Normal));
        if (variation == "case" && !OperatingSystem.IsWindows()) _files.Metadata[variant] = Metadata();
        var entries = (await Discovery().RefreshAsync()).Snapshot.Entries;
        Assert.Equal(variation == "case" && !OperatingSystem.IsWindows() ? 2 : 1, entries.Count);
        Assert.All(entries, entry => Assert.True(Path.IsPathFullyQualified(entry.Path)));
    }

    [Fact]
    public async Task SameFilenameInTwoRootsAndSameTokensNeverCollapseOrAutojoin()
    {
        AddRoot(Root); AddRoot(OtherRoot);
        var original = AddFile(Root, CanonicalFilename);
        var copy = AddFile(OtherRoot, CanonicalFilename);
        var fact = Fact(original, automatic: true); _history.ObserveTerminal(fact);
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Equal(fact, Assert.Single(snapshot.Entries.Single(x => x.Path == original).HistoryFacts));
        var copied = snapshot.Entries.Single(x => x.Path == copy);
        Assert.Empty(copied.HistoryFacts);
        Assert.Equal(GenesisRecordingLibraryLink.LegacyOrUnlinked, copied.Link);
        Assert.Equal(Session, copied.FilenameClaims!.SessionIdClaim);
    }

    [Theory]
    [InlineData(false, 123)]
    [InlineData(true, 124)]
    public async Task ExactHistoryJoinRetainsActualFactsAndHistoricalBytesSeparateFromLength(bool automatic, long currentLength)
    {
        AddRoot(Root); var path = AddFile(Root, CanonicalFilename, length: currentLength);
        var fact = Fact(path, automatic); _history.ObserveTerminal(fact);
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        var entry = Assert.Single(snapshot.Entries);
        Assert.Same(fact, Assert.Single(entry.HistoryFacts));
        Assert.Equal(new RecordingSessionId(Session), entry.HistoryFacts[0].Session.SessionId);
        Assert.Equal(new ChannelId(Channel), entry.HistoryFacts[0].Session.ChannelId);
        Assert.Equal(automatic ? RecordingSessionOrigin.AutomaticRule : RecordingSessionOrigin.Manual, fact.Session.Origin);
        Assert.Equal(automatic ? Rule : (Guid?)null, fact.Session.AutomaticRuleId);
        Assert.Equal(123, fact.WriteResult!.BytesWritten);
        Assert.Equal(currentLength, entry.CurrentFileLength);
        Assert.Equal(currentLength != 123, entry.LengthMismatch);
        Assert.Equal(Time.AddHours(1), fact.TerminalObservedAtUtc);
        Assert.Equal(Time, entry.FileMetadata!.LastWriteTimeUtc);
        Assert.Equal(1, snapshot.ObservedLocationCount);
        Assert.Equal(1, snapshot.Roots[0].IncludedEntryCount);
    }

    [Theory]
    [InlineData("available", nameof(GenesisRecordingLibraryAvailability.Available))]
    [InlineData("fileMissing", nameof(GenesisRecordingLibraryAvailability.Missing))]
    [InlineData("missing", nameof(GenesisRecordingLibraryAvailability.Missing))]
    [InlineData("access", nameof(GenesisRecordingLibraryAvailability.Unavailable))]
    [InlineData("io", nameof(GenesisRecordingLibraryAvailability.Unavailable))]
    public async Task EveryOutsideRootHistoryPathIsProbedWithoutParentRegistration(string kind, string expected)
    {
        AddRoot(Root);
        var path = Path.Combine(OtherRoot, "past.mp4");
        var fact = Fact(path); _history.ObserveTerminal(fact);
        if (kind == "available") _files.Metadata[path] = Metadata();
        else _files.Read = (_, _) => throw Failure(kind);
        var roots = _roots.GetSnapshot(); var history = _history.GetSnapshot();
        var entry = Assert.Single((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal(path, entry.Path);
        Assert.Equal(expected, entry.Availability.ToString());
        Assert.Equal(GenesisRecordingLibraryContainer.UnsupportedExtension, entry.Container);
        Assert.Null(entry.SourceRoot);
        Assert.Same(fact, Assert.Single(entry.HistoryFacts));
        Assert.Equal(roots.Roots, _roots.GetSnapshot().Roots);
        Assert.Equal(roots.Revision, _roots.GetSnapshot().Revision);
        Assert.Equal(history.Entries, _history.GetSnapshot().Entries);
        Assert.Equal(1, _files.ProbeCount);
    }

    [Fact]
    public async Task HistoryWithoutWriterPathDoesNotFabricateLibraryLocation()
    {
        var fact = Fact(Path.Combine(Root, "unused.flv"));
        _history.ObserveTerminal(new(fact.Session, null, null, null, Time, Time,
            RecordingSinkSessionLifecycleKind.NoDecodableVideoStart));
        Assert.Empty((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal(0, _files.ProbeCount);
    }

    [Fact]
    public async Task MultipleHistoryFactsAtOneExactPathAreRetainedWithoutArbitraryWinner()
    {
        var path = Path.Combine(OtherRoot, "shared.bin");
        var first = Fact(path); var second = Fact(path, automatic: true, sessionId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        _history.ObserveTerminal(second); _history.ObserveTerminal(first);
        var entry = Assert.Single((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal([first, second], entry.HistoryFacts);
        Assert.Equal(GenesisRecordingLibraryAvailability.Missing, entry.Availability);
    }

    [Fact]
    public async Task FaultedHistoryWithWriterProvenFinalFileRemainsOrthogonalToAvailability()
    {
        var path = Path.Combine(OtherRoot, "faulted.flv"); _files.Metadata[path] = Metadata();
        var fact = Fact(path);
        var faulted = new RecordingSessionTerminalFact(fact.Session, fact.ChannelName, "FLV", "flv", Time, Time,
            RecordingSinkSessionLifecycleKind.Faulted, fact.WriteResult,
            RecordingFaultDescriptor.FromSafeDetails(RecordingFailureStage.Unknown, "IOException"));
        _history.ObserveTerminal(faulted);
        var entry = Assert.Single((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal(GenesisRecordingLibraryAvailability.Available, entry.Availability);
        Assert.Equal(RecordingSinkSessionLifecycleKind.Faulted, Assert.Single(entry.HistoryFacts).Kind);
    }

    [Fact]
    public async Task DeletedLinkedBecomesMissingAndDeletedLegacyDisappearsWithoutHistoryMutation()
    {
        AddRoot(Root);
        var linked = AddFile(Root, "linked.flv"); var legacy = AddFile(Root, "legacy.bin");
        _history.ObserveTerminal(Fact(linked)); var before = _history.GetSnapshot();
        var discovery = Discovery(); Assert.Equal(2, (await discovery.RefreshAsync()).Snapshot.Entries.Count);
        _files.Entries[Root].Clear(); _files.Metadata.Remove(linked); _files.Metadata.Remove(legacy);
        var after = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(GenesisRecordingLibraryAvailability.Missing, Assert.Single(after.Entries).Availability);
        Assert.Equal(linked, after.Entries[0].Path);
        Assert.Equal(before.Entries, _history.GetSnapshot().Entries);
        Assert.Equal(before.Revision, _history.GetSnapshot().Revision);
    }

    [Fact]
    public async Task RenamedFileLeavesOldHistoryMissingAndNewLocationUnlinked()
    {
        AddRoot(Root); var old = AddFile(Root, CanonicalFilename);
        _history.ObserveTerminal(Fact(old)); var discovery = Discovery(); await discovery.RefreshAsync();
        _files.Entries[Root].Clear(); _files.Metadata.Remove(old);
        var renamed = AddFile(Root, CanonicalFilename.Replace("name_with_underscores", "renamed", StringComparison.Ordinal));
        var after = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(2, after.Entries.Count);
        Assert.Equal(GenesisRecordingLibraryAvailability.Missing, after.Entries.Single(x => x.Path == old).Availability);
        Assert.Empty(after.Entries.Single(x => x.Path == renamed).HistoryFacts);
        Assert.Equal(Session, after.Entries.Single(x => x.Path == renamed).FilenameClaims!.SessionIdClaim);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisappearingMetadataDoesNotFailWholeRootOrFabricateLegacyMissing(bool historyLinked)
    {
        AddRoot(Root); var path = AddFile(Root, "disappeared.flv"); _files.Metadata.Remove(path);
        if (historyLinked) _history.ObserveTerminal(Fact(path));
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(GenesisRecordingLibraryAvailability.Available, snapshot.Roots[0].Status);
        if (historyLinked) Assert.Equal(GenesisRecordingLibraryAvailability.Missing, Assert.Single(snapshot.Entries).Availability);
        else Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public async Task UnreadableLegacyMetadataProducesCoverageGapNotMissing()
    {
        AddRoot(Root); AddFile(Root, "private.flv");
        _files.Read = (_, _) => throw new UnauthorizedAccessException("sensitive");
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, snapshot.Roots[0].UnavailableFileCount);
        Assert.False(snapshot.AllRegisteredRootsFullyObserved);
    }

    [Theory]
    [InlineData(FileAttributes.Directory, nameof(GenesisRecordingLibraryFailure.NotRegularFile))]
    [InlineData(FileAttributes.ReparsePoint, nameof(GenesisRecordingLibraryFailure.ReparsePoint))]
    public async Task HistoryProbeNeverPromotesDirectoriesOrReparseFilesToAvailable(FileAttributes attributes, string failure)
    {
        var path = Path.Combine(OtherRoot, "not-regular.flv");
        _history.ObserveTerminal(Fact(path)); _files.Metadata[path] = Metadata() with { Attributes = attributes };
        var entry = Assert.Single((await Discovery().RefreshAsync()).Snapshot.Entries);
        Assert.Equal(GenesisRecordingLibraryAvailability.Unavailable, entry.Availability);
        Assert.Equal(failure, entry.Failure.ToString());
        Assert.Null(entry.FileMetadata);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(10000)]
    [InlineData(10001)]
    public async Task BoundsRetainDeterministicLexicalWindowIndependentOfEnumerationOrder(int count)
    {
        AddRoot(Root);
        for (var i = count - 1; i >= 0; i--) AddFile(Root, $"{i:D5}.flv");
        var discovery = Discovery(); var first = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(Math.Min(count, 4096), first.Entries.Count);
        Assert.Equal(Math.Min(count, 10000), _files.ProbeCount);
        Assert.Equal(count, first.Roots[0].CandidateCount);
        Assert.Equal(count > 10000, first.HasTruncatedRoots);
        Assert.Equal(count > 4096, first.IsSnapshotTruncated);
        Assert.Equal(Math.Min(count, 10000), first.ObservedLocationCount);
        Assert.Equal(Path.Combine(Root, "00000.flv"), first.Entries[0].Path);
        Assert.Equal(Path.Combine(Root, $"{Math.Min(count, 4096) - 1:D5}.flv"), first.Entries[^1].Path);
        var beforeRoots = _roots.GetSnapshot();
        _files.Entries[Root].Reverse();
        var second = await discovery.RefreshAsync();
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Unchanged, second.Status);
        Assert.Same(first, second.Snapshot);
        Assert.Equal(beforeRoots.Revision, _roots.GetSnapshot().Revision);
        Assert.Empty(_history.GetSnapshot().Entries);
        Assert.Equal(count, _files.Metadata.Count);
    }

    [Fact]
    public async Task GlobalBoundCoversMultipleRootsAndExactHistoryPathsWithoutUnboundedEntryRetention()
    {
        AddRoot(Root); AddRoot(OtherRoot);
        for (var i = 0; i < 3000; i++) { AddFile(Root, $"{i:D5}.bin"); AddFile(OtherRoot, $"{i:D5}.flv"); }
        _history.ObserveTerminal(Fact(Path.Combine(_directory, "outside.flv")));
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(4096, snapshot.Entries.Count);
        Assert.Equal(6001, snapshot.ObservedLocationCount);
        Assert.True(snapshot.IsSnapshotTruncated);
        Assert.False(snapshot.HasTruncatedRoots);
        Assert.True(snapshot.AllRegisteredRootsFullyObserved);
        Assert.False(snapshot.CoverageComplete);
        Assert.Equal(snapshot.Entries.Count(x => x.SourceRoot is not null), snapshot.Roots.Sum(x => x.IncludedEntryCount));
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("length")]
    [InlineData("modified")]
    [InlineData("created")]
    [InlineData("history")]
    [InlineData("rootStatus")]
    [InlineData("provenance")]
    [InlineData("historyGap")]
    public async Task SemanticChangesAdvanceRevisionAndSnapshotsAreCopySafe(string kind)
    {
        AddRoot(Root); var path = AddFile(Root, "first.flv");
        var discovery = Discovery(); var first = (await discovery.RefreshAsync()).Snapshot;
        switch (kind)
        {
            case "added": AddFile(Root, "second.flv"); break;
            case "removed": _files.Entries[Root].Clear(); _files.Metadata.Remove(path); break;
            case "length": _files.Metadata[path] = Metadata(124); break;
            case "modified": _files.Metadata[path] = Metadata() with { LastWriteTimeUtc = Time.AddSeconds(1) }; break;
            case "created": _files.Metadata[path] = Metadata() with { CreatedTimeUtc = null }; break;
            case "history": _history.ObserveTerminal(Fact(path)); break;
            case "rootStatus": _files.Enumerate = (_, _) => throw new DirectoryNotFoundException(); break;
            case "provenance": _roots.Register(Root, GenesisRecordingRootProvenance.Default); break;
            case "historyGap": _history.RecordPublicationFailure(); break;
        }
        var second = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(first.Revision + 1, second.Revision);
        Assert.Single(first.Entries);
        Assert.Equal(123, first.Entries[0].CurrentFileLength);
        Assert.Empty(first.Entries[0].HistoryFacts);
        Assert.Throws<NotSupportedException>(() => ((IList<GenesisRecordingLibraryEntry>)first.Entries).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GenesisRecordingLibraryRootObservation>)first.Roots).Clear());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledRefreshPreservesPreviousPublication(bool alreadyObserved)
    {
        AddRoot(Root); AddFile(Root, "first.flv"); var discovery = Discovery();
        if (alreadyObserved) await discovery.RefreshAsync();
        var before = discovery.GetSnapshot();
        using var cancellation = new CancellationTokenSource();
        _files.Read = (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Metadata(); };
        var result = await discovery.RefreshAsync(cancellation.Token);
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Cancelled, result.Status);
        Assert.Same(before, result.Snapshot);
        Assert.Same(before, discovery.GetSnapshot());
        Assert.Equal(alreadyObserved ? GenesisRecordingLibraryObservationState.Observed : GenesisRecordingLibraryObservationState.NotObservedYet, result.Snapshot.State);
    }

    [Fact]
    public async Task SerializedRefreshCapturesNewerInputsAfterOlderScanAndNeverOverlaps()
    {
        AddRoot(Root); var path = AddFile(Root, "first.flv");
        var entered = Gate(); var release = Gate();
        var calls = 0;
        _files.Read = (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Metadata();
            }
            return Metadata(456);
        };
        var discovery = Discovery(); var first = discovery.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _history.ObserveTerminal(Fact(path));
        var second = discovery.RefreshAsync();
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.False(second.IsCompleted);
        release.TrySetResult();
        var a = await first; var b = await second;
        Assert.Empty(Assert.Single(a.Snapshot.Entries).HistoryFacts);
        Assert.Single(Assert.Single(b.Snapshot.Entries).HistoryFacts);
        Assert.Equal(456, b.Snapshot.Entries[0].CurrentFileLength);
        Assert.Equal(a.Snapshot.Revision + 1, b.Snapshot.Revision);
        Assert.Same(b.Snapshot, discovery.GetSnapshot());
    }

    [Fact]
    public async Task CancelledQueuedCallerDoesNotPermitLaterRefreshToOverlapActiveScan()
    {
        AddRoot(Root); AddFile(Root, "first.flv");
        var entered = Gate(); var release = Gate(); var calls = 0;
        _files.Read = (_, _) => { if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); } return Metadata(); };
        var discovery = Discovery(); var first = discovery.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var second = discovery.RefreshAsync(cancellation.Token); cancellation.Cancel();
        var third = discovery.RefreshAsync();
        Assert.Equal(1, Volatile.Read(ref calls)); release.TrySetResult();
        await first;
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Cancelled, (await second).Status);
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Unchanged, (await third).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProductionApplicationBoundaryIsInertAtStartupAndRefreshCannotWriteMetadataSettingsOrRules()
    {
        Directory.CreateDirectory(_directory);
        var storePath = Path.Combine(_directory, "history.json");
        var settingsPath = Path.Combine(_directory, "settings.json");
        var rulesPath = Path.Combine(_directory, "rules-sentinel.json");
        await File.WriteAllTextAsync(rulesPath, "rules unchanged");
        var settings = new GenesisRecordingOutputDirectorySettings(new(settingsPath), Root);
        var roots = new GenesisRecordingLibraryKnownRootRegistry(); roots.Register(Root,
            GenesisRecordingRootProvenance.Default | GenesisRecordingRootProvenance.StartupRecordingDirectory);
        var history = new GenesisRecordingHistoryReadModel(); var exact = Path.Combine(OtherRoot, "restored.flv"); history.ObserveTerminal(Fact(exact));
        var store = new GenesisRecordingHistoryStore(storePath);
        await store.SaveAsync(new(history.GetSnapshot(), roots.GetSnapshot()), null, default);
        var bytes = await File.ReadAllBytesAsync(storePath);
        _files.Entries[Root] = []; AddFile(Root, "legacy.bin"); _files.Metadata[exact] = Metadata();
        for (var restart = 0; restart < 2; restart++)
        {
            var baselineEnumerations = _files.EnumerationCount;
            var composition = new PcpRecordingRuntimeComposition(settings.CurrentRuntimeDirectory, store,
                defaultRecordingDirectory: settings.DefaultDirectory, libraryFileSystem: _files);
            var shared = new GenesisSharedPcpPlaybackRuntime(composition);
            var callbacks = 0;
            await using var daily = new NovaDailyHttpFlvPlaybackController(new PcrPlayerExternalPlayerLauncher((string?)null),
                _ => callbacks++, _ => callbacks++, composition, shared, new EmptyRules());
            Assert.Equal(GenesisRecordingLibraryObservationState.NotObservedYet, daily.GetRecordingLibrarySnapshot().State);
            Assert.Equal(baselineEnumerations, _files.EnumerationCount);
            Assert.Single(daily.GetRecordingHistorySnapshot().Entries);
            Assert.Single(daily.GetKnownRecordingRootsSnapshot().Roots);
            var result = await daily.RefreshRecordingLibraryAsync();
            Assert.Equal(2, result.Snapshot.Entries.Count);
            Assert.Same(result.Snapshot, daily.GetRecordingLibrarySnapshot());
            Assert.False(daily.GetRecordingHistoryDurabilitySnapshot()!.Pending);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(storePath));
            Assert.False(File.Exists(settingsPath));
            Assert.Equal("rules unchanged", await File.ReadAllTextAsync(rulesPath));
            Assert.Equal(Root, composition.RecordingDirectory);
            Assert.Equal(Root, settings.Presentation.CurrentRuntimeDirectory);
            Assert.Equal(0, callbacks);
            Assert.Empty(await composition.Recording.GetActiveRecordingSessionsAsync());
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
            Assert.False(Directory.Exists(Root));
            Assert.False(Directory.Exists(OtherRoot));
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(storePath));
    }

    [Fact]
    public async Task RealTestOwnedFilesystemIsNonrecursiveAndRebuildsAfterDeleteRenameAndCopy()
    {
        Directory.CreateDirectory(Path.Combine(Root, "nested"));
        var flv = Path.Combine(Root, CanonicalFilename); var bin = Path.Combine(Root, "legacy.bin");
        await File.WriteAllBytesAsync(flv, [1, 2, 3]); await File.WriteAllBytesAsync(bin, [4]);
        await File.WriteAllBytesAsync(Path.Combine(Root, "nested", "nested.flv"), [5]);
        await File.WriteAllBytesAsync(Path.Combine(Root, "active.flv.partial"), []);
        Directory.CreateDirectory(Path.Combine(Root, "directory.flv"));
        AddRoot(Root); _history.ObserveTerminal(Fact(flv));
        var discovery = new GenesisRecordingLibraryDiscovery(_roots.GetSnapshot, _history.GetSnapshot);
        var first = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(2, first.Entries.Count);
        Assert.Equal(3, first.Entries.Single(x => x.Path == flv).CurrentFileLength);
        Assert.Single(first.Entries.Single(x => x.Path == flv).HistoryFacts);
        var renamed = Path.Combine(Root, "renamed.flv"); File.Move(flv, renamed);
        File.Copy(renamed, Path.Combine(Root, "copy.flv")); File.Delete(bin);
        var second = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(3, second.Entries.Count);
        Assert.Equal(GenesisRecordingLibraryAvailability.Missing, second.Entries.Single(x => x.Path == flv).Availability);
        Assert.Empty(second.Entries.Single(x => x.Path == renamed).HistoryFacts);
        Assert.DoesNotContain(second.Entries, x => x.Path == bin);
        Assert.Single(_history.GetSnapshot().Entries);
    }

    [Fact]
    public async Task MissingRootBecomesAvailableWithoutDestinationOrRegistryChanges()
    {
        AddRoot(Root); var roots = _roots.GetSnapshot();
        _files.Enumerate = (_, _) => throw new DirectoryNotFoundException();
        var discovery = Discovery(); var before = (await discovery.RefreshAsync()).Snapshot;
        _files.Enumerate = null; AddFile(Root, "now.flv");
        var after = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(GenesisRecordingLibraryAvailability.Available, after.Roots[0].Status);
        Assert.True(after.CoverageComplete);
        Assert.Equal(roots.Revision, _roots.GetSnapshot().Revision);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public async Task MidEnumerationFailurePublishesOnlyObservedCandidatesWithIncompleteRootCoverage()
    {
        AddRoot(Root); AddRoot(OtherRoot);
        var path = AddFile(Root, "before-error.flv"); AddFile(OtherRoot, "good.bin");
        _files.Enumerate = (root, _) => root == Root ? FailAfter(_files.Entries[root][0]) : _files.Entries[root];
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Contains(snapshot.Entries, x => x.Path == path);
        Assert.Equal(GenesisRecordingLibraryAvailability.Unavailable, snapshot.Roots.Single(x => x.Path == Root).Status);
        Assert.False(snapshot.CoverageComplete);
    }

    private static IEnumerable<GenesisRecordingLibraryDirectoryEntry> FailAfter(GenesisRecordingLibraryDirectoryEntry first)
    {
        yield return first;
        throw new IOException("sensitive mid-enumeration failure");
    }

    [Theory]
    [InlineData("upperFlv")]
    [InlineData("upperBin")]
    public async Task CandidateExtensionMatchingUsesPlatformConventions(string kind)
    {
        AddRoot(Root); AddFile(Root, kind == "upperFlv" ? "legacy.FLV" : "legacy.BIN");
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, snapshot.Entries.Count);
    }

    [Fact]
    public async Task PartialHistoryPathIsExplicitlyExcludedRatherThanExposedAsFinalCandidate()
    {
        _history.ObserveTerminal(Fact(Path.Combine(OtherRoot, "unexpected.partial")));
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, snapshot.ExcludedHistoryPathCount);
        Assert.False(snapshot.CoverageComplete);
        Assert.Equal(0, _files.ProbeCount);
        Assert.Single(_history.GetSnapshot().Entries);
    }

    [Fact]
    public async Task PreCancelledRefreshDoesNoFilesystemWork()
    {
        AddRoot(Root); var discovery = Discovery(); var before = discovery.GetSnapshot();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await discovery.RefreshAsync(cancellation.Token);
        Assert.Equal(GenesisRecordingLibraryRefreshStatus.Cancelled, result.Status);
        Assert.Same(before, result.Snapshot);
        Assert.Equal(0, _files.EnumerationCount);
        Assert.Equal(0, _files.ProbeCount);
    }

    [Fact]
    public async Task RootTruncationCoverageChangeAdvancesRevisionEvenWhenDisplayedWindowIsIdentical()
    {
        AddRoot(Root);
        for (var i = 0; i < 10000; i++) AddFile(Root, $"{i:D5}.flv");
        var discovery = Discovery(); var before = (await discovery.RefreshAsync()).Snapshot;
        AddFile(Root, "99999.flv");
        var after = (await discovery.RefreshAsync()).Snapshot;
        Assert.Equal(before.Entries, after.Entries);
        Assert.False(before.HasTruncatedRoots);
        Assert.True(after.HasTruncatedRoots);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(10000, after.Roots[0].MetadataObservationCount);
    }

    [Fact]
    public async Task AllRegisteredRootsAreObservedAndFinalSnapshotStaysBounded()
    {
        for (var rootIndex = 0; rootIndex < 64; rootIndex++)
        {
            var root = Path.Combine(_directory, $"root-{rootIndex:D2}"); AddRoot(root);
            for (var i = 0; i < 65; i++) AddFile(root, $"{i:D3}.bin");
        }
        var snapshot = (await Discovery().RefreshAsync()).Snapshot;
        Assert.Equal(64, snapshot.Roots.Count);
        Assert.Equal(4096, snapshot.Entries.Count);
        Assert.Equal(4160, snapshot.ObservedLocationCount);
        Assert.True(snapshot.IsSnapshotTruncated);
        Assert.True(snapshot.AllRegisteredRootsFullyObserved);
        Assert.All(snapshot.Roots, root => Assert.Equal(65, root.MetadataObservationCount));
        Assert.Equal(64, _files.EnumerationCount);
    }

    private void AddRoot(string root) { _roots.RegisterExplicit(root); _files.Entries[root] = []; }
    private string AddFile(string root, string filename, long length = 123, FileAttributes attributes = FileAttributes.Normal)
    {
        var path = Path.Combine(root, filename);
        _files.Entries[root].Add(new(path, attributes)); _files.Metadata[path] = Metadata(length) with { Attributes = attributes };
        return path;
    }
    private static GenesisRecordingLibraryFileMetadata Metadata(long length = 123) => new(FileAttributes.Normal, length, Time, Time.AddDays(-1));
    private static RecordingSessionTerminalFact Fact(string path, bool automatic = false, Guid? sessionId = null) => new(
        automatic ? new RecordingSinkSession(new(sessionId ?? Session), new(Channel), Rule) : RecordingSinkSession.Manual(new(sessionId ?? Session), new(Channel)),
        "Original name", "FLV", "flv", Time, Time.AddHours(1), RecordingSinkSessionLifecycleKind.Completed,
        RecordingFileWriteResult.FromFinalizedFacts(path, 123, RecordingFileWriteCompletionKind.Stopped));
    private static Exception Failure(string kind) => kind switch
    {
        "missing" => new DirectoryNotFoundException("sensitive path"),
        "fileMissing" => new FileNotFoundException("sensitive path"),
        "access" => new UnauthorizedAccessException("sensitive path"),
        "security" => new SecurityException("sensitive path"),
        "invalid" => new NotSupportedException("sensitive path"),
        _ => new IOException("sensitive path"),
    };
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class FakeFileSystem : IGenesisRecordingLibraryFileSystem
    {
        public Dictionary<string, List<GenesisRecordingLibraryDirectoryEntry>> Entries { get; } = new(GenesisRecordingLibraryKnownRootRegistry.PathComparer);
        public Dictionary<string, GenesisRecordingLibraryFileMetadata> Metadata { get; } = new(GenesisRecordingLibraryKnownRootRegistry.PathComparer);
        public Func<string, CancellationToken, IEnumerable<GenesisRecordingLibraryDirectoryEntry>>? Enumerate { get; set; }
        public Func<string, CancellationToken, GenesisRecordingLibraryFileMetadata>? Read { get; set; }
        public int EnumerationCount { get; private set; }
        public int ProbeCount { get; private set; }
        public IEnumerable<GenesisRecordingLibraryDirectoryEntry> EnumerateDirectEntries(string root, CancellationToken token)
        { EnumerationCount++; return Enumerate?.Invoke(root, token) ?? Entries[root]; }
        public GenesisRecordingLibraryFileMetadata ReadFileMetadata(string path, CancellationToken token)
        {
            ProbeCount++;
            return Read?.Invoke(path, token) ?? (Metadata.TryGetValue(path, out var metadata) ? metadata : throw new FileNotFoundException());
        }
    }
    private sealed class EmptyRules : IGenesisRecordingRuleSetProvider
    {
        public ValueTask<RecordingRuleSet> GetSnapshotAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new RecordingRuleSet([]));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
