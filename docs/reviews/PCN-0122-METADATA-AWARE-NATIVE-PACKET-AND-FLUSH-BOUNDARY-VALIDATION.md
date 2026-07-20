# PCN-0122: Metadata-aware Native Packet and Flush Boundary Validation

## Scope

Validate the 0.0.9d finite boundary from one exact source-owned bounded FLV event into one metadata-aware FFmpeg packet send or one per-stream null flush.

## Verified behavior

- exact source-session identity, stream ID, media kind, codec role, and snapshotted time base are required before native work;
- one packet or one per-stream EOS event is accepted per operation;
- first-stream EOS may arrive in a `PacketsAvailable` completion, while final-stream EOS may arrive in an `EndOfStream` completion;
- PTS, DTS, duration, stream index, time base, and keyframe flag are mapped without implicit rescaling;
- unknown PTS/DTS map to `AV_NOPTS_VALUE` and unknown duration maps to `0`;
- negative duration and non-integral time-base mapping fail closed;
- payload above the native boundary limit is rejected before allocation;
- FFmpeg-owned packet and padded payload storage are used with the reviewed FFmpeg 8 x64 metadata offsets;
- one native send or null flush is issued per boundary operation;
- packet `EAGAIN` retains both packet and parser completion;
- flush `EAGAIN` retains the active per-stream EOS completion;
- parser finite-read backpressure remains active while a completion is retained;
- cleanup releases packet ownership before parser completion and is idempotent;
- undefined native outcomes fail closed;
- receive loops, hidden retries, second encoded queues, PCP composition, backend activation, distribution, and output remain absent.

## Tests

Fourteen focused tests cover exact metadata, unknown values, packet `EAGAIN`, first and final per-stream EOS, flush `EAGAIN`, wrong source, time-base mismatch, non-integral timestamps, negative duration, payload limits, undefined native results, required input padding, reviewed ABI offsets, source-owned backpressure, and packet-first cleanup.

An implementation audit after the first green head found that valid first-stream EOS uses `PacketsAvailable`, not only `EndOfStream`. The boundary and tests were corrected before final documentation. The same audit changed retained packet send ownership from one-lifetime-only to serialized reusable ownership so the later state machine can retry only after explicit receive progress.

Status: Complete; final documentation head must pass CircleCI before merge.