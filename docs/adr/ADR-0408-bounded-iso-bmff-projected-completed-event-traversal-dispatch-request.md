# ADR-0408: Bounded ISO BMFF Projected Completed-Event Traversal Dispatch Request

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Project every exact ADR-0407 binding into immutable bounded request metadata
that retains the opaque target ID, media kind, exact owned codec packet payload,
deadline, and complete production-oriented source chain.

## Non-goals

Capability discovery, target resolution, registry access, request submission,
delivery, invocation, native execution, rendering, or physical execution are
not included.
