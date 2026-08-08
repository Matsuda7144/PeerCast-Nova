# PCN-0257: Explicit ISO BMFF AVC Payload Selection

## Scope

Review explicit selection across the four bounded AVC payload representations.

## Findings

- Missing classification and unsupported format values are distinct.
- Original length-prefixed selection needs no extra artifact.
- Each converted format requires its exact materialized payload.
- Foreign-classification artifacts fail closed by reference identity.
- Parameter-set inclusion and Annex-B representation are projected explicitly.
- Selection performs no payload copy, decoder operation, native call, demux
  event, or physical execution.

## Decision

Accept explicit AVC payload selection. Automatic decoder-state and injection
policy remain incomplete.
