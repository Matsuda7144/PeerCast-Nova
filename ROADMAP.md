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
- 0.0.9d Metadata-aware Native Packet and Flush Boundary
- 0.0.9e Finite One-event Packet Submission State Machine
- 0.0.9f Controlled Retained FLV Fixture Validation
- 0.0.9g One-event Final EOS Sequencing
- 0.0.9h PCP Chunk-to-FLV Parser Composition Review
- 0.0.9i Finite Awaited PCP-to-FLV Composition Boundary
- 0.0.9j Bounded Decode Iteration and Per-stream EOS Drain Review
- 0.0.9k Source-bound One-frame Receive and EOS Drain Boundary
- 0.0.9l Awaited Decoded Completion Composition Review

0.0.9l selects one future awaited PCP／FLV／decode generation that owns the 0.0.9k boundary as its sole submission and receive authority. Decoded completion settlement blocks parser and encoded retry while incomplete, required relief stays inside the PCP callback, ordinary receive and accepted-flush drain remain finite caller operations, and the existing bounded scheduler remains the only decoded queue.

The review also separates stream-local decoded identity from the scheduler's cross-media order. A generation-wide downstream sequence commits only after accepted ownership transfer, and scheduler-owned frames remain bound to the matching decoder generation and context lifetime.

### Next: 0.0.9m Finite Awaited Decoded Completion Composition Boundary

Implement the reviewed composition with injected cross-platform operations:

- replace the current generation's separate native-session／submission ownership with one `PlaybackSourceBoundOneFrameDecodeBoundary`;
- add one generation-scoped awaited decoded-completion settlement seam;
- hold the PCP callback open across one-frame relief, settlement, completion release, and one retry-only transition;
- repeat retained packet／flush relief only under shared frame-count, managed-byte, decoder-advance, and parser-operation budgets;
- block parser suffix and later PCP input while decoded or encoded ownership remains active;
- add serialized ordinary receive outside the PCP callback with normal `NeedMoreInput`;
- keep explicit encoded finalization separate from independent one-frame video／audio drain to decoder EOF;
- expose fixed accepting, input-finalized, decoder-drained, faulted, and disposed state without claiming downstream playback completion;
- handle cancellation, consumer rejection, source disposal, no progress, work limits, and discontinuity fail closed;
- dispose active settlement／decoded completion before decode-boundary ownership;
- add no real scheduler adapter, output device activation, second decoded queue, resident task, or production backend claim.

### Later sequence

1. add one-shot decoded-frame transfer and real-FLV bounded scheduler ingress using a generation-wide cross-media sequence;
2. add generation-matched awaited scheduler delivery and output lifecycle composition;
3. add a production generation factory and review ABI-gate acquisition／backend activation／Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production generation factory and backend activation
- real-FLV scheduler ingress／awaited delivery
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