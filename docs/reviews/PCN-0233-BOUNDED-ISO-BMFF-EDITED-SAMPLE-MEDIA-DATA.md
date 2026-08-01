# PCN-0233: Bounded ISO BMFF Edited-sample Media-data Association

## Scope

Review bounded top-level `mdat` association for an admitted sample byte range.

## Findings

- Configuration is validated before file or range access.
- The typed range preserves its original sample number and edit admission.
- Existing exact file bounds, box traversal, containment, and count limits are
  retained.
- Successful output carries only metadata and a box location.
- No payload byte is sliced or decoded and no native operation occurs.

## Decision

Accept bounded edited-sample `mdat` association. Payload ownership and production
packet composition remain incomplete.
