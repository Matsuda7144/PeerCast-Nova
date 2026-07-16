# ADR-0047 Decoder Backend Artifact Manifest

## Status

Accepted and verified in 0.0.5k.

## Decision

Third-party, bundled, and native decoder backends require an immutable artifact
manifest before registration. The manifest records bounded upstream identity,
version, license identifier, and a normalized SHA-256 digest. Tokens exclude
paths, URLs, whitespace, and free-form text.

First-party managed backends may omit the artifact manifest. Policy review and
activation audit remain separate fail-closed checks.
