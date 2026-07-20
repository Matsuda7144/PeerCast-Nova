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
- callback-scoped frame data
- managed copy、conversion、awaited delivery
- bounded scheduler and Windows output boundaries

## 0.0.7 Timing and Late-aware Dispatch — Implemented

- monotonic timing
- late-item policy
- reservation handling
- finite pumping

## 0.0.8 Recovery and Diagnostic Integration — Verified through parameter binding

### Verified sequence

- 0.0.8a-0.0.8c Recovery and Run Budget
- 0.0.8d-0.0.8j Diagnostic Gate through Packet／Frame
- 0.0.8k Frame Metadata
- 0.0.8l Frame Data
- 0.0.8m Managed Frame Copy
- 0.0.8n Managed Format Conversion
- 0.0.8o Awaited Sink Delivery
- 0.0.8p Playback Cutover Review
- 0.0.8q Scheduler Aggregate Byte Budget
- 0.0.8r Managed Frame Scheduler Ingress
- 0.0.8s Scheduler Dispatch Architecture Review
- 0.0.8t Diagnostic Late-aware Scheduler Dispatch
- 0.0.8u Bounded Demux Output Contract Review
- 0.0.8v Pure Managed Bounded Demux Output Contract
- 0.0.8w First Bounded Container Parser Selection Review
- 0.0.8x Pure Managed Bounded FLV v1 Demux Parser
- 0.0.8y Parser-to-Decoder Composition Architecture Review
- 0.0.8z FLV Session Decoder-Parameter Binding

0.0.8z maps one exact live FLV session into independently owned padded H.264 and
AAC codec-parameter leases. It performs no native call and consumes no packet
completion.

## 0.0.9 Source-bound Native Decoder and Packet Ownership — In Progress

### 0.0.9a — Timestamp-aware Packet Submission Architecture Review

The review found that source-bound opened contexts must exist before packet
submission. The current payload-only boundary cannot preserve parser timing or
EOS state, and the current major-version gate does not prove an exact AVPacket
layout.

### Next: 0.0.9b Source-bound Opened Native Decoder Session

Implement one finite composition that:

- consumes one live 0.0.8z parameter binding;
- consumes distinct video and audio ABI gate leases with distinct runtime owners;
- snapshots exact source session, stream IDs, and time bases;
- creates, configures, and opens the H.264 context first;
- creates, configures, and opens the AAC context second;
- returns no partial success state;
- owns the two opened contexts and disposes audio before video;
- retains source identity without owning the parser or demux session;
- adds no packet submission or ABI change.

### Later sequence

1. 0.0.9c review the exact AVPacket metadata and null-flush ABI;
2. implement one metadata-aware native packet／flush boundary after approval;
3. implement a finite one-event submission state machine that retains the active parser completion during frame-drain pressure;
4. validate parser and composition with a retained controlled FLV fixture;
5. review PCP chunk-to-parser composition and discontinuity ownership;
6. add bounded decode iteration and per-stream EOS drain;
7. explicitly review production activation and Windows lifecycle;
8. perform controlled real-media validation.

### Deferred

- AVPacket metadata field writes
- timestamp-aware packet submission
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- F4V, MPEG-2 TS, MKV, and OGG parser support
- avformat integration
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete synchronization
- controlled audible／visible playback

## CI policy

Routine GitHub Actions use Ubuntu only. Superseded runs on the same PR are
cancelled. Windows CI is reserved for explicit milestone or release validation.

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
