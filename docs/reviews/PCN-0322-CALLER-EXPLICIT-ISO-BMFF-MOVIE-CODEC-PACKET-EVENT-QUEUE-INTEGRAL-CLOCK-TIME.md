# PCN-0322: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Integral Clock Time

## Decision

Accept bounded integral clock intervals only under caller-explicit start and
exclusive-end `Floor`, `Ceiling`, or `RequireExact` modes. Reject fractional
exact-only values and empty rounded intervals. No default policy, clock origin,
deadline, scheduling, native operation, rendering, or physical execution is
introduced.
