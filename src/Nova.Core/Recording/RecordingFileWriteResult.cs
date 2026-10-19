namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Immutable result of one successfully finalized recording file.
/// </summary>
public sealed record RecordingFileWriteResult
{
    /// <summary>
    /// Reconstitutes previously observed finalization values. This does not
    /// inspect a file or prove its current existence or media validity.
    /// </summary>
    public static RecordingFileWriteResult FromFinalizedFacts(
        string path, long bytesWritten, RecordingFileWriteCompletionKind completionKind) =>
        new(path, bytesWritten, completionKind);

    internal RecordingFileWriteResult(
        string path,
        long bytesWritten,
        RecordingFileWriteCompletionKind completionKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesWritten);

        if (!Enum.IsDefined(completionKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completionKind));
        }

        Path = path;
        BytesWritten = bytesWritten;
        CompletionKind = completionKind;
    }

    public string Path { get; }

    public long BytesWritten { get; }

    public RecordingFileWriteCompletionKind CompletionKind { get; }
}
