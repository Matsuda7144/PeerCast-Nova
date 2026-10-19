using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingLibraryKnownRootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Nova-known-roots-tests", Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_root, "metadata", "history.json");
    private string DefaultRoot => Path.Combine(_root, "default");
    private string CustomRoot => Path.Combine(_root, "custom");
    private GenesisRecordingHistoryStore Store => new(StorePath);
    private NovaSettingsStore SettingsStore => new(Path.Combine(_root, "settings.json"));
    private GenesisRecordingOutputDirectorySettings Settings() => new(SettingsStore, DefaultRoot);
    private static readonly DateTimeOffset Time = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly ChannelId Channel = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    private const GenesisRecordingRootProvenance DefaultStartup = GenesisRecordingRootProvenance.Default | GenesisRecordingRootProvenance.StartupRecordingDirectory;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    [InlineData("../recordings")]
    [InlineData("bad\0path")]
    [InlineData("https://example.test/root")]
    public void InvalidInputMatchesSettingsPolicyAndDoesNotChangeRevision(string? path)
    {
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        Assert.False(GenesisRecordingOutputDirectorySettings.TryNormalize(path, out _));
        Assert.Equal(GenesisRecordingRootRegistrationResult.Invalid, roots.RegisterExplicit(path));
        Assert.Empty(roots.GetSnapshot().Roots);
        Assert.Equal(0, roots.GetSnapshot().Revision);
    }

    [Theory]
    [InlineData(@"C:\invalid?directory")]
    [InlineData(@"C:\dir:stream")]
    [InlineData(@"C:\CON")]
    [InlineData(@"C:\trailing.")]
    [InlineData(@"\\?\C:\recordings")]
    [InlineData(@"\\.\C:\recordings")]
    public void WindowsDeviceAndInvalidPathsMatchEstablishedPolicy(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(GenesisRecordingOutputDirectorySettings.TryNormalize(path, out _));
        Assert.Equal(GenesisRecordingRootRegistrationResult.Invalid, new GenesisRecordingLibraryKnownRootRegistry().RegisterExplicit(path));
    }

    [Fact]
    public void AbsoluteMissingRootIsAcceptedWithoutCreatingOrScanningIt()
    {
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        Assert.Equal(GenesisRecordingRootRegistrationResult.Added, roots.RegisterExplicit(CustomRoot));
        var snapshot = roots.GetSnapshot();
        Assert.Equal(new GenesisRecordingLibraryKnownRoot(CustomRoot, GenesisRecordingRootProvenance.Explicit), Assert.Single(snapshot.Roots));
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(64, snapshot.Capacity);
        Assert.False(Directory.Exists(_root));
        Assert.Equal(GenesisRecordingRootRegistrationResult.AlreadyKnown, roots.RegisterExplicit(CustomRoot));
        Assert.Equal(1, roots.GetSnapshot().Revision);
    }

    [Theory]
    [InlineData("separator")]
    [InlineData("multipleSeparators")]
    [InlineData("dot")]
    [InlineData("space")]
    [InlineData("case")]
    [InlineData("filesystemRoot")]
    public void NormalizedIdentityDeduplicatesAndPreservesCanonicalPath(string variation)
    {
        var original = variation == "filesystemRoot" ? Path.GetPathRoot(DefaultRoot)! : DefaultRoot;
        var candidate = variation switch
        {
            "separator" => original + Path.DirectorySeparatorChar,
            "multipleSeparators" => original + new string(Path.DirectorySeparatorChar, 3),
            "dot" => Path.Combine(original, "child", ".."),
            "space" => "  " + original + "  ",
            "case" => original.ToUpperInvariant(),
            _ => original,
        };
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        roots.RegisterExplicit(original);
        var distinct = variation == "case" && !OperatingSystem.IsWindows();
        Assert.Equal(distinct ? GenesisRecordingRootRegistrationResult.Added : GenesisRecordingRootRegistrationResult.AlreadyKnown,
            roots.RegisterExplicit(candidate));
        Assert.Equal(distinct ? 2 : 1, roots.GetSnapshot().Count);
        Assert.Contains(roots.GetSnapshot().Roots, x => x.Path == original);
        Assert.Equal(distinct ? 2 : 1, roots.GetSnapshot().Revision);
    }

    [Fact]
    public void ProvenanceMergesOnlyNewFactsAndSnapshotsCannotMutateRegistry()
    {
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        Assert.Equal(GenesisRecordingRootRegistrationResult.Added, roots.Register(DefaultRoot, GenesisRecordingRootProvenance.Default));
        var first = roots.GetSnapshot();
        Assert.Equal(GenesisRecordingRootRegistrationResult.Updated, roots.Register(DefaultRoot, GenesisRecordingRootProvenance.StartupRecordingDirectory));
        Assert.Equal(DefaultStartup, Assert.Single(roots.GetSnapshot().Roots).Provenance);
        Assert.Equal(GenesisRecordingRootRegistrationResult.Updated, roots.RegisterExplicit(DefaultRoot));
        Assert.Equal(GenesisRecordingRootRegistrationResult.AlreadyKnown, roots.RegisterExplicit(DefaultRoot));
        Assert.Equal(3, roots.GetSnapshot().Revision);
        Assert.Equal(GenesisRecordingRootProvenance.Default, Assert.Single(first.Roots).Provenance);
        Assert.Throws<NotSupportedException>(() => ((IList<GenesisRecordingLibraryKnownRoot>)first.Roots).Clear());
    }

    [Fact]
    public void OfflineUncRootRequiresOnlyLexicalNormalizationAndNoNetworkProbe()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string path = @"\\pr230-no-network-probe.invalid\offline\recordings";
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        Assert.Equal(GenesisRecordingRootRegistrationResult.Added, roots.RegisterExplicit(path));
        Assert.Equal(path, Assert.Single(roots.GetSnapshot().Roots).Path);
        // Deliberately no Exists/stat call even in this test.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(9)]
    public void InvalidInMemoryProvenanceCannotBeRegisteredOrSerialized(int value)
    {
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        Assert.Equal(GenesisRecordingRootRegistrationResult.Invalid, roots.Register(DefaultRoot, (GenesisRecordingRootProvenance)value));
        Assert.Equal(0, roots.GetSnapshot().Revision);
        var snapshot = new GenesisRecordingMetadataSnapshot(new GenesisRecordingHistoryReadModel().GetSnapshot(),
            new(0, [new(DefaultRoot, (GenesisRecordingRootProvenance)value)]));
        Assert.Throws<JsonException>(() => GenesisRecordingHistoryJsonCodec.Serialize(snapshot));
    }

    [Fact]
    public void CapacityRejectsWithoutEvictionHistorySettingsOrRevisionMutation()
    {
        var settings = Settings();
        var history = new GenesisRecordingHistoryReadModel();
        history.ObserveTerminal(Fact());
        var fact = Assert.Single(history.GetSnapshot().Entries);
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        for (var i = 0; i < 64; i++) Assert.Equal(GenesisRecordingRootRegistrationResult.Added,
            roots.Register(Path.Combine(_root, $"root-{i:D2}"), GenesisRecordingRootProvenance.Default));
        var before = roots.GetSnapshot();
        Assert.Equal(GenesisRecordingRootRegistrationResult.CapacityReached, roots.RegisterExplicit(CustomRoot));
        Assert.Equal(before.Roots, roots.GetSnapshot().Roots);
        Assert.Equal(64, roots.GetSnapshot().Revision);
        Assert.Equal(GenesisRecordingRootRegistrationResult.Updated, roots.RegisterExplicit(before.Roots[0].Path));
        Assert.Equal(65, roots.GetSnapshot().Revision);
        Assert.Equal(64, roots.GetSnapshot().Count);
        Assert.Equal(fact, Assert.Single(history.GetSnapshot().Entries));
        Assert.Equal(DefaultRoot, settings.CurrentRuntimeDirectory);
        Assert.Null(SettingsStore.Load().RecordingOutputDirectory);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionSeedsActualDefaultAndRuntimeWithNoRecordingOrRootIo(bool custom)
    {
        var settings = Settings();
        if (custom) { Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(CustomRoot)); settings = Settings(); }
        await using var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, Store);
        var roots = composition.KnownRecordingRoots.GetSnapshot();
        Assert.Equal(custom ? 2 : 1, roots.Count);
        Assert.Equal(custom ? GenesisRecordingRootProvenance.Default : DefaultStartup,
            roots.Roots.Single(x => x.Path == DefaultRoot).Provenance);
        Assert.Equal(custom ? CustomRoot : DefaultRoot, composition.RecordingDirectory);
        Assert.Equal(GenesisRecordingRootProvenance.StartupRecordingDirectory,
            roots.Roots.Single(x => x.Path == composition.RecordingDirectory).Provenance & GenesisRecordingRootProvenance.StartupRecordingDirectory);
        Assert.Empty(composition.RecordingHistory.GetSnapshot().Entries);
        Assert.Empty(await composition.Recording.GetActiveRecordingSessionsAsync());
        Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        Assert.Equal(0, composition.RelayRuntime.RecordingStreamSourceLeaseRegistry.Count);
        Assert.False(Directory.Exists(DefaultRoot));
        Assert.False(Directory.Exists(CustomRoot));
    }

    [Fact]
    public async Task SavedNextStartAndResetNeverAlterRuntimeOrRemoveHistoricalRoots()
    {
        var settingsA = Settings();
        var startA = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settingsA, Store);
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settingsA.Save(CustomRoot));
        Assert.Equal(CustomRoot, settingsA.Presentation.EffectiveDirectory);
        Assert.Equal(DefaultRoot, settingsA.CurrentRuntimeDirectory);
        Assert.DoesNotContain(startA.KnownRecordingRoots.GetSnapshot().Roots, x => x.Path == CustomRoot);
        await startA.DisposeAsync();
        var settingsB = Settings();
        var startB = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settingsB, Store);
        Assert.Equal(CustomRoot, startB.RecordingDirectory);
        Assert.Contains(startB.KnownRecordingRoots.GetSnapshot().Roots, x => x.Path == CustomRoot);
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settingsB.ResetToDefault());
        Assert.Equal(CustomRoot, settingsB.CurrentRuntimeDirectory);
        Assert.Equal(CustomRoot, startB.RecordingDirectory);
        Assert.Equal(DefaultRoot, settingsB.Presentation.EffectiveDirectory);
        Assert.Equal(2, startB.KnownRecordingRoots.GetSnapshot().Count);
        await startB.DisposeAsync();
        await using var startC = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        Assert.Equal(DefaultRoot, startC.RecordingDirectory);
        Assert.Equal(2, startC.KnownRecordingRoots.GetSnapshot().Count);
        Assert.Contains(startC.KnownRecordingRoots.GetSnapshot().Roots, x => x.Path == CustomRoot);
        Assert.Null(SettingsStore.Load().RecordingOutputDirectory);
    }

    [Fact]
    public async Task FutureConfiguredDirectoryIsNotSeededEvenIfSavedBeforeComposition()
    {
        var settings = Settings();
        settings.Save(CustomRoot);
        await using var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, Store);
        Assert.Equal(DefaultRoot, composition.RecordingDirectory);
        Assert.Equal(DefaultStartup, Assert.Single(composition.KnownRecordingRoots.GetSnapshot().Roots).Provenance);
        Assert.Equal(CustomRoot, SettingsStore.Load().RecordingOutputDirectory);
    }

    [Fact]
    public async Task ExplicitRootRoundtripsAlongsideHistoryWithoutChangingOutputSetting()
    {
        var settings = Settings();
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, Store);
        var fact = Fact(automatic: true);
        composition.RecordingHistory.ObserveTerminal(fact);
        Assert.Equal(GenesisRecordingRootRegistrationResult.Added, composition.KnownRecordingRoots.RegisterExplicit(CustomRoot));
        Assert.Equal(GenesisRecordingRootRegistrationResult.Updated, composition.KnownRecordingRoots.RegisterExplicit(DefaultRoot));
        await composition.DisposeAsync();
        var saved = await File.ReadAllBytesAsync(StorePath);
        await using var next = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        Assert.Equal(fact, Assert.Single(next.RecordingHistory.GetSnapshot().Entries));
        Assert.Equal(2, next.KnownRecordingRoots.GetSnapshot().Count);
        Assert.Equal(GenesisRecordingRootProvenance.Explicit, next.KnownRecordingRoots.GetSnapshot().Roots.Single(x => x.Path == CustomRoot).Provenance);
        Assert.Equal(DefaultStartup | GenesisRecordingRootProvenance.Explicit,
            next.KnownRecordingRoots.GetSnapshot().Roots.Single(x => x.Path == DefaultRoot).Provenance);
        Assert.False(next.GetHistoryDurabilitySnapshot()!.Pending);
        Assert.Null(SettingsStore.Load().RecordingOutputDirectory);
        Assert.Equal(saved, await File.ReadAllBytesAsync(StorePath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task V1ReadDoesNotWriteAndSeedUpgradePreservesEveryHistoryFact(bool automatic, bool stopped)
    {
        var fact = Fact(automatic: automatic, stopped: stopped);
        var history = new GenesisRecordingHistoryReadModel(); history.ObserveTerminal(fact); history.RecordPublicationFailure();
        var v1 = Document(history.GetSnapshot()); v1["schemaVersion"] = 1; v1.Remove("knownRoots");
        var original = Bytes(v1); await WriteStore(original);
        var loaded = Store.Load();
        Assert.Equal(fact, Assert.Single(loaded.Snapshot!.Entries));
        Assert.Empty(loaded.KnownRoots!.Roots);
        var readOnly = new GenesisRecordingHistoryPersistenceCoordinator(new(), Store, new());
        await readOnly.ShutdownAsync();
        Assert.Equal(original, await File.ReadAllBytesAsync(StorePath));
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        Assert.Equal(fact, Assert.Single(composition.RecordingHistory.GetSnapshot().Entries));
        await composition.DisposeAsync();
        var v2 = JsonNode.Parse(await File.ReadAllBytesAsync(StorePath))!.AsObject();
        Assert.Equal(2, v2["schemaVersion"]!.GetValue<int>());
        foreach (var property in v1.Where(x => x.Key != "schemaVersion")) Assert.True(JsonNode.DeepEquals(property.Value, v2[property.Key]), property.Key);
        Assert.Equal(DefaultStartup, Assert.Single(Store.Load().KnownRoots!.Roots).Provenance);
        await using var restart = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        Assert.Equal(fact, Assert.Single(restart.RecordingHistory.GetSnapshot().Entries));
        Assert.False(restart.GetHistoryDurabilitySnapshot()!.Pending);
    }

    [Fact]
    public void V1UnknownOptionalRootPropertyRemainsOpaqueAndNoRootsAreInferredFromHistoryPaths()
    {
        var v1 = Document(); v1["schemaVersion"] = 1; v1["knownRoots"] = "unknown optional v1 value";
        var loaded = GenesisRecordingHistoryJsonCodec.DeserializeDocument(Bytes(v1));
        Assert.Single(loaded.History.Entries);
        Assert.Empty(loaded.KnownRoots.Roots);
    }

    [Theory]
    [InlineData(RecordingSinkSessionLifecycleKind.NoDecodableVideoStart, false)]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted, false)]
    [InlineData(RecordingSinkSessionLifecycleKind.Faulted, true)]
    public async Task V1UpgradePreservesNonSuccessfulTerminalAndOptionalProvenWriter(RecordingSinkSessionLifecycleKind kind, bool writerKnown)
    {
        var original = Fact(automatic: true);
        var fact = new RecordingSessionTerminalFact(original.Session, null, null, null, Time, Time, kind,
            writerKnown ? original.WriteResult : null,
            kind == RecordingSinkSessionLifecycleKind.Faulted ? RecordingFaultDescriptor.FromSafeDetails(RecordingFailureStage.SourceRead, "IOException") : null);
        var history = new GenesisRecordingHistoryReadModel(); history.ObserveTerminal(fact);
        var v1 = Document(history.GetSnapshot()); v1["schemaVersion"] = 1; v1.Remove("knownRoots"); await WriteStore(Bytes(v1));
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        await composition.DisposeAsync();
        Assert.Equal(fact, Assert.Single(Store.Load().Snapshot!.Entries));
        Assert.Single(Store.Load().KnownRoots!.Roots);
    }

    [Fact]
    public async Task V1RetentionCountersWatermarkAndReplayBoundarySurviveUpgrade()
    {
        var history = new GenesisRecordingHistoryReadModel(); var evicted = Fact(); history.ObserveTerminal(evicted);
        for (var i = 1; i <= 4096; i++) history.ObserveTerminal(Fact(sequence: i));
        history.RecordPublicationFailure();
        var v1 = Document(history.GetSnapshot()); v1["schemaVersion"] = 1; v1.Remove("knownRoots"); await WriteStore(Bytes(v1));
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        var before = composition.RecordingHistory.GetSnapshot();
        composition.RecordingHistory.ObserveTerminal(evicted);
        Assert.Equal(before.Revision, composition.RecordingHistory.GetSnapshot().Revision);
        await composition.DisposeAsync();
        var restored = Store.Load().Snapshot!;
        Assert.Equal(before.Entries, restored.Entries);
        Assert.Equal(before.EvictedThrough, restored.EvictedThrough);
        Assert.Equal(1, restored.EvictedEntryCount);
        Assert.Equal(1, restored.PublicationFailureCount);
        Assert.True(restored.HasGap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedUpgradePreservesOriginalV1AndRetainsInMemorySeeds(bool replacement)
    {
        var v1 = Document(); v1["schemaVersion"] = 1; v1.Remove("knownRoots");
        var original = Bytes(v1); await WriteStore(original);
        var store = new GenesisRecordingHistoryStore(StorePath,
            replacement ? null : (_, _, _) => throw new IOException("temp failure"),
            replacement ? (_, _) => throw new IOException("replace failure") : null);
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), store);
        await composition.DisposeAsync();
        Assert.Equal(original, await File.ReadAllBytesAsync(StorePath));
        Assert.Single(composition.KnownRecordingRoots.GetSnapshot().Roots);
        Assert.Single(composition.RecordingHistory.GetSnapshot().Entries);
        Assert.True(composition.GetHistoryDurabilitySnapshot()!.Pending);
        Assert.True(composition.GetHistoryDurabilitySnapshot()!.IsDegraded);
    }

    [Theory]
    [InlineData("missingRoots")]
    [InlineData("nullRoots")]
    [InlineData("wrongType")]
    [InlineData("tooMany")]
    [InlineData("nullEntry")]
    [InlineData("duplicate")]
    [InlineData("caseDuplicate")]
    [InlineData("relative")]
    [InlineData("notNormalized")]
    [InlineData("emptyPath")]
    [InlineData("missingPath")]
    [InlineData("missingProvenance")]
    [InlineData("nullProvenance")]
    [InlineData("emptyProvenance")]
    [InlineData("unknownProvenance")]
    [InlineData("numericProvenance")]
    [InlineData("duplicateProvenance")]
    [InlineData("combinedName")]
    [InlineData("nullName")]
    [InlineData("tooManyProvenance")]
    public async Task InvalidV2RootsAreProtectedIncludingProductionSeeds(string mutation)
    {
        var json = Document(); var roots = json["knownRoots"]!.AsArray(); var root = roots[0]!.AsObject();
        switch (mutation)
        {
            case "missingRoots": json.Remove("knownRoots"); break;
            case "nullRoots": json["knownRoots"] = null; break;
            case "wrongType": json["knownRoots"] = "invalid"; break;
            case "tooMany": for (var i = 0; i < 64; i++) roots.Add(new JsonObject { ["path"] = Path.Combine(_root, $"more-{i}"), ["provenance"] = new JsonArray("Explicit") }); break;
            case "nullEntry": roots.Add(null); break;
            case "duplicate": roots.Add(root.DeepClone()); break;
            case "caseDuplicate":
                roots.Add(new JsonObject { ["path"] = DefaultRoot.ToUpperInvariant(), ["provenance"] = new JsonArray("Default") });
                if (!OperatingSystem.IsWindows()) { Assert.Equal(2, GenesisRecordingHistoryJsonCodec.DeserializeDocument(Bytes(json)).KnownRoots.Count); return; }
                break;
            case "relative": root["path"] = "relative"; break;
            case "notNormalized": root["path"] = DefaultRoot + Path.DirectorySeparatorChar; break;
            case "emptyPath": root["path"] = ""; break;
            case "missingPath": root.Remove("path"); break;
            case "missingProvenance": root.Remove("provenance"); break;
            case "nullProvenance": root["provenance"] = null; break;
            case "emptyProvenance": root["provenance"] = new JsonArray(); break;
            case "unknownProvenance": root["provenance"] = new JsonArray("Future"); break;
            case "numericProvenance": root["provenance"] = new JsonArray(1); break;
            case "duplicateProvenance": root["provenance"] = new JsonArray("Explicit", "Explicit"); break;
            case "combinedName": root["provenance"] = new JsonArray("Default, Explicit"); break;
            case "nullName": root["provenance"] = new JsonArray((JsonNode?)null); break;
            case "tooManyProvenance": root["provenance"] = new JsonArray("Default", "StartupRecordingDirectory", "Explicit", "Explicit"); break;
        }
        var bytes = Bytes(json); await WriteStore(bytes);
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        Assert.Equal(GenesisRecordingHistoryLoadStatus.Invalid, composition.GetHistoryDurabilitySnapshot()!.LoadStatus);
        Assert.False(composition.GetHistoryDurabilitySnapshot()!.AutomaticPersistenceEnabled);
        Assert.Single(composition.KnownRecordingRoots.GetSnapshot().Roots); // in-memory seeds remain usable
        await composition.DisposeAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(StorePath));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("invalidV1")]
    [InlineData("version0")]
    [InlineData("version3")]
    [InlineData("version99")]
    [InlineData("oversized")]
    public async Task BadOriginalCannotBeOverwrittenBySeedsOrExplicitRegistration(string kind)
    {
        var json = Document(); json["schemaVersion"] = kind switch { "version0" => 0, "version3" => 3, "version99" => 99, _ => 1 };
        json.Remove("knownRoots");
        if (kind == "invalidV1") json["entries"]![0]!["sessionId"] = Guid.Empty;
        var bytes = kind == "malformed" ? Encoding.UTF8.GetBytes("{broken") : kind == "oversized"
            ? new byte[GenesisRecordingHistoryStore.MaximumByteCount + 1] : Bytes(json);
        await WriteStore(bytes);
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        Assert.Equal(kind.StartsWith("version", StringComparison.Ordinal) ? GenesisRecordingHistoryLoadStatus.UnsupportedVersion : GenesisRecordingHistoryLoadStatus.Invalid,
            composition.GetHistoryDurabilitySnapshot()!.LoadStatus);
        composition.KnownRecordingRoots.RegisterExplicit(CustomRoot);
        composition.RecordingHistory.ObserveTerminal(Fact());
        await composition.DisposeAsync();
        Assert.False(composition.GetHistoryDurabilitySnapshot()!.AutomaticPersistenceEnabled);
        Assert.True(composition.GetHistoryDurabilitySnapshot()!.Pending);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(StorePath));
        Assert.Equal(2, composition.KnownRecordingRoots.GetSnapshot().Count);
        Assert.Single(composition.RecordingHistory.GetSnapshot().Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangesInBothModelsDuringSlowWritePersistLatestCompositeWithBoundedCoalescing(bool rootFirst)
    {
        var entered = Gate(); var release = Gate(); var writes = 0;
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        {
            if (Interlocked.Increment(ref writes) == 1) { entered.SetResult(); await release.Task.WaitAsync(token); }
            await File.WriteAllBytesAsync(path, bytes, token);
        });
        var history = new GenesisRecordingHistoryReadModel(); var roots = new GenesisRecordingLibraryKnownRootRegistry();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store, roots);
        if (rootFirst) roots.RegisterExplicit(DefaultRoot); else history.ObserveTerminal(Fact());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 25; i++) { history.ObserveTerminal(Fact(sequence: i)); roots.RegisterExplicit(Path.Combine(_root, $"registered-{i}")); }
        Assert.True(coordinator.GetSnapshot().Pending);
        release.SetResult(); await coordinator.FlushAsync();
        Assert.Equal(2, writes);
        Assert.Equal(history.GetSnapshot().Entries, store.Load().Snapshot!.Entries);
        Assert.Equal(roots.GetSnapshot().Roots, store.Load().KnownRoots!.Roots);
        Assert.False(coordinator.GetSnapshot().Pending);
        roots.RegisterExplicit(roots.GetSnapshot().Roots[0].Path);
        history.ObserveTerminal(history.GetSnapshot().Entries[0]);
        await coordinator.ShutdownAsync();
        Assert.Equal(2, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryOnlyOrRootOnlyUpdatePreservesOtherRestoredComponent(bool historyUpdate)
    {
        var document = Document(); await WriteStore(Bytes(document));
        var history = new GenesisRecordingHistoryReadModel(); var roots = new GenesisRecordingLibraryKnownRootRegistry();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, Store, roots);
        var originalHistory = history.GetSnapshot().Entries; var originalRoots = roots.GetSnapshot().Roots;
        Assert.False(coordinator.GetSnapshot().Pending);
        if (historyUpdate) history.ObserveTerminal(Fact()); else roots.RegisterExplicit(CustomRoot);
        await coordinator.ShutdownAsync();
        Assert.Equal(historyUpdate ? 2 : 1, Store.Load().Snapshot!.Entries.Count);
        Assert.Equal(historyUpdate ? 1 : 2, Store.Load().KnownRoots!.Count);
        if (historyUpdate) Assert.Equal(originalRoots, Store.Load().KnownRoots!.Roots);
        else Assert.Equal(originalHistory, Store.Load().Snapshot!.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootWriteConflictBeforeOrDuringTempWritePreservesExternalDocument(bool during)
    {
        await WriteStore(Bytes(Document()));
        var entered = Gate(); var release = Gate();
        var store = new GenesisRecordingHistoryStore(StorePath, async (path, bytes, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await File.WriteAllBytesAsync(path, bytes, token);
        });
        var history = new GenesisRecordingHistoryReadModel(); var roots = new GenesisRecordingLibraryKnownRootRegistry();
        var coordinator = new GenesisRecordingHistoryPersistenceCoordinator(history, store, roots);
        var external = Encoding.UTF8.GetBytes("external content must remain");
        if (!during) await File.WriteAllBytesAsync(StorePath, external);
        roots.RegisterExplicit(CustomRoot);
        if (during) { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await File.WriteAllBytesAsync(StorePath, external); release.SetResult(); }
        await coordinator.FlushAsync();
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Conflict, coordinator.GetSnapshot().LastWriteStatus);
        Assert.False(coordinator.GetSnapshot().AutomaticPersistenceEnabled);
        roots.RegisterExplicit(Path.Combine(_root, "later"));
        history.ObserveTerminal(Fact()); await coordinator.ShutdownAsync();
        Assert.Equal(external, await File.ReadAllBytesAsync(StorePath));
        Assert.Equal(3, roots.GetSnapshot().Count);
    }

    [Fact]
    public async Task RootPersistenceFailureDoesNotChangeActualRecorderStopFinalizedMediaOrHistory()
    {
        var failed = Gate();
        var store = new GenesisRecordingHistoryStore(StorePath, (_, _, _) => { failed.TrySetResult(); throw new IOException("metadata only"); });
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), store);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var stream = new BlockingStream();
        using var registration = composition.RelayRuntime.RecordingStreamSourceLeaseRegistry.Register(Channel, new LeaseFactory(stream));
        var result = await composition.Recording.StartManualAsync(new RecordingCandidate(Channel, new(name: "fixture", streamType: "DATA", streamExtension: "bin")));
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordingAutomationStopResultKind.Stopped,
            (await composition.Recording.StopManualAsync(Channel, result.State.Session!.SessionId)).Kind);
        var fact = Assert.Single(composition.RecordingHistory.GetSnapshot().Entries);
        Assert.Equal(result.State.Session, fact.Session);
        Assert.Null(fact.Fault);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, fact.WriteResult!.CompletionKind);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(fact.WriteResult.Path));
        Assert.Equal(GenesisRecordingRootRegistrationResult.Added, composition.KnownRecordingRoots.RegisterExplicit(CustomRoot));
        await composition.DisposeAsync();
        Assert.Equal(2, composition.KnownRecordingRoots.GetSnapshot().Count);
        Assert.True(composition.GetHistoryDurabilitySnapshot()!.IsDegraded);
        Assert.True(composition.GetHistoryDurabilitySnapshot()!.Pending);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(fact.WriteResult.Path));
    }

    [Fact]
    public async Task AllHistoryForRootCanBeEvictedWithoutRemovingDurableRoot()
    {
        var history = new GenesisRecordingHistoryReadModel(); var roots = new GenesisRecordingLibraryKnownRootRegistry();
        roots.RegisterExplicit(DefaultRoot);
        var evicted = Fact(); history.ObserveTerminal(evicted);
        for (var i = 1; i <= 4096; i++)
        {
            var fact = Fact(sequence: i);
            history.ObserveTerminal(new(fact.Session, fact.ChannelName, fact.StreamType, fact.RequestedFileExtension,
                fact.AdmissionObservedAtUtc, fact.TerminalObservedAtUtc, fact.Kind,
                RecordingFileWriteResult.FromFinalizedFacts(Path.Combine(CustomRoot, $"{Guid.NewGuid():N}.bin"), 10, RecordingFileWriteCompletionKind.EndOfStream)));
        }
        Assert.Null(history.Find(evicted.Session.SessionId));
        Assert.DoesNotContain(history.GetSnapshot().Entries, x => Path.GetDirectoryName(x.WriteResult!.Path) == DefaultRoot);
        Assert.Equal(GenesisRecordingHistoryWriteStatus.Saved,
            (await Store.SaveAsync(new(history.GetSnapshot(), roots.GetSnapshot()), null, default)).Status);
        var nextRoots = new GenesisRecordingLibraryKnownRootRegistry(); var nextHistory = new GenesisRecordingHistoryReadModel();
        await using var next = new GenesisRecordingHistoryPersistenceCoordinator(nextHistory, Store, nextRoots);
        Assert.Equal(DefaultRoot, Assert.Single(nextRoots.GetSnapshot().Roots).Path);
        Assert.Equal(1, nextHistory.GetSnapshot().EvictedEntryCount);
        Assert.False(Directory.Exists(DefaultRoot));
    }

    [Fact]
    public async Task FullRestoredCapacityExposesStartupSeedRejectionsWithoutLosingRoots()
    {
        var roots = new GenesisRecordingLibraryKnownRootRegistry();
        for (var i = 0; i < 64; i++) roots.RegisterExplicit(Path.Combine(_root, $"past-{i}"));
        await Store.SaveAsync(new(new GenesisRecordingHistoryReadModel().GetSnapshot(), roots.GetSnapshot()), null, default);
        var original = await File.ReadAllBytesAsync(StorePath);
        var settings = Settings(); settings.Save(CustomRoot); settings = Settings();
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(settings, Store);
        Assert.Equal(GenesisRecordingRootRegistrationResult.CapacityReached, composition.DefaultRootRegistration);
        Assert.Equal(GenesisRecordingRootRegistrationResult.CapacityReached, composition.StartupRootRegistration);
        Assert.Equal(64, composition.KnownRecordingRoots.GetSnapshot().Count);
        Assert.Equal(roots.GetSnapshot().Roots, composition.KnownRecordingRoots.GetSnapshot().Roots);
        Assert.False(composition.GetHistoryDurabilitySnapshot()!.Pending);
        await composition.DisposeAsync();
        Assert.Equal(original, await File.ReadAllBytesAsync(StorePath));
        Assert.Equal(CustomRoot, settings.CurrentRuntimeDirectory);
        Assert.Equal(CustomRoot, SettingsStore.Load().RecordingOutputDirectory);
    }

    [Fact]
    public void SerializationOrderingIsDeterministicAndProvenanceUsesNamedArray()
    {
        var rootsA = new GenesisRecordingLibraryKnownRootRegistry(); var rootsB = new GenesisRecordingLibraryKnownRootRegistry();
        rootsA.Register(CustomRoot, GenesisRecordingRootProvenance.Explicit); rootsA.Register(DefaultRoot, DefaultStartup);
        rootsB.Register(DefaultRoot, DefaultStartup); rootsB.Register(CustomRoot, GenesisRecordingRootProvenance.Explicit);
        var history = new GenesisRecordingHistoryReadModel().GetSnapshot();
        var bytes = GenesisRecordingHistoryJsonCodec.Serialize(new GenesisRecordingMetadataSnapshot(history, rootsA.GetSnapshot()));
        Assert.Equal(bytes, GenesisRecordingHistoryJsonCodec.Serialize(new GenesisRecordingMetadataSnapshot(history, rootsB.GetSnapshot())));
        var array = JsonNode.Parse(bytes)!["knownRoots"]!.AsArray();
        Assert.Contains(array, x => x!["provenance"]!.ToJsonString() == "[\"Default\",\"StartupRecordingDirectory\"]");
        Assert.Equal(rootsA.GetSnapshot().Roots, GenesisRecordingHistoryJsonCodec.DeserializeDocument(bytes).KnownRoots.Roots);
    }

    [Fact]
    public async Task ApplicationRootCommandAndQueryHaveNoPlaybackRecordingOrRulesSideEffect()
    {
        var composition = NovaDailyHttpFlvPlaybackController.CreateProductionRecordingComposition(Settings(), Store);
        var shared = new GenesisSharedPcpPlaybackRuntime(composition);
        var callbacks = 0;
        await using var daily = new NovaDailyHttpFlvPlaybackController(new PcrPlayerExternalPlayerLauncher((string?)null),
            _ => callbacks++, _ => callbacks++, composition, shared, new EmptyRules());
        Assert.Equal(GenesisRecordingRootRegistrationResult.Added, daily.RegisterRecordingLibraryRoot(CustomRoot));
        Assert.Equal(GenesisRecordingRootRegistrationResult.AlreadyKnown, daily.RegisterRecordingLibraryRoot(CustomRoot));
        Assert.Equal(2, daily.GetKnownRecordingRootsSnapshot().Count);
        Assert.Empty(daily.GetRecordingHistorySnapshot().Entries);
        Assert.Equal(0, callbacks);
        Assert.Empty(await composition.Recording.GetActiveRecordingSessionsAsync());
        Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        Assert.False(Directory.Exists(CustomRoot));
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    private sealed class EmptyRules : IGenesisRecordingRuleSetProvider
    {
        public ValueTask<RecordingRuleSet> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RecordingRuleSet([]));
    }

    private sealed class LeaseFactory(Stream stream) : IRecordingStreamSourceLeaseFactory
    {
        public ValueTask<IRecordingStreamSourceLease?> AcquireAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IRecordingStreamSourceLease?>(new Lease(stream));
    }
    private sealed class Lease(Stream stream) : IRecordingStreamSourceLease
    {
        public Stream ReadStream => stream;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
    private sealed class BlockingStream() : MemoryStream([1, 2, 3], writable: false)
    {
        public TaskCompletionSource Waiting { get; } = Gate();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            Waiting.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0;
        }
    }

    private RecordingSessionTerminalFact Fact(bool automatic = false, bool stopped = false, int sequence = 0) => new(
        automatic ? new RecordingSinkSession(new(Guid.NewGuid()), Channel, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")) : RecordingSinkSession.Manual(new(Guid.NewGuid()), Channel),
        "Original", "DATA", "bin", Time.AddSeconds(sequence), Time.AddSeconds(sequence + 1), RecordingSinkSessionLifecycleKind.Completed,
        RecordingFileWriteResult.FromFinalizedFacts(Path.Combine(DefaultRoot, $"{Guid.NewGuid():N}.bin"), 1234567,
            stopped ? RecordingFileWriteCompletionKind.Stopped : RecordingFileWriteCompletionKind.EndOfStream));

    private JsonObject Document(GenesisRecordingHistorySnapshot? snapshot = null)
    {
        if (snapshot is null) { var history = new GenesisRecordingHistoryReadModel(); history.ObserveTerminal(Fact()); snapshot = history.GetSnapshot(); }
        var roots = new GenesisRecordingLibraryKnownRootRegistry(); roots.RegisterExplicit(DefaultRoot);
        return JsonNode.Parse(GenesisRecordingHistoryJsonCodec.Serialize(new GenesisRecordingMetadataSnapshot(snapshot, roots.GetSnapshot())))!.AsObject();
    }
    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
    private async Task WriteStore(byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!); await File.WriteAllBytesAsync(StorePath, bytes); }
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
