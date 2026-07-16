# ADR-0033 Bounded Privacy-Safe Live Relay Diagnostic

## Status

Accepted for Milestone 0.0.4f.

## Context

The relay coordinator has deterministic and official-downstream evidence, but
the final 0.0.4 gate requires a controlled real media channel from an official
upstream through Nova to a downstream peer. The coordinator previously had no
repository-owned executable entry point, so that test depended on an
intermediate development harness and could not be handed to a user as a
repeatable procedure.

A diagnostic entry point must not turn Nova into an unbounded or externally
exposed service. It must not enumerate PeerCastStation channels, print a
channel ID or media content, infer packet/bootstrap semantics, or run forever.

## Decision

The CLI adds `--pcp-relay-diagnostics`. It owns one
`PcpRelayCoordinator` for a finite duration and binds its downstream listener
to IPv4 loopback only. User input selects the upstream host/port, channel ID,
initial position, loopback listen port, duration, minimum packet count, idle
timeout, and finite reconnect count.

The command validates every value before constructing the coordinator.
Duration is 1 second through 10 minutes, reconnects are 0 through 10, children
are fixed at 4, and each child queue is fixed at 16 packets / 4 MiB. Existing
finite handshake, write, child-stop, drain, TCP connection, packet, and atom
limits remain in force. Ctrl+C becomes caller cancellation.

The acceptance gate requires:

- terminal reason Stopped or UpstreamEnded;
- at least the requested ingress and delivered packet counts;
- positive ingress and delivered payload-byte counts;
- at least one accepted downstream handshake;
- zero discarded packet, cleanup-incomplete, and internal-failure counts;
- completed shutdown.

Output contains only the loopback listen port, configured duration, fixed
stop/end/drain enums, numeric aggregate counters, shutdown boolean, and
Pass/Fail. It does not echo the channel ID or upstream host.

## Consequences

- A two-terminal, repository-owned real-media relay procedure now exists.
- The diagnostic is local-only and self-terminating by default.
- Automation receives exit code 0 only when the complete acceptance gate
  passes.
- The command does not prove real-media interoperability by itself; that
  status changes only after a user-selected official-upstream run succeeds.
- No new protocol or packet/bootstrap assumption is introduced.
