# PCN-0364: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Clock Time

## Decision

Accept overflow-safe exact rational clock-tick metadata at one nonzero
caller-explicit rate while retaining exact ADR-0353 sources. No rounding, clock
reading, delivery, native operation, rendering, or physical execution occurs.
