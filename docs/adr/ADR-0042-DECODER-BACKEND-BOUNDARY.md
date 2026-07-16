# ADR-0042 Decoder Backend Boundary

## Status

Accepted and verified in 0.0.5f.

## Decision

Decoder backends are host-constructed implementations of
`IPlaybackDecoderBackend`. Nova does not scan directories, load assemblies, or
resolve native libraries in the playback core. Each backend exposes an immutable
descriptor containing a bounded fixed identifier, priority, and supported
containers. Selection is deterministic by descending priority and ordinal ID.

Backend creation is delayed until media classification succeeds. Unsupported
containers return no decoder. A selected backend's initialization failure is
mapped to a fixed `PlaybackDecoderBackendException` category and does not fall
through to another backend. Backend-specific exception text is not retained.

## Consequences

- capability inspection cannot initialize native code;
- deployment and license policy remain owned by the host/package boundary;
- selection is deterministic and configuration failures remain visible;
- existing decoder lifecycle, finite stop, reset, output ownership, and sink
  backpressure contracts remain unchanged;
- a real codec backend is explicitly outside 0.0.5f.
