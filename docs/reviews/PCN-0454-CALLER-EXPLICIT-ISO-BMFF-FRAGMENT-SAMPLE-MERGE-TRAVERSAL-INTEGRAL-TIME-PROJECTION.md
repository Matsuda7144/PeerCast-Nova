# PCN-0454: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Integral Time Projection

## Decision

Accept arbitrary-precision conversion of exact decode and presentation
intervals to a caller tick rate with explicit floor, ceiling, or require-exact
rounding, including correct negative floor/ceiling behavior, without clock,
origin, deadline, scheduler, delivery, payload, runtime, or physical execution.
