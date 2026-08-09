# PCN-0375: Exact ISO BMFF Completed Merge-Event Traversal Dispatch Target Resolution

## Decision

Accept all-or-nothing source-ordered resolution of exact bounded requests to
caller-declared target capabilities by opaque ID, media kind, and payload limit,
retaining exact request and capability references without runtime lookup,
invocation, delivery, or physical execution.
