# ADR-0478: Exact ISO BMFF Regular Movie Verified Runtime Capability Binding

## Status

Accepted as the verified-runtime handoff boundary following ADR-0477.

## Decision

Bind an exact selected regular-movie managed decoder preparation to
caller-supplied `PlaybackDecoderFfmpegAbiSymbolGateLease` instances, one for
each selected AVC or AAC role. Require the presence or absence of each gate to
match the explicit movie selection. Require the exact managed parameter lease,
codec ID, transfer-plan symbol coverage, and required-symbol set to remain
available and source-consistent.

Inspect only the already acquired gate lease's immutable symbol-address map and
require every role-specific transfer symbol to have a nonzero address. Do not
locate files, load libraries, resolve new symbols, invoke addresses, allocate a
decoder context, or transfer parameters. Borrow the caller-owned gate leases;
own and dispose the source managed AVC/AAC preparation leases through the
aggregate binding. Expose dynamic availability so disposed gates, runtime
leases, or parameter leases cannot be treated as usable.

## Consequences

The regular MP4 production chain now reaches a reference-exact handoff from
selected owned packets and managed codec parameters to an already verified ABI
gate. Video and audio retain distinct gate identities and exact codec-specific
transfer requirements. The binding itself performs no native operation.

## Non-goals

Runtime discovery, inventory verification, DLL loading, dependency inspection,
symbol lookup, version invocation, native parameter transfer, decoder context
allocation or open, packet submission, scheduling, delivery, rendering,
application integration, fragmented MP4, and physical execution are not
included.
