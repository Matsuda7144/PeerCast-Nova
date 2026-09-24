# PCP Compatibility Plan

## Goal

PeerCastStation compatibility is the first compatibility target. Preserve the existing HTTP/PCP relay behavior before considering any Nova-specific wire extension.

## Current status

The bounded HTTP-to-PCP relay handshake and downstream PCP relay path are implemented. The initial 0.0.2 compatibility work is complete for its recorded baseline; the exact probe scope and version evidence are in [PCP Relay Compatibility](PCP-RELAY-COMPATIBILITY.md).

This is not a claim of support for every PeerCastStation version, server configuration, channel, or media type. Keep compatibility claims tied to the exchanged messages, tested versions, and retained evidence.

## Compatibility practice

- Capture accepted and rejected wire behavior as byte fixtures; do not infer undocumented ordering from memory.
- Test malformed input, arbitrary TCP segmentation, resource limits, timeout, cancellation, and bounded shutdown.
- For live interop, record PeerCastStation version and the tested request/atom sequence. Remove real channel IDs, peer addresses, and other sensitive network details before publication.
- Recheck the supported baseline when PeerCastStation behavior changes.

## Nova extensions

No Nova-specific mandatory extension belongs in the MVP. Any future extension requires an RFC, capability negotiation, and continued legacy interoperability, consistent with [ADR-0014](../adr/ADR-0014-LEGACY-COMPATIBILITY-FIRST.md).
