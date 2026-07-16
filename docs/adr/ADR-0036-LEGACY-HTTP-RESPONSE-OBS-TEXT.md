# ADR-0036 Legacy HTTP Response Field Octets

## Status

Accepted for Milestone 0.0.4 final verification.

## Context

A controlled real-media relay request to official PeerCastStation 6.0.0.0
returned HTTP 200 but Nova rejected the response before PCP HELO because two
unknown response field values contained bytes above ASCII 0x7e. A bounded,
content-free observation found 405 header bytes across ten lines: the status
line and every field name were ASCII, while only two field values contained
legacy high octets with a UTF-8-shaped distribution.

The values were channel information not needed by the relay handshake. The
previous shared strict-ASCII rule was appropriate for untrusted downstream
requests but unnecessarily rejected an observed official upstream response.
Blindly decoding arbitrary response text as UTF-8 or relaxing names, start
lines, and controls would weaken the boundary.

## Decision

Keep downstream HTTP requests strict ASCII. For upstream HTTP responses only:

- require the status line and every field name to remain ASCII;
- continue rejecting bare CR/LF, controls below 0x20 except HTAB, and DEL;
- allow bytes 0x80 through 0xff only after a field-name colon;
- decode the bounded header block with Latin-1 for a one-byte-to-one-character
  representation;
- retain the existing total header limit and discard unknown response fields
  after handshake processing;
- keep parser failures free of peer-provided line contents.

This is the legacy HTTP `obs-text` compatibility boundary. It does not infer a
channel metadata encoding and does not move those values into Core or relay
diagnostics.

## Consequences

- The observed official PeerCastStation response reaches HELO/OLEH/OK without
  weakening the downstream request parser.
- UTF-8-shaped channel fields are accepted as opaque bounded octets, not
  interpreted or published.
- High octets in a status line or field name and all disallowed controls still
  fail deterministically.
- Six tests cover accepted response values and rejected request values, status
  lines, names, and controls.
- The controlled real-media relay gate can evaluate the actual data path
  rather than failing at an unused response field.
