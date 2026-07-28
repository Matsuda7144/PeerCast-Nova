# PCN-0172: Bounded Legal/About Projection

## Status

Implemented for milestone 0.0.10e.

## Material boundary

`NovaAboutLegalMaterial` projects the retained FFmpeg `NOTICE.md`,
`PROVENANCE.md`, and `RUNTIME-REPLACEMENT.md` into an immutable application
model. Every document and the combined model have fixed size bounds. Missing,
empty, invalid UTF-8, or oversized material is rejected.

The model states that PeerCast Nova source is Apache-2.0 licensed and FFmpeg is
an optional external shared runtime that PeerCast Nova does not bundle. It
does not contain a runtime path, binary, archive, downloader, installer, or
activation instruction.

## Ownership and lifetime

`NovaAboutLegalProjection` invokes one explicit target through the existing
shared `Nova.Windows` dispatcher. A successful target returns one finite
projection lifetime. The caller closes that lifetime and then performs the
existing host stop and disposal sequence.

No HWND owner, owner thread, message pump, dispatcher, or resident hidden loop
was added. `Nova.Playback` continues to own no application resources.

## Fail-closed behavior

- non-Windows systems reject before dispatcher or target access;
- unavailable or invalid legal material is not partially projected;
- dispatcher rejection and target rejection return fixed results;
- cancellation and unexpected failures expose no resource content or private
  exception detail;
- default application startup opens no automatic surface.

## Verification

Focused non-native tests cover exact model text, fixed model fields, one
dispatcher invocation, finite lifetime disposal, dispatcher rejection, target
rejection, non-Windows behavior, and default dependency-graph safety. The
application project embeds only the three legal text resources and has no
FFmpeg executable, library, archive, bundle, download, install, or invocation
reference.

## Deferred evidence boundary

0.0.10d remains explicitly deferred and incomplete. Issue #129, canonical
completed audio evidence, formal acceptance, stable ordinary delivery, and
paired admission remain open. This projection grants no readiness or physical
authorization.
