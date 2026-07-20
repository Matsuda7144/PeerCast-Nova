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

## 0.0.8 Recovery and Diagnostic Integration — In Progress

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

0.0.8y confirms that FLV session descriptions can enter the existing managed
codec-parameter ownership boundary without information loss. It also confirms
that the older paired packet transaction cannot consume real parser output
because it lacks event-order, timing, decoder-backpressure, and EOS semantics.

### Next

- 0.0.8z FLV Session Decoder-Parameter Binding

Implement a pure managed adapter that validates one live FLV session and creates
independent H.264 and AAC padded codec-parameter leases through the existing
factory. The binding retains the source session reference and immutable stream
IDs and time bases, but owns neither parser nor session.

Do not create native contexts, transfer parameters, open codecs, submit packets,
or change backend activation in the same milestone.

### Later sequence

1. review timestamp-aware one-event packet submission and native packet fields;
2. implement bounded packet-send, frame-drain retry, and EOS ownership;
3. validate parser and composition with a retained controlled FLV fixture;
4. review PCP chunk-to-parser composition and discontinuity ownership;
5. add bounded decode iteration and drain;
6. explicitly review production activation and Windows lifecycle;
7. perform controlled real-media validation.

### Deferred

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
