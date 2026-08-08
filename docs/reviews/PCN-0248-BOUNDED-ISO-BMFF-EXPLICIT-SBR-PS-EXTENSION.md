# PCN-0248: Bounded ISO BMFF Explicit SBR/PS Extension

## Scope

Review explicit SBR/PS extension projection from leading Audio Object Type `5`
or `29` in the owned AudioSpecificConfig.

## Findings

- Non-extension leading object types remain explicitly unsupported.
- Indexed, explicit, reserved, zero, and truncated extension frequencies are
  distinguished.
- Normal, extended, zero, and truncated core object types are covered.
- ER BSAC extension channel configuration is consumed only for core type `22`.
- The result retains its source header and exact ending bit position.
- No sync scan, decoder, native operation, demux event, or physical execution
  occurs.

## Decision

Accept bounded explicit SBR/PS extension parsing. Implicit sync extensions and
decoder composition remain incomplete.
