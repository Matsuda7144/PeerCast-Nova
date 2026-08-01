# PCN-0230: Complete-sample ISO BMFF Edit Admission

## Scope

Review conservative admission of sample appearances after exact edit overlap
projection.

## Findings

- Missing or malformed movie-overlap input fails closed.
- Partial-only samples are explicitly rejected.
- Complete appearances are retained without rounding rational movie bounds.
- Repeated complete appearances remain distinct.
- The policy does not imply codec-safe trimming of partial compressed samples.
- No native or physical operation occurs.

## Decision

Accept complete-sample-only edit admission. Packet selection and production
demux composition remain incomplete.
