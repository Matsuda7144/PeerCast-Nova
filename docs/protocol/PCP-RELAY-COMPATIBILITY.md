# PCP Relay Compatibility

## Status

Compatibility implementation and controlled loopback tests are complete.

**Milestone 0.0.2c is Verified. Live probes succeeded against official Stable
4.1.0 and official master development 7.1.1.0 with the same ordered top-level
Atom trace: `oleh`, then `ok`.**

## Independently implemented sequence

The following behavior was derived from observable protocol behavior and public
PeerCastStation tests, not by copying implementation code.

1. Open TCP connection.
2. Send an HTTP relay request:
   - `GET /channel/{32-hex-channel-id} HTTP/1.0`
   - `Host`
   - `User-Agent`
   - `x-peercast-pcp`
   - `x-peercast-pos`
3. Parse the HTTP response without consuming PCP bytes that may follow it.
4. Accept status 200 or 503 as PCP-capable relay responses.
5. Send a parent `helo` atom containing:
   - `agnt`: NUL-terminated agent string
   - `sid`: 16-byte RFC/network-order session ID
   - optional `port` or `ping`
   - `ver`: little-endian 32-bit servant version
6. Ignore unrelated atoms until `oleh` or `quit`.
7. Parse `oleh`, requiring a remote session ID.
8. Continue until:
   - `ok` means relay accepted;
   - `quit` means rejected or relay-full;
   - `host` and other atoms may appear before `quit`.
9. Close the probe before consuming media.

## Important wire corrections

PCP atom names are NUL-padded four-byte fields, not always four printable
characters.

Examples:

```text
sid  -> 73 69 64 00
ver  -> 76 65 72 00
ok   -> 6F 6B 00 00
```

This corrected an incompatible assumption in the first Nova framing draft.

## HTTP response handling

A 503 response does not end protocol negotiation by itself. A legacy peer may
still exchange HELO/OLEH, advertise alternative HOST atoms, and then send QUIT.

The compatibility probe therefore treats both 200 and 503 as entry points to
the PCP control exchange.

## Live verification command

Start PeerCastStation with a controlled test channel, then run:

```powershell
tools/Test-PeerCastStationCompatibility.ps1 `
  -ChannelId 0123456789ABCDEF0123456789ABCDEF `
  -TraceAtoms
```

Expected successful output includes:

```text
Outcome: Accepted
HTTP: 200
Received Atom: oleh
Received Atom: ok
Remote Session: ...
Remote Version: 1218
Terminal Atom: ok
Terminal Code: 1
```

A full relay node can legitimately produce:

```text
Outcome: RelayFull
HTTP: 503
Terminal Atom: quit
```

## Evidence to record

When live verification is performed, record:

- PeerCastStation version;
- operating system;
- IPv4 or IPv6;
- channel ID replaced with a synthetic identifier in published logs;
- HTTP status and selected response headers;
- ordered atom names only;
- OLEH version and agent;
- terminal OK or QUIT code;
- whether authentication was enabled.

Do not publish public IP addresses or user-generated channel metadata.
