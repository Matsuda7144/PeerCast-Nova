using System.Text.Json.Serialization;

namespace PeerCast.Nova.Network.Rtmp;

/// <summary>Stable rejection stages. Values are additive and must not be renumbered.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObsRtmpRejectionStage>))]
public enum ObsRtmpRejectionStage
{
    RtmpHandshake = 1,
    RtmpChunkHeader = 2,
    RtmpChunkAssembly = 3,
    RtmpControlMessage = 4,
    AmfDecode = 5,
    AmfCommand = 6,
    ConnectCommand = 7,
    CreateStreamCommand = 8,
    PublishCommand = 9,
    SessionPolicy = 10,
    Metadata = 11,
    AvcSequenceHeader = 12,
    AacSequenceHeader = 13,
    FlvBootstrap = 14,
    MediaPacket = 15,
    AmfResponse = 16,
}

/// <summary>Finite identifiers; exception text is never parsed to select a reason.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObsRtmpRejectionReason>))]
public enum ObsRtmpRejectionReason
{
    UnsupportedHandshakeVersion = 1,
    InvalidHandshakeEcho = 2,
    MissingChunkStreamContext = 3,
    ChunkStreamLimit = 4,
    MessageInterrupted = 5,
    ExtendedTimestampMismatch = 6,
    InvalidMessageLength = 7,
    MessageSizeLimit = 8,
    BufferedBytesLimit = 9,
    UnsupportedChunkSize = 10,
    InvalidMessageStreamId = 11,
    ZeroAcknowledgementWindow = 12,
    UnsupportedUserControlEvent = 13,
    MalformedControlMessage = 14,
    AmfPayloadLimit = 15,
    AmfValueOrDepthLimit = 16,
    NonFiniteAmfNumber = 17,
    InvalidAmfBoolean = 18,
    DuplicateAmfProperty = 19,
    AmfArrayLimit = 20,
    UnsupportedAmfType = 21,
    AmfStringLimit = 22,
    InvalidAmfUtf8 = 23,
    TruncatedAmfValue = 24,
    UnsupportedAmfResponseValue = 25,
    MalformedCommandEnvelope = 26,
    InvalidConnect = 27,
    CommandOutOfSequence = 28,
    DuplicatePublish = 29,
    InvalidCreateStream = 30,
    InvalidPublish = 31,
    InvalidBandwidthCheck = 32,
    PublisherNotActive = 33,
    InvalidDeleteStream = 34,
    InvalidCloseStream = 35,
    UnsupportedCommand = 36,
    InvalidPublishRoute = 37,
    MediaOutsidePublishedStream = 38,
    UnsupportedMessageType = 39,
    MalformedMetadataEnvelope = 40,
    UnsupportedAudioPacket = 41,
    MissingAudioConfiguration = 42,
    UnsupportedVideoPacket = 43,
    MalformedAvcSequenceHeader = 44,
    MissingVideoConfiguration = 45,
    MalformedAvcEndMarker = 46,
    CodecReplacement = 47,
    BootstrapSizeLimit = 48,
    PositionLimit = 49,
    UnsupportedAudioSpecificConfig = 50,
    InvalidAvcConfiguration = 51,
    MissingAvcParameterSets = 52,
    UnsupportedAvcConfigurationExtension = 53,
    TrailingAvcConfigurationBytes = 54,
    TruncatedAvcParameterSet = 55,
    InvalidAvcParameterSet = 56,
    InvalidNaluLength = 57,
    InvalidNaluBody = 58,
    EmptyNaluMessage = 59,
    UnclassifiedInvalidData = 60,
}

/// <summary>Only allowlisted names; unknown wire strings are never retained.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObsRtmpDiagnosticToken>))]
public enum ObsRtmpDiagnosticToken
{
    Unknown = 0,
    Connect = 1,
    ReleaseStream = 2,
    FCPublish = 3,
    CreateStream = 4,
    Publish = 5,
    CheckBandwidth = 6,
    FCUnpublish = 7,
    DeleteStream = 8,
    CloseStream = 9,
    SetDataFrame = 10,
    OnMetaData = 11,
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<ObsFlvBootstrapState>))]
public enum ObsFlvBootstrapState
{
    None = 0,
    Metadata = 1,
    AudioConfiguration = 2,
    VideoConfiguration = 4,
    HeadPublished = 8,
    MediaStarted = 16,
    VideoEnded = 32,
}

/// <summary>Fixed-size scalar context; no payload, credentials, route or metadata object.</summary>
public sealed record ObsRtmpRejectionContext
{
    public byte? MessageType { get; init; }
    public int? ChunkStreamId { get; init; }
    public uint? MessageStreamId { get; init; }
    public uint? Timestamp { get; init; }
    public byte? HeaderFormat { get; init; }
    public int? DeclaredPayloadLength { get; init; }
    public int? AssembledPayloadLength { get; init; }
    public ObsRtmpDiagnosticToken? CommandOrEvent { get; init; }
    public double? TransactionId { get; init; }
    public byte? PacketType { get; init; }
    public int? ConfigLength { get; init; }
    public ObsFlvBootstrapState? BootstrapState { get; init; }
}

/// <summary>One immutable terminal rejection, independent of raw media ownership.</summary>
public sealed record ObsRtmpRejectionDiagnostic(
    ObsRtmpRejectionStage Stage,
    ObsRtmpRejectionReason Reason,
    ObsRtmpRejectionContext Context);

/// <summary>
/// Keeps the existing InvalidDataException type and terminal behavior. The first
/// tagged site wins; outer boundaries only fill missing safe context.
/// </summary>
internal static class ObsRtmpRejection
{
    private static readonly object DiagnosticKey = new();

    public static InvalidDataException Create(
        ObsRtmpRejectionStage stage,
        ObsRtmpRejectionReason reason,
        string message,
        ObsRtmpRejectionContext? context = null)
    {
        var exception = new InvalidDataException(message);
        exception.Data[DiagnosticKey] = new ObsRtmpRejectionDiagnostic(stage, reason, context ?? new());
        return exception;
    }

    public static ObsRtmpRejectionDiagnostic Get(InvalidDataException exception) =>
        exception.Data[DiagnosticKey] as ObsRtmpRejectionDiagnostic ??
        new(ObsRtmpRejectionStage.SessionPolicy, ObsRtmpRejectionReason.UnclassifiedInvalidData, new());

    public static void Enrich(InvalidDataException exception, ObsRtmpRejectionContext context)
    {
        var diagnostic = Get(exception);
        var first = diagnostic.Context;
        exception.Data[DiagnosticKey] = diagnostic with
        {
            Context = first with
            {
                MessageType = first.MessageType ?? context.MessageType,
                ChunkStreamId = first.ChunkStreamId ?? context.ChunkStreamId,
                MessageStreamId = first.MessageStreamId ?? context.MessageStreamId,
                Timestamp = first.Timestamp ?? context.Timestamp,
                HeaderFormat = first.HeaderFormat ?? context.HeaderFormat,
                DeclaredPayloadLength = first.DeclaredPayloadLength ?? context.DeclaredPayloadLength,
                AssembledPayloadLength = first.AssembledPayloadLength ?? context.AssembledPayloadLength,
                CommandOrEvent = first.CommandOrEvent ?? context.CommandOrEvent,
                TransactionId = first.TransactionId ?? context.TransactionId,
                PacketType = first.PacketType ?? context.PacketType,
                ConfigLength = first.ConfigLength ?? context.ConfigLength,
                BootstrapState = first.BootstrapState ?? context.BootstrapState,
            },
        };
    }

    public static ObsRtmpRejectionContext ForMessage(RtmpMessage message) => new()
    {
        MessageType = message.Type,
        ChunkStreamId = message.ChunkStreamId,
        MessageStreamId = message.StreamId,
        Timestamp = message.Timestamp,
        DeclaredPayloadLength = message.Payload.Length,
        AssembledPayloadLength = message.Payload.Length,
    };

    public static ObsRtmpDiagnosticToken Token(string name) => name switch
    {
        "connect" => ObsRtmpDiagnosticToken.Connect,
        "releaseStream" => ObsRtmpDiagnosticToken.ReleaseStream,
        "FCPublish" => ObsRtmpDiagnosticToken.FCPublish,
        "createStream" => ObsRtmpDiagnosticToken.CreateStream,
        "publish" => ObsRtmpDiagnosticToken.Publish,
        "_checkbw" => ObsRtmpDiagnosticToken.CheckBandwidth,
        "FCUnpublish" => ObsRtmpDiagnosticToken.FCUnpublish,
        "deleteStream" => ObsRtmpDiagnosticToken.DeleteStream,
        "closeStream" => ObsRtmpDiagnosticToken.CloseStream,
        "@setDataFrame" => ObsRtmpDiagnosticToken.SetDataFrame,
        "onMetaData" => ObsRtmpDiagnosticToken.OnMetaData,
        _ => ObsRtmpDiagnosticToken.Unknown,
    };
}
