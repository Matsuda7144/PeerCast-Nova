# ADR-0332: Exact ISO BMFF Track Codec Packet Sequence Acknowledgement

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0331 returns an exact source cursor, sequence entry, and successor cursor
for one pure advance. Later composition needs to retain that transition without
claiming that the packet was delivered or consumed.

## Decision

Create immutable acknowledgement metadata only when all three advance objects
are present, both cursors retain the exact same ADR-0330 sequence, the entry is
the exact object at the source cursor index, and the successor index is exactly
one greater. Expose the exact sequence index and sample number from the retained
entry.

Fail closed for missing objects, foreign sequences or entries, exhausted or
out-of-range source positions, and skipped cursor transitions.

## Consequences

One sequence transition can be retained with exact provenance independently of
any later delivery or dispatch outcome.

## Non-goals

Acknowledgement is not a delivery, consumption, commit, cursor mutation,
automatic packet construction, file or `mdat` read, cross-track merge, event
projection, scheduling, native packet submission, decoder execution, rendering,
or physical execution.
