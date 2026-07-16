# PCP Compatibility Plan

## Goal

Interoperate with current PeerCast/PeerCastStation peers before introducing any
Nova-specific wire extension.

## Known framing baseline

The initial codec supports only independently verified framing primitives:

- atom name: four raw bytes;
- atom header: eight bytes;
- numeric fields: little-endian;
- high bit of the header value: parent/children flag;
- low 31 bits: content length or child count;
- protocol preamble: `pcp\n`, four-byte payload, protocol version 1.

This is not yet a complete handshake specification.

## HTTP entry

Compatibility testing must cover the legacy HTTP entry path used to request a
channel and switch into PCP communication. Exact request headers, response
status handling, and atom ordering will be captured as fixtures instead of
being inferred from memory.

## Completion criteria for 0.0.2

- exact byte fixtures for accepted and rejected preambles;
- incremental parsing across arbitrary TCP segment boundaries;
- malformed-input and resource-limit tests;
- timeout and cancellation tests;
- successful connection to a real PeerCastStation test node;
- packet log with sensitive network information removed;
- documented differences, if any, between observed versions.

## Nova extensions

No extension is allowed in the MVP unless legacy peers remain interoperable.
Future capabilities require an RFC and a negotiation mechanism.
