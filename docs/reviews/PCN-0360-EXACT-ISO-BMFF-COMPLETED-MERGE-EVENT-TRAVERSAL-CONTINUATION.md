# PCN-0360: Exact ISO BMFF Completed Merge-Event Traversal Continuation

## Decision

Accept finite continuation from the exact prior final cursor while retaining
cumulative indexes and omitting all prior acknowledgement re-emission. Reject
inconsistent sources or bounds without delivery, native operation, rendering,
or physical execution.
