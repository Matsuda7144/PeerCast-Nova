# PCN-0355: Exact ISO BMFF Completed Merge-Step View

## Decision

Accept a global-index view over exact locally indexed initial/continuation merge
segments only when completion count and cursor-state chaining remain exact. No
packet copying, appearance expansion, delivery, native operation, rendering, or
physical execution occurs.
