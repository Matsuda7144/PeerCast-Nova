# ADR-0371: Caller-Bounded ISO BMFF Track Sample Sequence Plan

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Project one reconciled nonempty sample table into a contiguous immutable sample
number plan beginning at one caller-supplied in-range sample number. Materialize
at most the caller maximum, bounded by the existing track packet-sequence limit,
and retain the exact reconciliation, remaining count before truncation,
zero-based sequence indexes, and explicit truncation state.

The plan owns sample numbers only. It does not inspect file bytes or construct
payloads or codec packets.

## Non-goals

Edit admission, byte-range or `mdat` lookup, payload copy, AVC/AAC composition,
packet construction, runtime access, native execution, rendering, or physical
execution are not included.
