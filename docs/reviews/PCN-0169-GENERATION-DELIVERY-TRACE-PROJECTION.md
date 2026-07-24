# PCN-0169 Generation Delivery Trace Projection

## Decision

Introduce one read-only projection boundary that extracts only the existing fixed four-slot generation delivery trace from a live production generation.

## Constraints

- Null or disposed generations project the absent default trace.
- The projection catches only the expected disposal race.
- No scheduler, timing, delivery, native output, endpoint, cleanup, or ownership behavior changes.
- No full generation snapshot is exposed.
- No outer delivery decorator is reintroduced.
- Production and lifecycle snapshot wiring remains a later, narrow change.
- Physical audio/video output remains blocked.

## Verification

Focused tests cover null, live, and disposed generation paths and confirm the borrowed source session is untouched.
