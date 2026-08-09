# PCN-0320: Exact ISO BMFF Movie Codec Packet Event Queue Acknowledgement

## Decision

Accept an immutable acknowledgement binding the exact source cursor, queue
entry, and one-step successor cursor from ADR-0309. Fail closed on missing,
foreign, or skipped-transition inputs. This records managed consistency only;
no delivery outcome, dispatch, scheduling, native operation, rendering, or
physical execution occurs.
