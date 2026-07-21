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
- 0.0.9u Finite Production Generation Ingress and Replacement Composition Boundary
- 0.0.9v FFmpeg Candidate License, Backend Registration, and Distribution Review
- 0.0.9w External LGPL Runtime Compliance and User Replacement Boundary

0.0.9s implements the policy-gated complete generation factory. Approved provenance plus explicit third-party and native policy creates exact FLV binding, distinct video／audio runtime and ABI ownership, source-bound decode, bounded scheduler, timing, and result-aware delivery as one caller-owned product.

0.0.9u implements the dedicated production owner. It serializes awaited PCP／FLV parsing, source-bound submission and receive, one-shot scheduler transfer, caller-driven delivery, explicit reset, stop, and discontinuity replacement.

0.0.9v accepts the exact BtbN `win64-lgpl-shared` candidate class in principle for external dynamic use. Nova permanently keeps FFmpeg binaries outside the default Apache-2.0 distribution, prohibits implicit discovery and automatic acquisition, rejects GPL／nonfree variants, and places production registration at the application composition root.

0.0.9w implements the compliance gate required by that review. It adds immutable built-in and host manifests, accepts only `LGPL-3.0-or-later`, validates the exact five DLL roles, filenames, lengths, hashes, and ABI majors, preserves every existing runtime and symbol gate for user replacements, packages the required GNU license texts and FFmpeg notice, and changes the exact built-in candidate to `LicenseReview.Approved`.

### Next: 0.0.9x Approved External Runtime and Windows Lifecycle Activation Review

Review one application-owned activation composition that:

- selects one approved built-in or host-supplied manifest;
- receives one explicit runtime directory without discovery or fallback;
- owns Windows adapter selection, COM／window activation, device resources, bindings, and stream initialization;
- creates and borrows one `PlaybackWindowsOutputBufferProcessingBoundary` for the production decode composition;
- defines exact construction order before encoded input admission;
- defines stop order across input admission, production generation, scheduler reservations, output processing, streams, bindings, activation, devices, and COM resources;
- defines explicit reset and device-loss behavior without automatic recovery loops;
- keeps parser discontinuity replacement separate from application device replacement;
- prevents duplicate ownership of windows, adapters, devices, events, output buffers, runtime leases, and decoder generations;
- specifies controlled retained-fixture and real-media Windows validation evidence;
- keeps audible／visible playback unclaimed until the subsequent implementation and validation milestones pass.

### Later sequence

1. implement the reviewed application-owned Windows activation composition;
2. perform controlled retained-fixture production activation validation;
3. perform controlled real-media decode validation;
4. perform controlled Windows audio／video output validation;
5. expand user-facing lifecycle and packaging only after the preceding gates pass.

### Deferred

- FFmpeg binary bundling or Nova-hosted runtime pack
- MP4／fragmented MP4 parser
- avformat integration beyond the reviewed runtime inventory
- discontinuity reset and `avcodec_flush_buffers`
- duration cutoff
- independent audio recovery generation changes
- complete synchronization
- controlled audible／visible playback

## CI policy

Routine Linux CI uses CircleCI while GitHub Actions usage is exhausted. Final branch heads must pass restore, Release build with analyzers, all tests, and format verification. Windows CI remains reserved for explicit milestone or release validation.

## Platform and dependency policy

- retain `net10.0`
- avoid speculative production dependencies
- keep FFmpeg host-installed and dynamically loaded
- do not bundle, mirror, rehost, download, or install FFmpeg in the default distribution
- preserve explicit-directory and checksum／ABI verification for user replacements
- keep Windows output resources application-owned
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- approved external runtime compliance and replacement boundary
- application-owned Windows activation composition
- finite decoded buffering and output lifecycle
- controlled audible／visible verification
- user-facing application and packaging