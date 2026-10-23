# Structured OBS RTMP rejection diagnostics

## Authority and scope

Read-only audit predecessor: `6b873f573380231a4d010f874cd8272b59790c00`.
Branch: `feature/obs-flv-ingest-foundation`; main/merge-base:
`1193569f79274a2797e544f8796c85183227eda2`; PR #259 remains Draft.

This slice instruments existing rejection conditions. It does not extend the
accepted protocol, change bootstrap order, or relax AAC/AVC/AMF/chunk validation.
The previous physical one-shot is consumed; no real OBS or player retry belongs
to this slice.

## Read-only rejection-site audit and identifiers

All source names below are under `src/Nova.Network/Rtmp/`.

| Stage | Reason identifiers | Production source |
| --- | --- | --- |
| RtmpHandshake | UnsupportedHandshakeVersion, InvalidHandshakeEcho | ObsRtmpProtocolSession.HandshakeAsync |
| RtmpChunkHeader | MissingChunkStreamContext, ChunkStreamLimit, ExtendedTimestampMismatch | RtmpChunkReader.ReadAsync |
| RtmpChunkAssembly | MessageInterrupted, InvalidMessageLength, MessageSizeLimit, BufferedBytesLimit | RtmpChunkReader.ReadAsync |
| RtmpControlMessage | UnsupportedChunkSize, InvalidMessageStreamId, ZeroAcknowledgementWindow, UnsupportedUserControlEvent, MalformedControlMessage | RtmpChunkReader.SetChunkBytes; ObsRtmpProtocolSession.ControlAsync |
| AmfDecode | AmfPayloadLimit, AmfValueOrDepthLimit, NonFiniteAmfNumber, InvalidAmfBoolean, DuplicateAmfProperty, AmfArrayLimit, UnsupportedAmfType, AmfStringLimit, InvalidAmfUtf8, TruncatedAmfValue | RtmpAmf0.Decode / Reader |
| AmfCommand | MalformedCommandEnvelope, InvalidBandwidthCheck, UnsupportedCommand | ObsRtmpProtocolSession.CommandAsync |
| ConnectCommand | InvalidConnect | ObsRtmpProtocolSession.CommandAsync |
| CreateStreamCommand | InvalidCreateStream | ObsRtmpProtocolSession.CommandAsync |
| PublishCommand | InvalidPublish, InvalidPublishRoute | ObsRtmpProtocolSession.CommandAsync / RequireStreamName |
| SessionPolicy | CommandOutOfSequence, DuplicatePublish, PublisherNotActive, InvalidDeleteStream, InvalidCloseStream, MediaOutsidePublishedStream, UnsupportedMessageType, UnclassifiedInvalidData | ObsRtmpProtocolSession; ObsRtmpIngestHost fallback |
| Metadata | MalformedMetadataEnvelope (AMF faults retain AmfDecode stage) | ObsFlvPublishingSession.AcceptAsync |
| AvcSequenceHeader | UnsupportedVideoPacket (sequence envelope), MalformedAvcSequenceHeader, InvalidAvcConfiguration, MissingAvcParameterSets, UnsupportedAvcConfigurationExtension, TrailingAvcConfigurationBytes, TruncatedAvcParameterSet, InvalidAvcParameterSet | ObsFlvPublishingSession.ValidateAvc / ReadParameterSets |
| AacSequenceHeader | UnsupportedAudioPacket (sequence envelope), UnsupportedAudioSpecificConfig | ObsFlvPublishingSession.ValidateAac |
| FlvBootstrap | CodecReplacement, BootstrapSizeLimit, PositionLimit | ObsFlvPublishingSession.RetainConfiguration / Retain / PublishAsync |
| MediaPacket | UnsupportedAudioPacket, MissingAudioConfiguration, UnsupportedVideoPacket, MissingVideoConfiguration, MalformedAvcEndMarker, InvalidNaluLength, InvalidNaluBody, EmptyNaluMessage | ObsFlvPublishingSession.AcceptAsync / ValidateNalus |
| AmfResponse | UnsupportedAmfResponseValue (server encoder invariant, not external acceptance) | RtmpAmf0.WriteValue |

### Audited paths that are not ProtocolRejected

- Basic headers use their existing one/two-byte extended chunk stream IDs.
  A compressed header without a prior context rejects; truncated input is EOF,
  not a new protocol rejection.
- Truncated headers, extended timestamps and bodies remain Disconnected;
  IO deadlines remain TimedOut; cancellation and transport failures retain
  their original classifications.
- Message header format inheritance, chunk continuations, assembly/context
  budgets and chunk-size limits retain their original conditions and order.
- Abort removes only an existing incomplete context; unknown abort IDs remain
  ignored. ACK/window/peer bandwidth/ping handling is unchanged.
- releaseStream and FCPublish retain publish-route and session-policy checks.
  FCUnpublish/deleteStream/closeStream preserve clean terminal behavior.
- Malformed AMF is rejected even inside unknown metadata events; recognized
  @setDataFrame/onMetaData keep original bytes after wrapper removal.
  Fully decoded unknown events and unsupported body shapes remain ignored.
- Metadata's 64 KiB AMF limit and separate retained bootstrap limit are distinct.
- Duplicate identical AVC/AAC configurations are allowed; changed configuration
  rejects. Both configs are required before HEAD; order is canonical AVC then
  AAC. Initial audio/interframes remain gated until a true post-HEAD IDR.
- DTS, extended timestamps and signed CTS are preserved. Backward DTS is not
  newly rejected; inconsistent extended chunk timestamps still reject.
- Borrowed PCP consumer exceptions remain ConsumerRejected. Owner/HTTP child
  generation/order failures are downstream faults, not protocol rejection sites.
- The response encoder's unsupported-value condition is a server invariant;
  it has a finite AmfResponse identifier but no broader response-value support.

## Architecture

`RtmpChunkReader / RtmpAmf0 / ObsRtmpProtocolSession / ObsFlvPublishingSession`
→ `InvalidDataException` carrying `ObsRtmpRejectionDiagnostic`
→ `ObsRtmpIngestResult.Rejection`
→ `ObsPublishChannelResult.Rejection`
→ normal JSON evidence serialization.

The internal `ObsRtmpRejection` helper tags the exception at the actual reject
site. Existing exception type and text are preserved; no substring parsing.
Outer boundaries only fill missing context and preserve the first stage/reason.
The public immutable diagnostic and context contain only fixed scalar fields.
No event list, packet memory, arbitrary string or metadata object is retained.
A single terminal rejection is propagated; successful sessions have null.

Existing result constructor/deconstruction arity is preserved by additive init
properties. RTMP messages gain an internal optional chunk stream ID without
changing their positional constructor. Enum numeric values are explicit and
must not be renumbered; JSON enum names are stable evidence identifiers.

Context includes message type/stream IDs/timestamp, header format, declared and
assembled lengths, a finite allowlisted command/event token, finite transaction
ID, codec packet type/config length and bootstrap-state flags. Unknown names map
to Unknown, not to the incoming text. Application, stream key, credentials,
property names/values, audio/video bytes and arbitrary exception text are absent.
Missing context remains null rather than being guessed. Partial-assembly context
describes the current incomplete message; no body is returned or retained.

For example, serializing a managed rejected five-byte ASC fixture produces the
following diagnostic fragment. This is a managed example, not a physical capture:

```json
{
  "Stage": "AacSequenceHeader",
  "Reason": "UnsupportedAudioSpecificConfig",
  "Context": {
    "MessageType": 8,
    "ChunkStreamId": 6,
    "MessageStreamId": 1,
    "Timestamp": 0,
    "DeclaredPayloadLength": 7,
    "AssembledPayloadLength": 7,
    "PacketType": 0,
    "ConfigLength": 5,
    "BootstrapState": "None"
  }
}
```

Use `JsonSerializer.Serialize(result)` for the complete safe owner result, or
serialize `result.Rejection` for this fragment. Default serialization also emits
null context fields omitted from the abbreviated example above. No new logger or
automatic evidence file writer is introduced by the production API.

## AAC observation

This section records the earlier diagnostic implementation slice. The later
real-OBS diagnostic and bounded compatibility fix are documented in
[AAC compatibility](obs-aac-audiospecificconfig-compatibility.md); that evidence
establishes the exact-length rejection while leaving original ASC bytes unknown.

At this diagnostic slice's predecessor, behavior required exactly two ASC bytes, AAC-LC object type 2,
an indexed sample rate and mono/stereo, with the existing reserved-bit check.
Rejected variants receive AacSequenceHeader / UnsupportedAudioSpecificConfig.
Compatibility change performed: **NO**.

**NOT ESTABLISHED AS THE PHYSICAL FAILURE CAUSE.** The previous run retained no
AAC payload and no evidence that ValidateAac was reached.

## Managed validation

Windows Release validation for this diagnostic slice:

| Scope | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| New rejection diagnostic tests | 68 | 0 | 0 |
| Existing OBS compatibility | 17 | 0 | 0 |
| Existing Network ingest | 82 | 0 | 0 |
| Existing ingest-to-Playback parser | 1 | 0 | 0 |
| Owner / relay / HTTP-FLV / localhost regression | 191 | 0 | 0 |
| Nova.Network (full solution run) | 480 | 0 | 0 |
| Nova.Core | 310 | 0 | 0 |
| Nova.Playback | 3102 | 15 | 0 |
| Full solution | 4351 | 15 | 0 |

The existing ingest total 83 is the Network 82 plus Playback parser 1. Filtered
rows overlap with project/full-solution rows and are not additive. Release build
had zero warnings/errors; full-solution format verification and diff check passed.
Physical tests requiring explicit opt-in remain skipped. CI uses Ubuntu and its
platform-specific pass/skip counts must be reported separately at the new HEAD.

## Safety and evidence boundary

- Raw payload logged: **NO**
- Unbounded diagnostic history: **NO**
- Stream secret exposure: **NO**
- Parser limits weakened: **NO**
- NO REAL OBS RETRY PERFORMED
- REAL OBS INTEROPERABILITY BLOCKED — PHYSICAL A/V ACCEPTANCE NOT PROVEN

Managed rejection tests establish identifiers/propagation, not the historical
physical cause. A separately authorized one-shot real OBS diagnostic capture is
the next boundary. Only after that evidence identifies a rejection should a
minimal compatibility fix be considered in its own slice.
