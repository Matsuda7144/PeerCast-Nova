namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Safe, bounded fault details for a recording session. Exception messages,
/// file paths, stream bytes, and network details are deliberately omitted.
/// </summary>
public sealed record RecordingFaultDescriptor
{
    internal RecordingFaultDescriptor(
        RecordingFailureStage stage,
        string exceptionType)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionType);

        Stage = stage;
        ExceptionType = exceptionType.Length <= 128
            ? exceptionType
            : exceptionType[..128];
    }

    public RecordingFailureStage Stage { get; }

    public string ExceptionType { get; }

    internal static RecordingFaultDescriptor FromException(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new RecordingFaultDescriptor(
            RecordingFailureStage.Unknown,
            exception.GetType().Name);
    }

    internal static RecordingFaultDescriptor FromException(
        RecordingFailureStage stage,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new RecordingFaultDescriptor(
            stage,
            exception.GetType().Name);
    }
}
