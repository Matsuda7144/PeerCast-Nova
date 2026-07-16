# PCN-0012 Two-Build Interoperability Report

## Result

The 0.0.2c live interoperability gate passed against both a confirmed official
Stable release and the current official development branch.

No channel ID, session ID, channel metadata, public address, or Atom payload is
included in this report.

## Controlled environment

- Date: 2026-07-14 (Asia/Tokyo)
- Operating system: Windows 10.0.26200, x64
- Nova SDK: .NET 10.0.301
- Transport: IPv4 loopback only
- Channel: one controlled relay channel; identifier redacted
- Authentication: disabled on the isolated loopback listeners
- Yellow Pages: disabled in both isolated peers
- External accepts: disabled in both isolated peers

The upstream test channel was relayed over loopback from an already-running
local PeerCastStation. Each verification peer used its own settings file and
port. Both temporary peers were stopped after the test; the user's existing
port 7144 instance remained running.

## Confirmed Stable

- Official release: PeerCastStation 4.1.0 (Stable)
- Official tag: `v4.1.0`
- Published: 2022-10-06
- Asset: `PeerCastStation-4.1.0-any.zip`
- Asset SHA-256:
  `26E2A1E7E252B10C6E665AEB8C8B8C12D61DAC5A1246ECA04EA8128B24C18075`
- Isolated listener: `127.0.0.1:27144`

```text
Outcome: Accepted
HTTP: 200
Server: PeerCastStation/4.1.0.0
Content-Type: application/x-peercast-pcp
Received Atom: oleh
Received Atom: ok
Remote Agent: PeerCastStation/4.1.0.0
Remote Version: 1218
Observed Address: 127.0.0.1
Observed Port: 0
Terminal Atom: ok
Terminal Code: 1
```

## Current development

- Official repository: `kumaryu/peercaststation`
- Branch: `master`
- Commit observed at verification:
  `76ba223a661f54d64729295f89a1eb201de92d75`
- Assembly version: 7.1.1.0
- Source archive SHA-256:
  `3542DF4B64A26D3F4433BEA276695956777CBE8A284C26D0F7F7CF938730E578`
- Build: official source targeting .NET 9, framework-dependent
- Execution: installed .NET 10 runtime with major roll-forward
- Isolated listener: `127.0.0.1:17144`

```text
Outcome: Accepted
HTTP: 200
Server: PeerCastStation/7.1.1.0
Content-Type: application/x-peercast-pcp
Received Atom: oleh
Received Atom: ok
Remote Agent: PeerCastStation/7.1.1.0
Remote Version: 1218
Observed Address: 127.0.0.1
Observed Port: 0
Terminal Atom: ok
Terminal Code: 1
```

The official source tree was used only to build and isolate the independent
test peer. It remained outside the Nova source tree and no PeerCastStation
implementation code was copied into Nova.

## Decision

Both release-gate peers accepted Nova's independently implemented relay entry
and HELO/OLEH handshake with identical top-level Atom order. ADR-0022 is
satisfied and Milestone 0.0.2c is **Verified**.

The next milestone is 0.0.3 Channel.
