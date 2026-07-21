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

0.0.9s implements the policy-gated complete generation factory. The public path remains `LicenseReviewPending`; approved injected conditions create exact FLV binding, distinct video／audio runtime and ABI ownership, source-bound decode, bounded scheduler, timing, and result-aware delivery as one caller-owned product.

0.0.9u implements the dedicated production owner. It serializes awaited PCP／FLV parsing, source-bound submission and receive, one-shot scheduler transfer, caller-driven delivery, explicit reset, stop, and discontinuity replacement.

0.0.9v accepts the exact BtbN `win64-lgpl-shared` candidate class in principle for external dynamic use. Nova permanently keeps FFmpeg binaries outside the default Apache-2.0 distribution, prohibits implicit discovery and automatic acquisition, rejects GPL／nonfree variants, places production registration at the application composition root, and requires a checksum-verified host manifest so users can authorize compatible modified LGPL builds. Candidate state remains `Pending` until those compliance requirements are implemented.

### Next: 0.0.9w External LGPL Runtime Compliance and User Replacement Boundary

Implement one finite compliance boundary that:

- represents the reviewed built-in candidate and a host-supplied candidate with bounded immutable manifests;
- accepts only `LGPL-3.0-or-later` and rejects GPL, nonfree, unknown, or undefined license classes;
- requires exact DLL roles, recognizable filenames, one SHA-256 per DLL, and the reviewed FFmpeg ABI major;
- rejects duplicate／missing roles, traversal, inconsistent manifests, wrong ABI, missing files, and checksum mismatch before native loading;
- composes with the existing explicit-directory runtime locator, retained handles, and ABI symbol gate;
- keeps `PATH`, registry, conventional-directory, network, download, extraction, update, and fallback discovery absent;
- includes verbatim GPLv3 and LGPLv3 texts in distributable output;
- includes a prominent FFmpeg third-party notice and user-readable replacement instructions;
- permits a user to authorize an interface-compatible modified shared build through their own verified manifest;
- proves that replacement support does not disable integrity verification;
- proves that bundled, GPL, nonfree, and implicit-discovery paths remain impossible;
- changes the built-in candidate from `Pending` to `Approved` only after all compliance tests pass.

### Later sequence

1. connect the approved external runtime under the application-owned Windows lifecycle;
2. perform controlled retained-fixture and real-media decode validation;
3. perform controlled Windows audio／video output validation;
4. expand user-facing lifecycle and packaging only after the preceding gates pass.

### Deferred

- FFmpeg binary bundling or Nova-hosted runtime pack
- MP4／fragmented MP4 parser
- avformat integration
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
- do not bundle, mirror, rehost, download, or install FFmpeg in the default distribution
- preserve explicit-directory and checksum／ABI verification for user replacements
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- approved external runtime compliance and replacement boundary
- finite decoded buffering and output lifecycle
- controlled audible／visible verification
- user-facing application and packaging
