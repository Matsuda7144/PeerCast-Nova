# ADR-0048 External Backend Artifact Verification

## Status

Accepted and verified in 0.0.6a.

## Decision

An external decoder artifact must be streamed through a bounded SHA-256 verifier
before activation. Verification compares against the immutable 0.0.5k manifest,
uses fixed-time digest comparison, observes cancellation, leaves stream ownership
with the caller, and returns only fixed result categories.

The verifier accepts a stream rather than a path so paths never enter Playback
contracts or diagnostics. The default limit is 512 MiB and the absolute policy
ceiling is 4 GiB. Verification does not load or execute the artifact.
