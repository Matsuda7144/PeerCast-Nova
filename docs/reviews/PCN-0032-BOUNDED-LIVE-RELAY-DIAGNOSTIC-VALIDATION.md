# PCN-0032 Bounded Live Relay Diagnostic Validation

## Scope

This report records Milestone 0.0.4f validation of the repository-owned CLI
relay entry point. It covers strict arguments, finite lifecycle, executable
three-stage delivery, content-free acceptance, and process exit status. It
does not claim the still-pending official-upstream real-media result.

## Environment

- Date: 2026-07-14
- Platform: Windows IPv4 loopback
- .NET SDK: 10.0.301
- Synthetic upstream: isolated port 29144
- CLI relay listener: isolated port 28145
- Existing user PeerCastStation on port 7144: not queried, stopped, or changed

## Executable chain

The controlled chain used the built CLI assembly rather than calling the
coordinator directly:

```text
synthetic bounded upstream
  -> PeerCast.Nova.Cli --pcp-relay-diagnostics
  -> separate PeerCast.Nova.Cli --pcp-channel-session-probe
```

The downstream command connected after `Relay State: Ready`, stopped itself at
four packets, and returned exit code 0. The relay remained active until its
five-second window, requested owned shutdown, evaluated the fixed acceptance
gate, and also returned exit code 0.

## Sanitized result

```text
Relay Stop Trigger: WindowElapsed
Relay End: Stopped
Drain: NotRequired
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
Relay Exit: 0
Downstream Stop Trigger: PacketLimit
Downstream Packets: 4
Downstream Payload Bytes: 16
Downstream Exit: 0
```

No channel ID, upstream endpoint, metadata, payload content, stream position,
session ID, exception, or peer-controlled text is present in the product
result.

## Argument and acceptance tests

The 15 CLI tests cover:

- bounded loopback defaults;
- culture-independent explicit values including a 64-bit initial position;
- missing/invalid channel ID;
- invalid upstream/listen port;
- duration and packet-count lower bounds;
- reconnect upper bound;
- header-injection host text;
- unknown and duplicate arguments;
- occupied loopback listen-port failure without a false Ready marker;
- complete acceptance requirements;
- fixed enum/numeric/boolean result properties.

The existing Network integration suite continues to cover continuous wire
delivery, reconnect continuity, requested stop, caller cancellation, finite
drain, upstream failure, slow/failing child isolation, and cleanup deadlines.

## Automated validation

- Release build: zero warnings, zero errors across 10 projects
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 72 passed
- CLI tests: 15 passed
- Total: 147 passed
- CLI repetition gate: 10 consecutive runs / 150 executions passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Decision

Milestone 0.0.4f bounded live relay diagnostics is **Implemented** and its
isolated executable path is **Verified**.

Full 0.0.4 Relay remains in progress. The next gate is to run the documented
two-terminal procedure against a user-selected controlled real media channel
served by an official PeerCastStation upstream, without channel enumeration or
guessed protocol semantics.
