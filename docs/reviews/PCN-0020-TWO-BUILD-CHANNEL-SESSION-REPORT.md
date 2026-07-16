# PCN-0020 Two-Build Channel Session Report

## Scope

This report records finite live packet-session verification for Milestone
0.0.3f. Testing used official PeerCastStation binary builds only. No existing
PeerCastStation implementation source was copied or used to implement Nova.

The exact build provenance and artifact hashes were previously recorded in
`PCN-0012-TWO-BUILD-INTEROPERABILITY-REPORT.md`.

## Environment

- Date: 2026-07-14
- Platform: Windows loopback
- .NET SDK: 10.0.301
- Stable peer: official PeerCastStation 4.1.0, isolated local settings
- Development peer: official PeerCastStation 7.1.1, isolated local settings
- Discovery: disabled; no YP or external endpoint was used
- Source channel: controlled local PeerCastStation session
- Stop policy: four packets, finite deadlines, no reconnect

The stable and development processes used separate loopback ports and settings.
Only one temporary peer was exercised at a time. Ports, process IDs, channel
IDs, peer/session IDs, metadata, payload contents, positions, and external
addresses are intentionally omitted.

## Stable 4.1.0 result

```text
End: Stopped
Stop Trigger: PacketLimit
Attempts: 1
Reconnects: 0
HTTP: 200
Channel Messages: 4
Packets: 4
Payload Bytes: 39271
Final State: Stopped
```

## Development 7.1.1 result

```text
End: Stopped
Stop Trigger: PacketLimit
Attempts: 1
Reconnects: 0
HTTP: 200
Channel Messages: 4
Packets: 4
Payload Bytes: 38112
Final State: Stopped
```

## Privacy and cleanup

The probe printed only fixed lifecycle labels and aggregate counts. It did not
print or persist channel IDs, peer/session IDs, addresses, metadata, payload
contents, stream positions, or exception text. No terminal fault was published
because both sessions stopped cooperatively at the configured packet limit.

Both isolated PeerCastStation processes were stopped after verification and
their temporary listeners were closed. The user's pre-existing local
PeerCastStation process was not stopped or modified.

## Decision

Both official build lines accepted HTTP relay entry, delivered four live CHAN
packets through Nova's bounded one-packet-at-a-time consumer boundary, and
stopped within the finite policy. Together with ADR-0027, PCN-0019, the 95-test
suite, and both self-tests, this completes the remaining Milestone 0.0.3 gate.

Milestone 0.0.3 Channel is **Verified**. Work may proceed to 0.0.4 Relay.
