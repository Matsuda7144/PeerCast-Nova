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
- 0.0.9m Finite Awaited Decoded Completion Composition Boundary
- 0.0.9n Decoded Completion Transfer and Scheduler Ingress Review
- 0.0.9o One-shot Decoded Completion Transfer and Scheduler Ingress Boundary
- 0.0.9p Generation-matched Scheduler Delivery and Output Lifecycle Review
- 0.0.9q Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary
- 0.0.9r Production Decoder Generation Factory and Activation Review
- 0.0.9s Policy-gated Production Decoder Generation Factory Boundary
- 0.0.9t Production Generation Ingress and Replacement Composition Review

0.0.9s implements the policy-gated complete generation factory. The public path remains `LicenseReviewPending`; approved injected conditions create exact FLV binding, distinct video／audio runtime and ABI ownership, source-bound decode, bounded scheduler, timing, and result-aware delivery as one caller-owned product.

0.0.9t establishes that the existing consumed-settlement composition cannot directly host that product. Production scheduler transfer consumes and detaches decoded ownership, so a new production-specific composition must forward finite encoded ingress through the singular generation owner, never touch a completion after `Transferred`, keep delivery caller-driven, and stop the old generation before parser discontinuity disposes its borrowed source.

### Next: 0.0.9u Finite Production Generation Ingress and Replacement Composition Boundary

Implement one production-specific boundary that:

- keeps the existing consumed-settlement composition unchanged;
- owns one parser, one dedicated production factory, and at most one production generation;
- borrows one application-owned Windows output boundary;
- internally forwards exact decode snapshot, one-event submission, and one-frame receive through the production product;
- serializes packet input, finalization, receive／drain, transfer, one-item delivery, reset, replacement, and stop;
- validates a decoded completion before one transfer attempt;
- treats `Transferred` as final ownership settlement and never accesses the completion afterward;
- maps retained output states without consuming the scheduler reservation;
- exposes caller-driven one-item delivery and explicit finite reset outside the PCP callback;
- treats rejected scheduler admission as terminal without capacity polling or hidden retry;
- asynchronously stops the old generation before parser discontinuity reset;
- creates the replacement only after a new exact parser source description exists;
- forbids overlapping generations, old-generation revival, rollback, and partial packet commit;
- uses one ordered asynchronous terminal cleanup path;
- adds no resident loop, fallback runtime, device activation, or second queue.

### Later sequence

1. validate the finite production composition with approved injected ownership and retained FLV fixtures;
2. make an explicit license／backend registration and distribution decision;
3. connect the composition under an application-owned Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production license approval and backend registration
- discontinuity reset and `avcodec_flush_buffers`
- duration cutoff
- independent audio recovery generation changes
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
