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

0.0.8r transfers copied video then audio into the existing scheduler with fixed
sequences 0 and 1. Partial acceptance preserves the native lease required by
accepted frames. Atomic two-stream enqueue is not claimed.

### Next

Review a finite diagnostic dispatch from scheduler ownership into the existing
timing and output boundaries.

### Deferred

- bounded FLV／MP4 demux
- production decoder ingress
- continuous decode、flush、EOS
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
