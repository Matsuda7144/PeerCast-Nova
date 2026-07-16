# ADR-0016: Dedicated PCP Module and No Catch-All Common Module

- Status: Accepted
- Decision: Add `Nova.Protocol.Pcp` as a pure codec project.
- Decision: Remove `Nova.Common` until a stable shared abstraction is proven.
- Reason: Protocol parsing must be deterministic and dependency-free; catch-all projects decay boundaries.
