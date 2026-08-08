# PCN-0264: Bounded ISO BMFF PCE-Aware GA Specific Config Composition

## Scope

Review bounded GA continuation after either the common prefix or exact PCE.

## Findings

- PCE presence follows channel configuration exactly.
- A PCE from another prefix cannot be composed.
- Object-type-specific fields retain ADR-0236 conditions.
- Truncated continuation fails without a partial projection.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept PCE-aware GA composition. ER post-configuration sync-extension and
decoder composition remain incomplete.
