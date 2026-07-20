# ADR-0113: AVPacket Metadata and Flush ABI Review

Status: Accepted

## Context

0.0.9b owns one source-bound opened H.264/AAC decoder-context pair but deliberately does not compose or submit `AVPacket` values. The existing ABI gate proves fixed FFmpeg DLL identities, ABI majors, imports, and symbols. A major-version match alone does not prove that arbitrary managed `AVPacket` field writes are safe.

The bounded FLV parser already supplies owned encoded payload, independently known or unknown PTS and DTS, optional duration, stream identity, keyframe state, sequence, and discontinuity generation. The next native boundary must preserve that metadata without creating a second encoded queue or hiding decoder backpressure behind retries.

## Decision

Approve one metadata-aware native packet and flush ABI boundary only under the exact verified FFmpeg 8 runtime identity already accepted by the runtime lease. Major-version-only or unverified runtime use remains rejected.

### Runtime and allocation boundary

- The ABI gate must retain the exact verified runtime lease and require the pinned five-DLL identity before packet operations are exposed.
- `AVPacket` storage must be created by `av_packet_alloc` and released by `av_packet_free`; managed code must never allocate storage using `sizeof(AVPacket)`.
- Payload storage must be created through an FFmpeg packet allocation API and must include FFmpeg input padding. Caller payload ownership is copied once into the native packet; no second resident encoded queue is introduced.
- Packet cleanup must use `av_packet_unref` before reuse and `av_packet_free` at final ownership release.

### Approved metadata prefix

For the exact accepted runtime identity, the managed interop layout may expose only the reviewed prefix fields required by decoding:

- `pts` and `dts` as signed 64-bit values;
- `data` and `size` as the FFmpeg-owned payload buffer;
- `stream_index` for exact source stream mapping;
- `flags` with only `AV_PKT_FLAG_KEY` currently approved;
- `duration` as signed 64-bit packet duration;
- `time_base` as the exact positive parser time base.

Unknown PTS and DTS map to `AV_NOPTS_VALUE`. Unknown duration maps to `0`, matching the FFmpeg packet contract. Packet position is not supplied and remains unknown. No side data, opaque state, corruption flag, discard flag, or disposable flag is synthesized.

The interop definition is a reviewed runtime-specific prefix, not a claim that the complete `AVPacket` layout is stable across arbitrary FFmpeg builds. New fields may not be added to the managed view without a later ABI review.

### Stream and timestamp mapping

- Video events must match the session video stream ID and use the snapshotted video time base.
- Audio events must match the session audio stream ID and use the snapshotted audio time base.
- Mismatched source identity, stream ID, media kind, time base, undefined enum, invalid rational, impossible payload size, or disposed ownership fails closed before native send.
- PTS, DTS, and duration are written without implicit rescaling because the parser event and source-bound session already share the same exact time base.
- Sequence and discontinuity generation remain managed composition state; they are validated by the later submission state machine and are not written into undocumented packet fields.

### Flush boundary

End-of-stream flush is a distinct operation that calls `avcodec_send_packet(context, NULL)`. It must not allocate an empty packet and must not use a zero-size packet as an EOS surrogate.

The first accepted null send enters decoder draining. A repeated flush is not retried implicitly; `AVERROR_EOF`, `AVERROR(EAGAIN)`, success, and other failures are returned as explicit bounded results for the caller-owned state machine.

`avcodec_flush_buffers` is not part of EOS drain. It may only be considered later for an explicitly reviewed discontinuity or decoder-reset transition after ownership and output consequences are defined.

### Backpressure and ownership

The packet boundary performs one finite compose-and-send attempt. When `avcodec_send_packet` returns `AVERROR(EAGAIN)`, the native packet and active parser completion remain owned by the caller-visible completion until bounded frame receive work makes progress. The boundary does not retry, sleep, spin, pump, retain caller suffixes, or enqueue a second encoded copy.

## Consequences

0.0.9d may implement one runtime-identity-gated metadata-aware packet/flush boundary with explicit ownership and fixed results. It may not connect PCP input, activate the FFmpeg backend, distribute FFmpeg, add continuous decode, or claim EOS drain completion.

## Validation requirements

Implementation must use injected native operations for deterministic cross-platform tests and retain CircleCI as routine CI. Tests must cover exact metadata mapping, unknown values, keyframe mapping, stream/time-base rejection, payload bounds and padding, one-copy ownership, `EAGAIN` retention, null flush, repeated flush results, cleanup order, undefined native outcomes, and idempotent disposal.