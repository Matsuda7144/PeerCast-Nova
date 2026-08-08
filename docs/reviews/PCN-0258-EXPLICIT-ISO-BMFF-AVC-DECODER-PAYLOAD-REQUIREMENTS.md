# PCN-0258: Explicit ISO BMFF AVC Decoder-payload Requirements

## Scope

Review exact mapping from caller-stated decoder payload requirements to one
bounded AVC payload format.

## Findings

- Missing requirements are distinct.
- The two independent boolean requirements cover all four formats exactly.
- No default or sample-entry-based format is inferred.
- Resolution retains the caller requirements object.
- No payload access, allocation, decoder operation, native call, demux event, or
  physical execution occurs.

## Decision

Accept explicit AVC decoder-payload requirements. Runtime capability discovery
and automatic policy remain incomplete.
