# PCN-0353: Exact ISO BMFF Merge Traversal Continuation

## Decision

Accept continuation from an exact truncated final cursor state under the same
tie policy and remaining global step bound. The new segment retains local
indexes and does not re-emit prior steps; cumulative count and chained source
identity remain explicit. No delivery, native operation, rendering, or physical
execution occurs.
