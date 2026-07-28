# ADR-0164: Installer, Signing, and Update Boundary Review

## Status

Accepted for milestone 0.0.10e.

## Context

ADR-0163 defines one framework-dependent `win-x64` portable package for
`Nova.App`. A portable publish output does not itself justify an installer,
code-signing flow, update client, release upload, or external runtime payload.

0.0.10d remains deferred and Issue #129 remains open. Distribution mechanics
are not readiness or physical authorization.

## Decision

Keep the `WindowsPortable` output as the only implemented application
distribution boundary for 0.0.10e.

### Installer boundary

No installer project, bootstrapper, custom action, registration step, service,
scheduled task, auto-start entry, or system-wide resource owner is added.

A future installer must be a separately reviewed packaging project that
consumes one already validated portable output. It must not build or invoke
Nova, invoke FFmpeg, acquire an external runtime, or silently add files outside
an exact reviewed manifest. Install and uninstall must be explicit finite user
operations with separately defined failure and rollback behavior.

### Signing boundary

No signing key, certificate, password, token, key file, certificate
thumbprint, signing command, timestamp service, or secret-backed CI step is
stored or selected here.

Future Authenticode signing, if approved, is a post-publish release operation.
Its identity, certificate custody, timestamp policy, input hashes, output
hashes, failure behavior, and verification command require a separate review.
A valid signature does not approve an FFmpeg runtime or establish application
correctness.

### Update boundary

Nova adds no update feed, update URL, network check, downloader, background
agent, resident loop, scheduled task, replacement process, or automatic
installer invocation.

A future update design must separately define explicit user intent, signed
metadata, exact artifact identity, bounded download and staging, atomic
replacement, rollback, downgrade policy, and cleanup. An update artifact must
not contain FFmpeg unless a new runtime-pack ADR satisfies the existing legal
and provenance gates.

## Verification

Non-native contract tests require the portable project and profile to omit the
reviewed installer, bootstrapper, manifest-signing, assembly-signing, publish
URL, and update properties. Existing package tests continue to require the
exact legal-material allowlist and reject runtime packages and external binary
payloads.

## Consequences

The application ownership, legal/About, and portable package work for 0.0.10e
is complete without selecting installer, signing, or update implementation.
Any one of those mechanisms requires a later ADR and new tests before it enters
the default graph or release process.

This decision does not complete or accept 0.0.10d.

