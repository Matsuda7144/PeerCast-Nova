# PCN-0174: Installer, Signing, and Update Boundary

## Status

Reviewed for milestone 0.0.10e.

## Selected release boundary

The framework-dependent `WindowsPortable` output remains the only implemented
application distribution boundary. This review adds no installer, signing
operation, updater, release upload, or external runtime acquisition.

## Installer boundary

- no installer or bootstrapper project;
- no custom action or application invocation during install;
- no service, scheduled task, auto-start entry, or hidden resident owner;
- no FFmpeg binary, archive, downloader, locator, or installation step;
- any future installer consumes an exact already validated portable output
  under a separate manifest and rollback review.

## Signing boundary

- no key, certificate, password, token, thumbprint, or key file in source;
- no signing or timestamp command in the default build or CI graph;
- future signing is a separately reviewed post-publish release operation;
- a signature does not convert a host runtime into a Nova-approved runtime.

## Update boundary

- no feed, URL, network check, download, staging, replacement process, service,
  scheduled task, or resident update loop;
- no automatic installer execution;
- future updates require signed metadata, exact identity, finite transfer,
  atomic replacement, rollback, downgrade, and cleanup contracts;
- FFmpeg remains external unless a new runtime-pack ADR satisfies all legal and
  provenance requirements.

## Verification

The non-native package contract tests reject installer, bootstrapper,
manifest-signing, assembly-signing, publish URL, and update properties in the
portable project and profile. Existing tests continue to require the exact
project and FFmpeg legal-material allowlist and prohibit runtime packages or
external binary payloads.

## Milestone result

0.0.10e is complete as an application ownership and portable packaging
boundary. It does not claim a finished product installer or automatic update
system.

0.0.10d remains explicitly deferred and incomplete. Issue #129 remains open;
no readiness, physical execution, formal audio acceptance, or paired admission
is granted by this review.
