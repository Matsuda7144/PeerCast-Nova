# PCN-0260: Bounded ISO BMFF GA Specific Config Prefix

## Scope

Review the exact General Audio prefix shared by direct and explicit-SBR/PS core
configurations.

## Findings

- Missing direct and explicit-extension inputs remain distinct.
- Unsupported object types fail before conditional bit access.
- Core-coder delay is read only when its flag is set.
- The consumed position identifies the exact Program Config Element start.
- No PCE body, decoder, native operation, or physical execution is included.

## Decision

Accept the bounded GA prefix projection. Program Config Element body and later
GA semantics remain incomplete.
