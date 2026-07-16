# PCN-0009 Compatibility Implementation Review

## Result

The compatibility layer is ready for external live verification.

## Implemented

- NUL-padded 1–4 byte atom names
- Owned atom tree codec for control packets
- Typed PCP values:
  - little-endian 16-bit and 32-bit integers
  - RFC/network-order GUIDs
  - NUL-terminated UTF-8 strings
  - legacy reversed IPv4 values
- HTTP relay request builder
- bounded HTTP response parser sharing the same PipeReader as PCP
- HELO builder
- OLEH parser
- OK / QUIT terminal handling
- 200 and 503 response paths
- loopback peer integration tests
- CLI and PowerShell live probe

## Not claimed yet

- successful connection to PeerCastStation 6.0.1
- successful connection to development version 7.x
- IPv6 PCP version 100
- authenticated relay ports
- port-open ping callback
- HOST redirection parsing
- media CHAN packet compatibility

## Release gate

Milestone 0.0.2c becomes **Verified** only after the probe succeeds against at
least one stable PeerCastStation release and one current development build in a
controlled environment.
