# ADR-0112: Source-bound Opened Native Decoder Session

Status: Accepted

## Context

0.0.8z preserves exact FLV source-session identity and managed H.264/AAC parameters. 0.0.9a concluded that packet submission must wait until an opened native context pair exists for that same source.

## Decision

Add one finite factory that consumes a live FLV parameter binding plus distinct video and audio ABI gate leases.

The required order is:

1. snapshot source identity, stream IDs, and time bases;
2. create, configure, and open H.264;
3. confirm the source is still live;
4. create, configure, and open AAC;
5. confirm the source is still live;
6. return one session owning both opened contexts.

The binding and both gates are consumed on every result. Partial configured or opened state is never returned as success. The successful session keeps the source only as a non-owning identity reference and disposes audio before video.

## Consequences

The real FLV path now has a source-bound opened native decoder session boundary. Packet metadata, send/receive state, flush, drain, PCP composition, backend activation, and playback remain deferred.

## Validation

Injected-operation tests cover ordering, partial failure, source disposal, shared-gate rejection, total input consumption, and idempotent audio-before-video cleanup. CircleCI is the routine merge gate while GitHub Actions usage is exhausted.
