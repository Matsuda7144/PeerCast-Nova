# PCN-0038 Controlled Real-Media Relay Validation

## Scope

This report records the final Milestone 0.0.4 Relay gate using a
user-selected controlled real-media channel served by official
PeerCastStation. The Channel ID, metadata values, payload content, positions,
session identifiers, and command line are intentionally omitted.

## Environment

- Date: 2026-07-14
- Platform: Windows IPv4 loopback
- .NET SDK: 10.0.301
- Official upstream: PeerCastStation 6.0.0.0 on caller-owned port 7144
- Nova downstream listener: isolated IPv4 loopback port 17145
- Relay window: 60 seconds
- Downstream acceptance target: 4 packets
- PeerCastStation channel list, settings, and process lifecycle: not modified

## Compatibility finding

The first run stopped before HELO because the official HTTP 200 response had
two unknown field values containing bytes above ASCII 0x7e. A bounded
content-free observation recorded 405 header bytes and ten lines. The status
line and field names were ASCII; only two values contained high octets.

ADR-0036 permits those response-only bytes as opaque legacy `obs-text` while
retaining strict downstream requests, ASCII status/name validation, control
rejection, and the existing header limit. After the change, the compatibility
probe completed:

```text
Outcome: Accepted
HTTP: 200
Server: PeerCastStation/6.0.0.0
Content-Type: application/x-peercast-pcp
Received Atom: oleh
Received Atom: ok
Remote Version: 1218
Terminal Atom: ok
Terminal Code: 1
Process Exit: 0
```

## Final end-to-end gate

```text
Relay Stop Trigger: WindowElapsed
Relay End: Stopped
Drain: NotRequired
Ingress Packets: 1338
Ingress Payload Bytes: 17381199
Accepted Handshakes: 1
Delivered Packets: 4
Delivered Bytes: 51633
Discarded Packets: 0
Cleanup Incomplete: 0
Internal Failures: 0
Shutdown Completed: True
Acceptance Gate: Passed
Downstream End: Stopped
Downstream Stop Trigger: PacketLimit
Downstream Attempts: 1
Downstream Reconnects: 0
Downstream HTTP: 200
Downstream Packets: 4
Downstream Payload Bytes: 51633
End-to-End Gate: Passed
Process Exit: 0
```

No isolated listener or owned relay job remained after completion. The
caller-owned PeerCastStation listener remained active and was not stopped or
reconfigured.

## Automated validation

- Response `obs-text` boundary tests: 6 passed
- Network tests: 81 passed
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- CLI tests: 15 passed
- Total: 156 passed
- Network repetition gate: 10 runs / 810 executions passed
- Release build: zero warnings, zero errors
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Decision

Milestone 0.0.4 Relay is **Verified**.

The MVP relay prerequisite now has controlled official-upstream evidence for
HTTP/PCP negotiation, continuous packet ingress, bounded Nova fan-out,
downstream delivery, finite stop, and complete cleanup. Playback remains a
separate 0.0.5 milestone; this result does not claim audio/video decoding or
rendering.
