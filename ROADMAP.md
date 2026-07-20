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
- 0.0.9c AVPacket Metadata and Flush ABI Review

0.0.9c approves a future packet boundary only under the exact verified FFmpeg 8 runtime identity. Packet storage must come from FFmpeg allocation APIs, only the reviewed decoding metadata prefix may be written, EOS is a null packet send, and `EAGAIN` retains the active parser completion without a second queue or hidden retry.

### Next: 0.0.9d Metadata-aware Native Packet and Flush Boundary

Implement one finite boundary that:

- consumes one source-bound opened decoder session reference without taking parser ownership;
- validates exact source, stream ID, media kind, and time base;
- allocates one FFmpeg-owned padded packet and copies one parser-owned payload;
- maps PTS, DTS, duration, stream index, time base, and keyframe state exactly;
- maps unknown timestamps to `AV_NOPTS_VALUE` and unknown duration to `0`;
- performs at most one native packet-send or null-flush call;
- returns explicit success, output-required, EOF, rejection, and fault states;
- retains packet and parser-completion ownership on `EAGAIN`;
- releases packet state before parser completion on cleanup;
- adds no receive loop, second encoded queue, retry loop, PCP wiring, or activation.

### Later sequence

1. implement a finite one-event submission state machine retaining the active parser completion under frame-drain pressure;
2. validate with a retained controlled FLV fixture;
3. review PCP chunk-to-parser composition and discontinuity ownership;
4. add bounded decode iteration and per-stream EOS drain;
5. review production activation and Windows lifecycle;
6. perform controlled real-media validation.

### Deferred

- one-event submission state machine
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- avformat integration
- production decoder ingress
- continuous decode、flush、EOS drain
- discontinuity reset and `avcodec_flush_buffers`
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