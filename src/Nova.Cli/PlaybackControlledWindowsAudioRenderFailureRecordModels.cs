namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsAudioRenderFailureRecordSource
{
    None,
    ChildCapture,
    Helper,
}

public enum PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult
{
    None,
    FailureRecordValidated,
    InvalidConfiguration,
    InvalidRecord,
    SourceMismatch,
}

public enum PlaybackControlledWindowsAudioRenderHelperFailurePhase
{
    None,
    SolutionRestore,
    RunnerBuild,
    RunnerAssemblyValidation,
    VideoRecordGate,
    RenderEvidenceCapture,
    CaptureIntegration,
    EvidenceEnvelopeValidation,
    TerminalEnvelopeValidation,
    PostRunRepositoryCheck,
    Completed,
}

public enum PlaybackControlledWindowsAudioRenderHelperFailureCategory
{
    None,
    ValidationFailure,
    CommandUnavailable,
    AccessDenied,
    IoFailure,
    InvalidArgument,
    InvalidOperation,
    PowerShellRuntime,
    UnexpectedException,
}

public sealed record PlaybackControlledWindowsAudioRenderChildFailureRecord(
    int SchemaVersion,
    string Milestone,
    bool ChildExitCodeAvailable,
    int ChildExitCode,
    long StdoutByteCount,
    long StderrByteCount,
    string LastNativeAudioStage,
    bool ChildProcessResultCaptured)
{
    public string ControlledRunResult { get; init; } = "None";
    public string TerminalRecordRejection { get; init; } = "None";
    public string ManagedFailurePhase { get; init; } = "None";
}

public sealed record PlaybackControlledWindowsAudioRenderHelperFailureRecord(
    int SchemaVersion,
    string Milestone,
    PlaybackControlledWindowsAudioRenderHelperFailurePhase FailurePhase,
    PlaybackControlledWindowsAudioRenderHelperFailureCategory FailureCategory);

public readonly record struct
    PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommandResult(
        int ExitCode,
        PlaybackControlledWindowsAudioRenderFailureRecordPreflightResult Result,
        PlaybackControlledWindowsAudioRenderFailureRecordSource DetectedSource,
        bool Validated);
