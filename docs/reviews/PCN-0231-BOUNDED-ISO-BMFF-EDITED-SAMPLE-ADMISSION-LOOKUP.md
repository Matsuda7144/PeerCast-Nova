# PCN-0231: Bounded ISO BMFF Edited-sample Admission Lookup

## Scope

Review the managed composition from one reconciled sample number to complete
edited movie appearances.

## Findings

- Invalid configuration is rejected before metadata access.
- Upstream reconciliation and sample-range failures remain distinguishable.
- Edit absence, unsupported policy, bounded allocation, rational projection,
  and partial-only rejection preserve their fail-closed outcomes.
- Repeated complete appearances remain available to callers.
- The output contains metadata only and does not imply packet selection.
- No native or physical operation occurs.

## Decision

Accept bounded edited-sample admission lookup. Sample byte-range association and
production demux composition remain incomplete.
