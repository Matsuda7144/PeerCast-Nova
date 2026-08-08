# PCN-0265: Bounded ISO BMFF ER Audio Post Config

## Scope

Review exact `epConfig` projection after a composed ER GA Specific Config.

## Findings

- ER and non-ER object types remain distinct.
- Values zero and one retain exact source and consumed position.
- Values two and three fail before any unsupported variable structure is skipped.
- Truncation produces no partial projection.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded ER post-config projection. Error Protection Specific Config and
subsequent sync-extension handling remain incomplete.
