# ADR-0225: Bounded Owned ISO BMFF Edited-sample Payload

## Status

Accepted as a post-0.0.10e non-physical ownership slice.

## Context

ADR-0224 exposes a validated but aliased span. Asynchronous packet lifetimes
require an owned copy with the same bound used by the existing demux contract.

## Decision

Add a pure managed copy boundary that:

- composes the validated non-owning payload view;
- rejects empty payloads for owned packet promotion;
- defaults to and cannot exceed the existing 4 MiB demux packet hard limit;
- checks the limit before allocation; and
- retains the complete edited-sample metadata beside private owned bytes.

## Consequences

The owned bytes remain stable after source mutation and preserve their sample,
edit, range, and `mdat` association.

## Non-goals

No demux event, codec configuration, timestamp rounding, decoder submission,
recording, or physical execution is included.
