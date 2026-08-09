# PCN-0337: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Admitted Invocation Selection

## Decision

Accept caller-bounded source-ordered selection of exact admitted invocation
metadata with total-admitted count and explicit truncation. Denied entries are
not selected, and no runtime access, lease, invocation, dispatch, native
operation, rendering, or physical execution occurs.
