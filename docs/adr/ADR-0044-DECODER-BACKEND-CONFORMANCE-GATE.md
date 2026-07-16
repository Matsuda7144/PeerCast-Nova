# ADR-0044 Decoder Backend Conformance Gate

## Status

Accepted and verified in 0.0.5h.

## Decision

Backend selection and privacy invariants receive a dedicated repeatable gate
before any real codec integration. The gate covers lazy construction,
deterministic selection, unsupported media, duplicate identifiers, immutable
capabilities, sanitized initialization failures, and safe identifiers.

The gate runs only fake backends and does not load media, assemblies, native
libraries, or codec packages. It complements rather than replaces the existing
decoder lifecycle repetition gate.
