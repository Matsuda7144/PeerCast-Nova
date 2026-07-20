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

0.0.8w selects FLV v1 as the first parser behind the 0.0.8v contract. FLV maps
one H.264 video and one AAC audio stream to sequential callback-driven input,
exposes DTS plus AVC composition offset for PTS, and avoids the larger box,
track, sample-table, and fragment state required by MP4／fragmented MP4.

### Next

- 0.0.8x Pure Managed Bounded FLV v1 Demux Parser

Implement caller-pushed finite chunks, consumed-byte progress, one bounded
incomplete-record carry buffer, strict FLV header/tag/previous-size validation,
AVC and AAC sequence-header bootstrap, bounded SPS and AudioSpecificConfig
parsing, AVCC packet validation, separate PTS／DTS, keyframe state, discontinuity
reset, and explicit EOS behind the existing 0.0.8v ownership contract.

Do not connect PCP ingress, decoder codec parameters, packet submission,
production activation, or Windows output in the same milestone.

### Later sequence

1. validate the bounded FLV parser with controlled synthetic and retained test fixtures;
2. review PCP chunk-to-parser composition and discontinuity ownership;
3. review parser-to-codec-parameter and packet-submission composition;
4. add bounded decode iteration, drain, and EOS handling;
5. explicitly review production activation and Windows lifecycle;
6. perform controlled real-media validation.

### Deferred

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
