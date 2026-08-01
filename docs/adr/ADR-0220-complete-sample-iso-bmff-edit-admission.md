# ADR-0220: Complete-sample ISO BMFF Edit Admission

## Status

Accepted as a post-0.0.10e non-physical policy slice.

## Context

ADR-0219 exposes full and partial sample appearances on the movie timeline.
Admitting partial compressed samples would require codec- and packet-specific
policy that is not yet defined.

## Decision

Add a conservative pure managed admission policy that:

- requires a projected movie-overlap set;
- validates nonzero denominators and non-empty rational intervals;
- retains every appearance marked as containing the complete sample;
- preserves repeated complete appearances; and
- explicitly rejects a sample when only partial appearances exist.

Fractional movie boundaries remain exact and do not prevent admission when the
complete sample is contained.

## Consequences

Later packet selection can require whole-sample edit containment without
silently treating partial overlap as decodable media.

## Non-goals

No partial compressed-sample trimming, codec policy, packet selection, payload
read, decoder composition, recording, or physical execution is included.
