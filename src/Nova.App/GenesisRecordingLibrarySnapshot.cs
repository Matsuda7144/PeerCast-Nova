using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingLibraryObservationState { NotObservedYet, Observed }
internal enum GenesisRecordingLibraryAvailability { Available, Missing, Unavailable }
internal enum GenesisRecordingLibraryFailure { None, AccessDenied, Io, InvalidPath, NotRegularFile, ReparsePoint }
internal enum GenesisRecordingLibraryContainer { FlvCandidate, UnknownContainerCandidate, UnsupportedExtension }
internal enum GenesisRecordingLibraryLink { LegacyOrUnlinked, HistoryLinked }
internal enum GenesisRecordingLibraryRefreshStatus { Published, Unchanged, Cancelled }

internal sealed record GenesisRecordingLibraryFileMetadata(
    FileAttributes Attributes, long Length, DateTimeOffset LastWriteTimeUtc, DateTimeOffset? CreatedTimeUtc);

/// <summary>Unverified filename claims; never session, origin, rule, or terminal facts.</summary>
internal sealed record GenesisRecordingLibraryFilenameClaims(
    DateTimeOffset AllocationTimestampClaimUtc, string SanitizedChannelNameClaim,
    Guid ChannelIdClaim, Guid SessionIdClaim);

internal sealed record GenesisRecordingLibraryEntry(
    string Path, GenesisRecordingLibraryAvailability Availability, GenesisRecordingLibraryFailure Failure,
    GenesisRecordingLibraryContainer Container, GenesisRecordingLibraryFileMetadata? FileMetadata,
    string? SourceRoot, IReadOnlyList<RecordingSessionTerminalFact> HistoryFacts,
    GenesisRecordingLibraryFilenameClaims? FilenameClaims)
{
    public string Filename => System.IO.Path.GetFileName(Path);
    public GenesisRecordingLibraryLink Link => HistoryFacts.Count == 0
        ? GenesisRecordingLibraryLink.LegacyOrUnlinked : GenesisRecordingLibraryLink.HistoryLinked;
    public long? CurrentFileLength => FileMetadata?.Length;
    // Multiple exact-path facts are retained explicitly rather than selecting
    // a winner. Their historical counters never replace current metadata.
    public bool LengthMismatch => FileMetadata is { } metadata &&
        HistoryFacts.Any(x => x.WriteResult!.BytesWritten != metadata.Length);
}

internal sealed record GenesisRecordingLibraryRootObservation(
    string Path, GenesisRecordingRootProvenance Provenance,
    GenesisRecordingLibraryAvailability Status, GenesisRecordingLibraryFailure Failure,
    long CandidateCount, int MetadataObservationCount, int IncludedEntryCount,
    bool IsTruncated, int UnavailableFileCount)
{
    public bool CoverageComplete => Status == GenesisRecordingLibraryAvailability.Available &&
        !IsTruncated && UnavailableFileCount == 0;
}

/// <summary>Current metadata observations only, in normalized path order. Never persisted.</summary>
internal sealed record GenesisRecordingLibrarySnapshot(
    long Revision, GenesisRecordingLibraryObservationState State,
    IReadOnlyList<GenesisRecordingLibraryEntry> Entries,
    IReadOnlyList<GenesisRecordingLibraryRootObservation> Roots,
    long ObservedLocationCount, bool IsSnapshotTruncated, int ExcludedHistoryPathCount,
    bool HistoryHasGap)
{
    public bool HasUnavailableRoots => Roots.Any(x => x.Status == GenesisRecordingLibraryAvailability.Unavailable);
    public bool HasTruncatedRoots => Roots.Any(x => x.IsTruncated);
    public bool AllRegisteredRootsFullyObserved => State == GenesisRecordingLibraryObservationState.Observed &&
        Roots.All(x => x.CoverageComplete);
    public bool CoverageComplete => AllRegisteredRootsFullyObserved && !IsSnapshotTruncated &&
        ExcludedHistoryPathCount == 0 && !HistoryHasGap &&
        Entries.All(x => x.Availability != GenesisRecordingLibraryAvailability.Unavailable);
}

internal sealed record GenesisRecordingLibraryRefreshResult(
    GenesisRecordingLibraryRefreshStatus Status, GenesisRecordingLibrarySnapshot Snapshot);
