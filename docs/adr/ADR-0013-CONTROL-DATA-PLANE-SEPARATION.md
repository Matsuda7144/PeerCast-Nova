# ADR-0013: Separate Control and Data Planes

- Status: Accepted
- Decision: Core coordinates commands and status; media payload bytes bypass Core.
- Reason: Backpressure, ownership, latency, and throughput require explicit data-plane paths.
- Consequence: Relay, player, and recorder sinks will have separate bounded policies.
