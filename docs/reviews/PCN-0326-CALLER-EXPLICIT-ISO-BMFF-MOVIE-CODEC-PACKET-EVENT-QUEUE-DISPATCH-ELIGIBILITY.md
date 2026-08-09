# PCN-0326: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Eligibility

## Decision

Accept caller-explicit `ReadyOnly` or `ReadyOrExpired` eligibility metadata
over exact ADR-0315 observations. Pending remains ineligible and ready remains
eligible. No candidate selection, dispatch, cursor mutation, scheduling, native
operation, rendering, or physical execution occurs.
