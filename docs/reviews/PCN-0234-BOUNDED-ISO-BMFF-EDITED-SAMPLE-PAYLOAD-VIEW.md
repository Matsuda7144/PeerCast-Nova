# PCN-0234: Bounded ISO BMFF Edited-sample Payload View

## Scope

Review the non-owning sample slice derived from edited `mdat` metadata.

## Findings

- File length and `mdat` containment are revalidated.
- Recorded box location, size, type, and header width must match current bytes.
- The exact range is sliced only after all validation succeeds.
- Empty structural samples are supported without allocation.
- Tests demonstrate that the view retains source-memory aliasing.
- No decoder or native operation occurs.

## Decision

Accept the bounded non-owning payload view. Owned packet memory remains a
separate boundary.
