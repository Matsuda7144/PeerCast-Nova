# ADR-0288: Bounded ISO BMFF ER Configuration Admission

## Status

Accepted as a post-0.0.10e non-physical policy slice.

## Decision

Admit ER configuration only when its owned inputs match `epConfig`: values zero
and one require no Specific Config; value two requires an exact source-bound
Specific Config; value three additionally requires an admitted, source-bound
direct-mapping decision. Retain the exact resulting end and produce no partial
admission for missing or mismatched ownership.

## Non-goals

Decoder or native integration and physical execution are not included.
