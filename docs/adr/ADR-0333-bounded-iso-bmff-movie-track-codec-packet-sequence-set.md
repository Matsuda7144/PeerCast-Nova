# ADR-0333: Bounded ISO BMFF Movie Track Codec Packet Sequence Set

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0330 owns one exact track sequence. Cross-track work first needs bounded
movie-level ownership and stable caller stream identity without selecting or
merging packet heads.

## Decision

Own between one and 256 caller-provided ADR-0330 sequences in exact caller
order. Require every sequence to retain the exact same movie-timing object and
require unique track IDs. Assign deterministic zero-based stream indexes while
retaining each exact sequence object.

Copy the caller list into a read-only collection. Fail closed for missing,
empty, excessive, or null input; foreign movie provenance; or duplicate track
identity.

## Consequences

Multiple bounded same-movie track sequences can be addressed by stable caller
stream index before an explicit selection or merge policy is chosen.

## Non-goals

No video/audio default selection, cursor creation, packet-head comparison,
cross-track merge, event projection, automatic packet construction, file or
`mdat` read, scheduling, native packet submission, decoder execution, rendering,
or physical execution is included.
