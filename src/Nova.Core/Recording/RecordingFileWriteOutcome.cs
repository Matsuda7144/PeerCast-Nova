namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// A stream may complete without a file when its declared start boundary was
/// never established.
/// </summary>
internal sealed record RecordingFileWriteOutcome
{
    private RecordingFileWriteOutcome(
        RecordingFileWriteResult? writeResult,
        bool noDecodableVideoStart)
    {
        if (writeResult is null && !noDecodableVideoStart)
        {
            throw new ArgumentException(
                "A file-write outcome must contain a finalized result or a no-start result.",
                nameof(writeResult));
        }

        if (writeResult is not null && noDecodableVideoStart)
        {
            throw new ArgumentException(
                "A no-start outcome cannot also contain a finalized file result.",
                nameof(noDecodableVideoStart));
        }

        WriteResult = writeResult;
        IsNoDecodableVideoStart = noDecodableVideoStart;
    }

    public RecordingFileWriteResult? WriteResult { get; }

    public bool IsNoDecodableVideoStart { get; }

    public static RecordingFileWriteOutcome Finalized(
        RecordingFileWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new RecordingFileWriteOutcome(result, noDecodableVideoStart: false);
    }

    public static RecordingFileWriteOutcome NoDecodableVideoStart { get; } =
        new(writeResult: null, noDecodableVideoStart: true);
}

internal interface IRecordingFileWriteOutcomeSource
{
    bool NoDecodableVideoStart { get; }

    bool HasPendingDecodableStartOutput { get; }
}
