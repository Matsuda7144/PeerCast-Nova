namespace PeerCast.Nova.Core.Recording;

/// <summary>Fixed classifications only; these diagnostics are independent of History.</summary>
public enum RecordingTransformPhase { Unknown, DecodableStart, TimestampNormalization }

public enum RecordingTransformFailureReason
{
    Unknown,
    InvalidFlvHeader,
    InvalidFlvVersion,
    InvalidTagFraming,
    TruncatedInput,
    InvalidCodecConfiguration,
    InvalidAudioPayload,
    InvalidVideoPayload,
    InvalidAvcPayload,
    InvalidAacPayload,
    TimestampRegression,
    UnsupportedTransformInput,
    BoundsViolation,
}

/// <summary>Stable logical sites, never source line numbers or parsed exception messages.</summary>
public enum RecordingTransformThrowSite
{
    Unknown = 0,
    DecodableStartSignature = 1,
    DecodableStartVersionOrFlags = 2,
    DecodableStartHeaderOffset = 3,
    DecodableStartInitialPreviousTagSize = 4,
    DecodableStartTagDataSize = 5,
    DecodableStartTagStreamId = 6,
    DecodableStartPreviousTagSize = 7,
    DecodableStartReadExactlyTruncated = 8,
    DecodableStartWaitingTagType = 9,
    DecodableStartAvcConfiguration = 10,
    DecodableStartAvcConfigurationBounds = 11,
    DecodableStartGateAvcPacketType = 12,
    DecodableStartReplacementBootstrapBounds = 13,
    DecodableStartAdditionalBootstrapBounds = 14,
    DecodableStartMediaTagType = 15,
    DecodableStartAudioHeader = 16,
    DecodableStartAacPacketHeader = 17,
    DecodableStartAacPacketType = 18,
    DecodableStartVideoHeader = 19,
    DecodableStartAvcPacketHeader = 20,
    DecodableStartAvcPacketType = 21,
    DecodableStartEmptyNalPayload = 22,
    DecodableStartNalLengthPrefix = 23,
    DecodableStartNalLength = 24,
    DecodableStartNalType = 25,
    DecodableStartNalCount = 26,
    TimestampNormalizationSignature = 27,
    TimestampNormalizationVersionOrFlags = 28,
    TimestampNormalizationHeaderOffset = 29,
    TimestampNormalizationInitialPreviousTagSize = 30,
    TimestampNormalizationTagDataSize = 31,
    TimestampNormalizationTagStreamId = 32,
    TimestampNormalizationPreviousTagSize = 33,
    TimestampNormalizationMediaTagType = 34,
    TimestampNormalizationMediaTimestamp = 35,
    TimestampNormalizationReadExactlyTruncated = 36,
    TimestampNormalizationAudioHeader = 37,
    TimestampNormalizationAacPacketHeader = 38,
    TimestampNormalizationAacPacketType = 39,
    TimestampNormalizationVideoHeader = 40,
    TimestampNormalizationAvcPacketHeader = 41,
    TimestampNormalizationAvcPacketType = 42,
}

internal readonly record struct RecordingTransformFailure(
    RecordingTransformPhase Phase,
    RecordingTransformThrowSite Site,
    RecordingTransformFailureReason Reason)
{
    internal static RecordingTransformFailure FromSite(RecordingTransformThrowSite site) => site switch
    {
        RecordingTransformThrowSite.DecodableStartSignature => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidFlvHeader),
        RecordingTransformThrowSite.DecodableStartVersionOrFlags => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidFlvVersion),
        RecordingTransformThrowSite.DecodableStartHeaderOffset => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.DecodableStartInitialPreviousTagSize => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidTagFraming),
        RecordingTransformThrowSite.DecodableStartTagDataSize => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.DecodableStartTagStreamId => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidTagFraming),
        RecordingTransformThrowSite.DecodableStartPreviousTagSize => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidTagFraming),
        RecordingTransformThrowSite.DecodableStartReadExactlyTruncated => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.TruncatedInput),
        RecordingTransformThrowSite.DecodableStartWaitingTagType => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.DecodableStartAvcConfiguration => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidCodecConfiguration),
        RecordingTransformThrowSite.DecodableStartAvcConfigurationBounds => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.DecodableStartGateAvcPacketType => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.DecodableStartReplacementBootstrapBounds => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.DecodableStartAdditionalBootstrapBounds => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.DecodableStartMediaTagType => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.DecodableStartAudioHeader => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidAudioPayload),
        RecordingTransformThrowSite.DecodableStartAacPacketHeader => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidAacPayload),
        RecordingTransformThrowSite.DecodableStartAacPacketType => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.DecodableStartVideoHeader => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidVideoPayload),
        RecordingTransformThrowSite.DecodableStartAvcPacketHeader => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidAvcPayload),
        RecordingTransformThrowSite.DecodableStartAvcPacketType => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.DecodableStartEmptyNalPayload => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidAvcPayload),
        RecordingTransformThrowSite.DecodableStartNalLengthPrefix => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidAvcPayload),
        RecordingTransformThrowSite.DecodableStartNalLength => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.InvalidAvcPayload),
        RecordingTransformThrowSite.DecodableStartNalType => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.DecodableStartNalCount => new(RecordingTransformPhase.DecodableStart, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.TimestampNormalizationSignature => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidFlvHeader),
        RecordingTransformThrowSite.TimestampNormalizationVersionOrFlags => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidFlvVersion),
        RecordingTransformThrowSite.TimestampNormalizationHeaderOffset => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.TimestampNormalizationInitialPreviousTagSize => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidTagFraming),
        RecordingTransformThrowSite.TimestampNormalizationTagDataSize => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.BoundsViolation),
        RecordingTransformThrowSite.TimestampNormalizationTagStreamId => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidTagFraming),
        RecordingTransformThrowSite.TimestampNormalizationPreviousTagSize => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidTagFraming),
        RecordingTransformThrowSite.TimestampNormalizationMediaTagType => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.TimestampNormalizationMediaTimestamp => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.TimestampRegression),
        RecordingTransformThrowSite.TimestampNormalizationReadExactlyTruncated => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.TruncatedInput),
        RecordingTransformThrowSite.TimestampNormalizationAudioHeader => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidAudioPayload),
        RecordingTransformThrowSite.TimestampNormalizationAacPacketHeader => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidAacPayload),
        RecordingTransformThrowSite.TimestampNormalizationAacPacketType => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        RecordingTransformThrowSite.TimestampNormalizationVideoHeader => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidVideoPayload),
        RecordingTransformThrowSite.TimestampNormalizationAvcPacketHeader => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.InvalidAvcPayload),
        RecordingTransformThrowSite.TimestampNormalizationAvcPacketType => new(RecordingTransformPhase.TimestampNormalization, site, RecordingTransformFailureReason.UnsupportedTransformInput),
        _ => default,
    };
}

public enum RecordingDiagnosticEventKind { WriterAdmitted, TransformFailed, Terminal }
public enum RecordingDiagnosticExceptionType { Unknown, RecordingStreamTransformException }

/// <summary>No free text, channel metadata, URI, file path, payload, or exception object.</summary>
public readonly record struct RecordingDiagnosticEvent(
    Guid ActualSessionId,
    RecordingDiagnosticEventKind EventKind,
    RecordingTransformPhase TransformPhase = RecordingTransformPhase.Unknown,
    RecordingTransformThrowSite ThrowSiteId = RecordingTransformThrowSite.Unknown,
    RecordingTransformFailureReason SafeReason = RecordingTransformFailureReason.Unknown,
    RecordingFailureStage FaultStage = RecordingFailureStage.Unknown,
    RecordingDiagnosticExceptionType SafeExceptionType = RecordingDiagnosticExceptionType.Unknown,
    RecordingSinkSessionLifecycleKind? TerminalKind = null)
{
    public const int DiagnosticSchemaVersion = 1;
}

/// <summary>
/// Optional nonblocking observation. Implementations must bound retention and
/// use TryWrite, never perform I/O or wait on the recorder's callback thread.
/// The recorder ignores rejection and observer exceptions.
/// </summary>
public interface IRecordingDiagnosticObserver
{
    bool TryObserve(in RecordingDiagnosticEvent diagnostic);
}
