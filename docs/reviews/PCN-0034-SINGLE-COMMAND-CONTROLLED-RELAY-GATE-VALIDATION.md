# PCN-0034 Single-Command Controlled Relay Gate Validation

## Scope

This report records Milestone 0.0.4g validation of the one-command relay
orchestration. It covers listener readiness, complete executable delivery,
conjunctive process acceptance, failure output, privacy, and owned cleanup.
It does not claim the pending official-upstream real-media result.

## Environment

- Date: 2026-07-14
- Platform: Windows IPv4 loopback
- .NET SDK: 10.0.301
- Synthetic upstream: isolated port 29144
- Nova relay listener: isolated port 17145
- Failure-path ports: isolated 29143, 17146, and 17147
- Existing user PeerCastStation on port 7144: not queried, stopped, or changed

## Successful executable gate

The script started its downstream job, observed the new Nova loopback
listener, ran the relay foreground process, and connected the built downstream
CLI. The content-free result was:

```text
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

## Negative paths

With no upstream listener, Nova accepted the downstream handshake but received
and delivered zero packets. The relay acceptance failed, the downstream ended
remotely, the final gate failed, and the script returned exit code 1.

With the requested relay listen port already occupied, the script failed its
preflight before creating a downstream job or connection. The caller-owned
test listener remained untouched until the test itself stopped it.

Both negative results omitted the supplied Channel ID. All owned jobs and all
isolated validation listeners were absent after completion.

## Automated validation

- PowerShell parser: no syntax errors
- Release build: zero warnings, zero errors across 10 projects
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 72 passed
- CLI tests: 15 passed
- Total: 147 passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Decision

Milestone 0.0.4g single-command controlled relay gate is **Implemented** and
its isolated success, rejection, privacy, and cleanup paths are **Verified**.

Full 0.0.4 Relay remains in progress. The remaining gate is one invocation
against a user-selected controlled real media channel served by an official
PeerCastStation upstream. No channel enumeration or guessed protocol
semantics are authorized by this result.
