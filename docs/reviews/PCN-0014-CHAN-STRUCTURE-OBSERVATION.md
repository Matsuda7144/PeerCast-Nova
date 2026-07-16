# PCN-0014 CHAN Structure Observation

## Scope

A single controlled IPv4 loopback observation was performed against the
locally operated PeerCastStation 6.0.0.0 instance after an accepted relay
handshake.

The observer was bounded to:

- four top-level post-handshake Atoms;
- 256 KiB per complete Atom tree;
- three seconds;
- names, parent/leaf structure, lengths, and strict NUL-UTF-8 candidacy only.

Channel ID, remote session ID, IP address, metadata values, and packet payload
were not printed or persisted. Exact traffic bytes were not saved, so this is a
content-free observation report rather than a captured binary fixture.

## Observed structure

The first top-level message had this shape:

```text
chan
  id      16-byte leaf
  pkt
    type  4-byte leaf
    pos   4-byte leaf
    data  variable-length opaque leaf
  info
    type  NUL-terminated UTF-8 candidate
    styp  NUL-terminated UTF-8 candidate
    name  NUL-terminated UTF-8 candidate
    gnre  NUL-terminated UTF-8 candidate
    desc  NUL-terminated UTF-8 candidate
    cmnt  NUL-terminated UTF-8 candidate
    url   NUL-terminated UTF-8 candidate
    sext  NUL-terminated UTF-8 candidate
    bitr  4-byte leaf
  trck
    titl  NUL-terminated UTF-8 candidate
    gnre  NUL-terminated UTF-8 candidate
    albm  NUL-terminated UTF-8 candidate
    crea  NUL-terminated UTF-8 candidate
    url   NUL-terminated UTF-8 candidate
```

The next observed messages contained `chan` with `id` and `pkt`, consistent
with continuing packet delivery. Field order is recorded as observation only;
the decoder does not require that order.

## Independent implementation rule

No PeerCastStation CHAN parser or stream implementation source was used for
the Nova decoder. The implementation is based on this wire-shape observation,
Nova's independent framing codec, and synthetic Nova-owned test values.

## Decoder decisions

- `id` is decoded with the existing independent 16-byte PCP GUID codec.
- `type`, `pos`, and `bitr` are decoded as observed four-byte legacy integers.
- known text fields have a 4 KiB default limit and require strict UTF-8 with
  exactly one trailing NUL and no embedded NUL;
- unknown fields are ignored without copying their payload;
- duplicate known fields are rejected as ambiguous;
- `pkt/data` contributes only its length to the control model;
- packet bytes are never returned by the control sink.

## Remaining uncertainty

- numeric packet type meanings are not yet assigned semantic enum names;
- 32-bit position wrap/backward behavior now has a deterministic Network
  policy, but reconnect behavior still needs live-session validation;
- Stable 4.1.0 and current development builds have handshake evidence, but
  this CHAN shape observation has not yet been repeated on both builds;
- Network-to-Core control mapping is implemented; the bounded media data plane
  remains pending.
