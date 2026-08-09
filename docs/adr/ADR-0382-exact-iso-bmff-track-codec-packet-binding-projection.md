# ADR-0382: Exact ISO BMFF Track Codec-Packet Binding Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Bind every exact ADR-0381 codec packet in source order to one caller-provided
exact `tkhd` identity and the exact `mdhd` timing and `hdlr` already retained by
the source chain. Apply the existing track packet binder to every entry and
return no partial projection on identity, timescale, handler, or source-chain
mismatch.

## Non-goals

Movie timing, multi-track selection, delivery, scheduling, runtime access,
native execution, rendering, or physical execution are not included.
