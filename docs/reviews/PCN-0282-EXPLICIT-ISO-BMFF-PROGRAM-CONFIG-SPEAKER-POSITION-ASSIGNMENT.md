# PCN-0282: Explicit ISO BMFF Program Config Speaker-Position Assignment

## Scope

Review caller-stated speaker-position assignment over the bounded PCE channel-slot
map.

## Findings

- Every source slot requires exactly one explicit caller position.
- Undefined, duplicate, missing, and wrong-count inputs fail without assignment.
- LFE slots and the low-frequency position must agree in both directions.
- Bindings retain the exact slot map, slot objects, and caller order.
- No PCE region or pair component implicitly selects a speaker position.
- No platform mask, decoder layout, native operation, or physical execution is
  produced.

## Decision

Accept explicit PCE speaker-position assignment. Platform layout projection,
decoder binding, and native transfer remain incomplete.
