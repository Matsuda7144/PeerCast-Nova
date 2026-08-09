# ADR-0336: Exact ISO BMFF Selected Sequence Packet Head Snapshot

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0335 owns immutable cursors for the explicitly selected video and/or audio
sequences. Cross-track ordering first needs a non-destructive view of each
current entry without advancing either cursor or choosing a winner.

## Decision

Retain the exact ADR-0335 cursor set and project the exact entry at each
selected cursor's next sequence index. Preserve an absent selected role as an
absent head. Preserve an exhausted selected role as an absent head while
distinguishing whether both selected roles are exhausted.

Require each role cursor to retain the exact sequence selected for that role,
require its position to be within the inclusive zero-to-count range, and require
each non-exhausted entry index to equal the cursor position. Reject a missing or
internally empty cursor set and any invalid role cursor without returning a
partial snapshot.

## Consequences

Callers can inspect exact current video and/or audio sequence entries without
mutating source cursors or implying comparison, acknowledgement, or consumption.

## Non-goals

Packet-head time comparison, winner selection, cursor advance, acknowledgement,
cross-track merge, event projection, delivery, scheduling, automatic packet
construction, file or `mdat` reads, native packet submission, decoder execution,
rendering, or physical execution are included.
