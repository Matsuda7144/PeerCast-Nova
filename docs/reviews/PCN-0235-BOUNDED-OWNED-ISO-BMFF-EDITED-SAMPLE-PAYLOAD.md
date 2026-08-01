# PCN-0235: Bounded Owned ISO BMFF Edited-sample Payload

## Scope

Review bounded promotion of a validated edited-sample view to owned memory.

## Findings

- Configuration is validated before metadata or source access.
- Empty and oversized payloads fail before allocation.
- The 4 MiB hard limit matches the existing bounded demux packet contract.
- Successful bytes no longer alias the input while retaining all metadata.
- No demux dispatch, decoder, or native operation occurs.

## Decision

Accept bounded owned edited-sample payload memory. Demux-event projection and
codec-specific composition remain incomplete.
