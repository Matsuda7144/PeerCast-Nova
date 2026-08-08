# PCN-0250: Bounded ISO BMFF Implicit SBR Sync Extension

## Scope

Review immediate optional SBR sync-extension parsing after bounded direct
GASpecificConfig.

## Findings

- Missing input, explicit signalling, and unsupported ER core paths are
  distinct.
- Short or mismatched marker input reports absence without speculative scans.
- Only primary marker `0x2B7` and extension Audio Object Type `5` are accepted.
- SBR false omits frequency fields; SBR true validates indexed or explicit
  frequency exactly.
- Conditional truncation never emits partial output.
- No decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept the bounded immediate implicit-SBR boundary. ER post-configuration and
arbitrary-position sync-extension handling remain incomplete.
