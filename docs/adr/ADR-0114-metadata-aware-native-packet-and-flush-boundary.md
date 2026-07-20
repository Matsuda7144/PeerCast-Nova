# ADR-0114: Metadata-aware Native Packet and Flush Boundary

Status: Accepted

## Context

0.0.9c approved one exact-runtime-gated AVPacket metadata and null-flush ABI boundary before timestamp-aware submission state is connected to frame receive work. The bounded FLV parser already owns one active completion at a time, and the 0.0.9b native decoder session preserves the exact source identity, video/audio stream IDs, and time bases.

## Decision

Add one finite `PlaybackMetadataAwareNativePacketBoundary.SubmitOne` operation over exactly one source-owned demux completion.

The boundary:

- requires the exact live source session referenced by the opened native decoder session;
- accepts one packet or one per-stream EOS event only;
- validates stream ID, media kind, codec role, keyframe role, and exact snapshotted time base before native work;
- rejects packet discontinuity until reset semantics are separately reviewed;
- maps known PTS, DTS, and duration only when they are integral in the exact packet time base;
- maps unknown PTS/DTS to `AV_NOPTS_VALUE` and unknown duration to `0`;
- maps video keyframe state only to `AV_PKT_FLAG_KEY`;
- creates packet and payload storage through FFmpeg allocation functions, writes the reviewed FFmpeg 8 x64 prefix, and zeroes the required input padding;
- performs one native packet send or one null-packet flush call per operation;
- returns explicit submitted, output-required, EOF, rejection, allocation, gate, unsupported, and fault results.

A packet `EAGAIN` completion retains both the FFmpeg-owned packet and the active parser completion. A flush `EAGAIN` completion retains the active per-stream EOS completion. Disposal is idempotent and releases native packet ownership before releasing parser backpressure.

The retained packet owner serializes individual native send calls but is not permanently consumed by the first `EAGAIN`; a later bounded submission state machine may retry it only after explicit frame receive progress.

## Consequences

0.0.9d provides the first real FLV-event-to-native-packet boundary without connecting a frame receive loop, hidden retry, second encoded queue, PCP input, backend activation, FFmpeg distribution, or output.

The next milestone must add a finite one-event submission state machine that performs explicit receive progress before any retained packet or flush retry and that preserves per-stream EOS state.

## Validation

Injected cross-platform tests cover exact and unknown metadata mapping, keyframe mapping, source and time-base rejection, non-integral and negative timing rejection, payload bounds, per-stream and final EOS, packet and flush `EAGAIN` retention, parser backpressure, packet-first cleanup, undefined native outcomes, idempotent disposal, FFmpeg input padding, and reviewed metadata offsets. CircleCI remains the routine merge gate.