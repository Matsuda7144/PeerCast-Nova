# Changelog

## [Unreleased]

Detailed history through 0.0.8g remains preserved in Git history at commit
`f078dab5372a45a8c577c1c802a697ab72b45c02` and in milestone ADR/PCN records.

### 0.0.9b - Source-bound Opened Native Decoder Session

- Added one finite factory from a live FLV parameter binding plus distinct video/audio ABI gate leases into one opened H.264/AAC decoder session.
- Snapshotted exact source-session identity, stream IDs, and time bases before native work.
- Completed video context creation, parameter transfer, and codec open before beginning audio.
- Consumed the binding and both gates on every result and returned no partial success state.
- Failed closed on invalid configuration, source disposal, context rejection, parameter-transfer rejection, codec-open rejection, and faults.
- Added audio-before-video idempotent cleanup without owning the parser or source demux session.
- Added six focused injected-operation tests and fixed one CS9124 test-only compiler issue.
- Added ADR-0112 and PCN-0120.
- Kept AVPacket metadata writes, packet submission, flush, drain, PCP composition, backend activation, and output unchanged.

### 0.0.9a - Timestamp-aware Packet Submission Architecture Review

- Determined that source-bound opened native contexts must exist before packet submission.
- Rejected the old diagnostic native lease for real FLV composition because it lacks source identity, stream IDs, and time bases.
- Selected 0.0.9b for source-bound native session ownership and 0.0.9c for exact AVPacket metadata and flush ABI review.
- Required active parser-completion retention under decoder backpressure, explicit null-packet EOS flush, bounded drain, no second encoded queue, and no hidden retry loop.
- Added ADR-0111 and PCN-0119.

### 0.0.8z - FLV Session Decoder-Parameter Binding

- Added a pure managed adapter from one exact live FLV session to independently owned padded H.264/AAC codec-parameter leases.
- Preserved source identity, stream IDs, and `1/1000` time bases without native calls or packet consumption.
- Added ADR-0110 and PCN-0118.

### 0.0.8x-0.0.8y - Bounded FLV Parser and Composition Review

- Added the pure managed bounded FLV v1 parser with owned packet/EOS completions, strict AVC/AAC bootstrap, timestamps, discontinuity, and one-active-completion backpressure.
- Reviewed the parser-to-decoder seam and rejected metadata-losing reuse of the paired diagnostic packet transaction.
- Added ADR-0108/0109 and PCN-0116/0117.

### Earlier active playback milestones

0.0.8v and earlier active integration entries remain available in repository history and their ADR/PCN records.
