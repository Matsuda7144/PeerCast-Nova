# PCN-0011 Live Interoperability Report

## Scope

This is a sanitized record of the first successful live Compatibility Probe.
It intentionally omits the channel ID, channel metadata, remote source address,
and session identifier.

## Environment

- Date: 2026-07-14 (Asia/Tokyo)
- Operating system: Windows 10.0.26200, x64
- .NET SDK: 10.0.301
- Transport: IPv4 loopback (`127.0.0.1:7144`)
- Peer: PeerCastStation 6.0.0.0
- Authentication: no credentials were requested by the local relay endpoint
- Channel: active receiving channel; identifier redacted

## Result

```text
Outcome: Accepted
HTTP: 200
Server: PeerCastStation/6.0.0.0
Content-Type: application/x-peercast-pcp
Received Atom: oleh
Received Atom: ok
Remote Agent: PeerCastStation/6.0.0.0
Remote Version: 1218
Observed Address: 127.0.0.1
Observed Port: 0
Terminal Atom: ok
Terminal Code: 1
```

The client sent the relay HTTP request and HELO, parsed OLEH, and received the
terminal OK result. A second run after adding content-free tracing confirmed
the ordered top-level Atom names `oleh`, then `ok`. No Atom payloads or channel
metadata were logged by the trace.

## Milestone decision

The first live probe passed. The later two-build verification in
`PCN-0012-TWO-BUILD-INTEROPERABILITY-REPORT.md` completed ADR-0022, so
Milestone 0.0.2c is now **Verified**.
