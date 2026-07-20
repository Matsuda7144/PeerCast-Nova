# Roadmap

## 0.0.1-0.0.4 Network Foundation — Verified

- Foundation／Core lifecycle
- bounded TCP／PCP／Channel
- official PeerCastStation compatibility
- Relay fan-out and controlled real-media relay verification

## 0.0.5 Playback Foundation — Verified

- separate `Nova.Playback` project
- bounded encoded input
- replaceable decoder lifecycle
- canonical decoded output contract
- backend provenance and audit boundaries

## 0.0.6 Decoder and Output Boundaries — Implemented; activation pending

- verified FFmpeg runtime lease and ABI gate
- native context through packet／frame processing
- managed copy、conversion、awaited delivery
- bounded scheduler and Windows output boundaries

## 0.0.7 Timing and Late-aware Dispatch — Implemented

- monotonic timing
- late-item policy
- reservation handling
- finite pumping

## 0.0.8 Recovery, Demux, and Parameter Binding — Verified

- recovery and finite run budgeting
- diagnostic native packet／frame path
- managed frame copy, conversion, scheduler ingress, and late-aware dispatch
- bounded demux output contract
- pure managed bounded FLV v1 parser
- parser-to-decoder composition review
- exact FLV session decoder-parameter binding

## 0.0.9 Source-bound Native Decoder and Packet Ownership — In Progress

### Completed

- 0.0.9a Timestamp-aware Packet Submission Architecture Review
- 0.0.9b Source-bound Opened Native Decoder Session

0.0.9b consumes one live FLV parameter binding and distinct video/audio ABI gate ownership chains. It opens H.264 before AAC, preserves source identity, stream IDs, and time bases, returns no partial success, and owns exactly two opened contexts on success.

### Next: 0.0.9c AVPacket Metadata and Flush ABI Review

The review must decide:

- exact approved FFmpeg runtime identity;
- AVPacket layout or verified setter mechanism;
- PTS, DTS, duration, time base, keyframe, unknown-value, and stream mapping;
- null-packet flush behavior;
- ABI gate strengthening and validation evidence;
- no direct field writes under a major-version-only assumption.

### Later sequence

1. implement one approved metadata-aware native packet／flush boundary;
2. implement a finite one-event submission state machine retaining the active parser completion under frame-drain pressure;
3. validate with a retained controlled FLV fixture;
4. review PCP chunk-to-parser composition and discontinuity ownership;
5. add bounded decode iteration and per-stream EOS drain;
6. review production activation and Windows lifecycle;
7. perform controlled real-media validation.

### Deferred

- AVPacket metadata field writes until review approval
- timestamp-aware packet submission
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- avformat integration
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete synchronization
- controlled audible／visible playback

## CI policy

Routine Linux CI uses CircleCI while GitHub Actions usage is exhausted. Final branch heads must pass restore, Release build, all tests, and format verification. Windows CI remains reserved for explicit milestone or release validation.

## Platform and dependency policy

- retain `net10.0`
- avoid speculative production dependencies
- keep FFmpeg host-installed and dynamically loaded
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- approved runtime／distribution decision
- finite decoded buffering and output lifecycle
- controlled audible／visible verification
- user-facing application and packaging
