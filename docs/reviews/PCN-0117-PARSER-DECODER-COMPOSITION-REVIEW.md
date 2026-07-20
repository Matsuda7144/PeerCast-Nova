# PCN-0117: Parser-to-Decoder Composition Review

## Scope

0.0.8y reviews how the 0.0.8x bounded FLV parser should connect to the existing
managed codec-parameter and native packet/frame boundaries. It selects the next
safe implementation seam without adding runtime code.

## Reviewed boundaries

- `PlaybackBoundedFlvParser`
- `PlaybackBoundedDemuxSession`
- `PlaybackBoundedDemuxReadCompletion`
- `PlaybackDecoderFfmpegCodecParametersFactory`
- `PlaybackDiagnosticDecoderConfigurationAdapter`
- `PlaybackDiagnosticNativeParameterTransaction`
- `PlaybackDiagnosticCodecOpenTransaction`
- `PlaybackDecoderFfmpegPacketFrameBoundary`
- `PlaybackDiagnosticPacketFrameTransaction`

## Confirmed compatible seam

The parser session can be mapped to the existing managed codec-parameter factory
without information loss:

- video codec is H.264;
- video width and height are SPS-derived and bounded;
- video extradata remains the validated AVCDecoderConfigurationRecord;
- audio codec is AAC Low Complexity;
- audio sample rate and channel count are AudioSpecificConfig-derived and bounded;
- audio extradata remains the validated AudioSpecificConfig;
- the factory independently validates, copies, pads, owns, and clears the data.

The next implementation is therefore a pure managed session-to-parameter binding
rather than a native or packet-submission transaction.

## Required 0.0.8z contract

### Input validation

- A source session is required and must not be disposed.
- Container kind must be FLV.
- Video must be stream 0, H.264, and video media.
- Audio must be stream 1, AAC, and audio media.
- Stream identifiers must remain distinct and non-negative.
- Both time bases must be exactly `1/1000`.
- Codec data must be live and non-empty.
- Video dimensions and audio configuration must pass the existing factory.
- Undefined enum and contradictory session state fail closed.

### Output binding

- The adapter returns fixed prepared, invalid-session, video-rejected,
  audio-rejected, disposed, and fault results.
- Success owns exactly one video parameter lease and one audio parameter lease.
- The binding retains a non-owning reference to the exact source session.
- The binding snapshots video/audio stream identifiers and time bases.
- The binding does not own parser or session lifetime.
- Disposal clears audio parameters before video parameters and is idempotent.
- Partial creation failure disposes every created lease.

### Safety boundaries

- No encoded packet is copied or retained.
- No parser completion is consumed or disposed.
- No native runtime, gate, context, symbol, parameter transfer, or codec open is
  created.
- No backend factory or activation path changes.
- No AVCC-to-Annex-B conversion.
- No Windows-only execution requirement.

## Confirmed incompatible direct seam

The existing paired diagnostic packet transaction cannot represent real FLV
output because:

- it always sends video then audio, while FLV emits one interleaved event at a
  time;
- its packet boundary accepts payload only and cannot preserve PTS, DTS,
  duration, time base, keyframe state, discontinuity, or sequence;
- `NeedFrameDrain` is treated as rejection instead of retained retry state;
- parser completion ownership is not retained across decoder backpressure;
- EOS cannot be represented because empty payload is rejected and no explicit
  flush/drain operation exists;
- source-session identity is not checked;
- one packet cannot select and advance only its matching decoder context.

Direct composition would therefore lose information or dispose ownership too
early and is rejected.

## Later timestamp-aware packet review criteria

A later milestone must define:

- one-event-at-a-time context selection;
- exact source-session identity;
- parser-order preservation;
- timestamp and keyframe transfer into AVPacket;
- explicit decoder-backpressure retention;
- bounded frame receive and retry state;
- explicit EOS flush and bounded drain;
- discontinuity and stale-binding rejection;
- no second encoded-packet queue;
- separate ABI review for any AVPacket layout or new native symbols.

## Evidence

- `PlaybackDecoderFfmpegCodecParametersFactory.Create` accepts exactly the fields
  held by the parser session and creates independent padded ownership.
- `PlaybackDecoderFfmpegPacketFrameBoundary.SendPacket` currently accepts only
  `ReadOnlyMemory<byte>` payload.
- `PlaybackDiagnosticPacketFrameTransaction` requires a paired video packet and
  audio packet and accepts only `Sent` before one receive attempt.
- The 0.0.8x parser emits at most one completion per finite operation and blocks
  further parsing until that completion is disposed.

## Validation

The milestone is documentation-only. The final branch head must pass restore,
Release build, the full test suite, and format verification on routine Ubuntu
CI. The diff must contain only ADR-0109, PCN-0117, and synchronized project
progress documents, remain based directly on current main, and have no
unresolved review threads before merge.

Status: Architecture review complete; final branch-head CI is the merge gate.
