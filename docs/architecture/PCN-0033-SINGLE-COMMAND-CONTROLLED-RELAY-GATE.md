# PCN-0033 Single-Command Controlled Relay Gate

> 0.0.4h replaces the TCP-table readiness observation below with the explicit
> coordinator contract in PCN-0035. The terminal two-process gate is retained.

## Composition boundary

```text
official upstream selected by the operator
  -> foreground PeerCast.Nova.Cli relay diagnostic
       -> new IPv4-loopback listener
  -> owned background PeerCast.Nova.Cli Channel Session Probe
  -> two process exit codes plus two fixed acceptance markers
  -> End-to-End Gate
```

The PowerShell script composes two existing public diagnostic modes. Protocol
parsing, Core state, packet ownership, backpressure, fan-out, downstream wire
format, and cleanup remain owned by their existing assemblies. The script
does not load a payload or infer header/bootstrap meaning.

## Readiness and race boundary

The requested listen port is inspected before the job starts. Any existing
listener causes a fixed failure, preventing the downstream probe from
attaching to an unrelated local service. The owned job then waits only for a
new listener on IPv4 loopback and connects once it appears. It never scans
other ports or asks PeerCastStation for a channel list.

The relay process is foreground-owned by the script invocation. The
downstream PowerShell job and the CLI process inside it are background-owned
by that same invocation. The `finally` path stops and removes only this job.
It does not stop, restart, or reconfigure the upstream process.

## Finite lifecycle

- relay window: 1 second through 10 minutes;
- listener-start wait: 1 through 60 seconds, 10 seconds by default;
- downstream session timeout: 1 second through 10 minutes;
- downstream idle timeout: 1 second through 10 minutes;
- reconnects: 0 through 10 for upstream and fixed zero downstream;
- post-relay job completion grace: fixed 10 seconds;
- all coordinator, fan-out, child, connection, packet, atom, write, drain, and
  cleanup limits remain those of the existing 0.0.4f CLI path.

## Evidence boundary

The final gate is conjunctive. It requires relay exit 0, relay acceptance,
downstream exit 0, and downstream packet-limit stop. It does not claim a media
format, successful playback, header interpretation, or bootstrap semantics.

The script prints the two existing privacy-safe summaries and a fixed final
Pass/Fail line. It does not itself print a Channel ID, upstream endpoint,
metadata, payload, stream position, session ID, exception detail, or
peer-controlled text.
