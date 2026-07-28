# ADR-0163: Framework-dependent Windows Portable Package

## Status

Accepted for milestone 0.0.10e.

## Context

ADR-0162 completed the bounded legal/About projection. The next packaging
slice must make the legal material available beside the user-facing
application without introducing an installer, native runtime redistribution,
or a transformed executable that obscures the reviewed assembly boundary.

0.0.10d remains deferred and Issue #129 remains open. Packaging is not
readiness or physical authorization.

## Decision

Add one explicit `WindowsPortable` publish profile for `Nova.App`:

- Release, `win-x64`;
- framework-dependent;
- ordinary app host;
- not single-file;
- not trimmed;
- not ReadyToRun;
- no debug symbols.

The application package copies the root Apache-2.0 `LICENSE` and `NOTICE` plus
an exact twelve-file allowlist from `THIRD-PARTY-LICENSES/FFmpeg`. The allowlist
contains notices, license texts, provenance, replacement guidance, the
distribution guard, hashes, build configuration, and text inventory only.

The package does not copy an FFmpeg DLL, executable, ZIP, runtime directory,
downloader, installer, updater, or locator. It does not use a wildcard that
could silently admit a later runtime payload.

## Verification

Non-native contract tests parse the project and publish profile, require the
exact legal allowlist, require all source files to exist, and reject wildcard,
runtime-pack, package-reference, or external binary entries. A repository-
external publish inventory check confirms that the legal files and project
license material are present and no FFmpeg runtime payload is published.

## Consequences

PeerCast Nova now has a reviewable portable Windows application package
boundary. Installer, signing, update, distribution, and runtime acquisition
remain separate future decisions.

This decision does not complete or accept 0.0.10d.
