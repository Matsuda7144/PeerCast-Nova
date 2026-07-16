# PCN-0036 Explicit Relay Readiness Validation

## Scope

This report records Milestone 0.0.4h validation of coordinator-owned listener
readiness and the readiness-stream end-to-end tool. The later official-upstream
real-media result is recorded separately in PCN-0038.

## Contract validation

Automated Network tests establish that:

- successful bind completes readiness as `Ready` before a downstream peer
  connects;
- an occupied listener completes readiness as `Failed`, produces the fixed
  coordinator `DownstreamFailed` result, and exposes no local endpoint;
- stopping an unstarted coordinator completes readiness as `Stopped` and the
  single-use coordinator cannot later run;
- canceling one readiness wait leaves the owner in `Created` and a later stop
  still completes the shared readiness contract.

The existing CLI occupied-port test also confirms that `Relay State: Ready`
is absent and the Channel ID remains absent from output.

## Executable success path

The built 0.0.4h CLI was executed through the updated one-command script on
isolated IPv4 loopback ports. The script consumed the CLI output stream rather
than inspecting the TCP table.

```text
Relay State: Ready
Relay End: Stopped
Ingress Packets: 46
Ingress Payload Bytes: 184
Accepted Handshakes: 1
Delivered Packets: 4
Delivered Bytes: 16
Discarded Packets: 0
Cleanup Incomplete: 0
Internal Failures: 0
Shutdown Completed: True
Acceptance Gate: Passed
Downstream Stop Trigger: PacketLimit
Downstream Packets: 4
Downstream Payload Bytes: 16
End-to-End Gate: Passed
Process Exit: 0
```

## Negative executable paths

For an already occupied relay port, the relay ended `DownstreamFailed`, never
emitted Ready, the downstream probe was not started, and the caller-owned
listener had no pending connection. The final gate returned exit code 1.

For an absent upstream, the downstream listener legitimately became Ready and
accepted one downstream handshake, but ingress and delivery stayed zero. Both
terminal acceptance and the end-to-end gate failed with exit code 1.

Both paths omitted the supplied Channel ID. All isolated listeners and owned
jobs were gone after completion. The existing user PeerCastStation on port
7144 was not queried, stopped, or changed.

## Automated validation

- Release build: zero warnings, zero errors across 10 projects
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 75 passed
- CLI tests: 15 passed
- Total: 150 passed
- Network repetition gate: 10 runs / 750 executions passed
- CLI repetition gate: 10 runs / 150 executions passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Decision

Milestone 0.0.4h explicit relay readiness is **Implemented** and its contract,
success, rejection, privacy, and cleanup paths are **Verified**.

The remaining end-to-end gate subsequently passed against a user-selected
controlled real media channel served by official PeerCastStation; see
PCN-0038. Full 0.0.4 Relay is now Verified.
