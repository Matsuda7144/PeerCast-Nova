# PCN-0319: Bounded ISO BMFF Movie Codec Packet Event Queue Cursor

## Decision

Accept immutable bounded cursor creation and pure one-entry advance over the
ADR-0308 snapshot. Advance returns a new cursor without mutating its source and
reports exhaustion explicitly. No delivery acknowledgement, clock,
scheduling, native operation, rendering, or physical execution occurs.
