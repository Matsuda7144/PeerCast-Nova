# ADR-0012: .NET 10 LTS

- Status: Accepted
- Decision: Use .NET 10 LTS for the initial implementation.
- Reason: Cross-platform Core support, modern networking APIs, and a long support window.
- Constraint: UI-specific projects must not leak platform dependencies into Core.
