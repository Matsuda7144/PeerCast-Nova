# PCN-0261: Bounded ISO BMFF Program Config Element Header

## Scope

Review fixed-field projection at the exact PCE start exposed by ADR-0250.

## Findings

- Missing prefix and channel configuration not requiring a PCE are distinct.
- Every fixed field is read with exact bit width and truncation fails closed.
- Reserved sampling-frequency indices are rejected.
- Field-width maxima inherently bound all projected counts.
- No variable PCE body, decoder, native operation, or physical execution occurs.

## Decision

Accept the bounded PCE fixed header. Mixdown, element-list, alignment, comment,
channel-layout, and later GA semantics remain incomplete.
