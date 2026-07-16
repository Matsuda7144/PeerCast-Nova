# PCN-0022 Relay Fan-Out Validation

## Scope

This report records deterministic validation of the in-process 0.0.4a relay
fan-out boundary. It does not claim downstream PCP wire interoperability.

## Environment

- Date: 2026-07-14
- Platform: Windows
- .NET SDK: 10.0.301
- Configuration: Release, strict analyzers, warnings as errors

## Verified behavior

- upstream callback memory is copied before the ingress callback returns;
- the payload is copied once and safely remains valid for child callbacks;
- one blocked child reaches its packet bound and is stopped without blocking a
  healthy child;
- a child-sink exception is isolated from the upstream and another child;
- a packet larger than the per-child byte limit is rejected before send;
- the finite child limit rejects excess registrations;
- an actually completed child releases its capacity slot;
- explicit owner removal leaves another child active;
- fan-out disposal cancels a blocked, cancellation-aware child and accounts for
  the undelivered packet;
- a cancellation-violating child returns control at the finite stop deadline,
  remains `Stopping`, and retains its bounded slot until real cleanup;
- explicit stream discontinuity reaches the child unchanged;
- the status surface has no string or exception property.

## Automated result

- Release build: zero warnings, zero errors
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 38 passed
- Network tests: 50 passed
- Total: 105 passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Decision

Milestone 0.0.4a bounded relay fan-out is **Implemented**. Its ownership,
resource limits, slow-child isolation, failure isolation, shutdown, and privacy
boundaries are deterministic and covered by tests.

Milestone 0.0.4 Relay is not yet Verified. Downstream HTTP/PCP negotiation,
bounded CHAN serialization, and controlled live one-hop interoperability remain
required.
