# ADR-0168: Non-native Application Information Entrypoint

## Status

Accepted as a post-0.0.10e non-physical user-facing application slice.

## Context

`Nova.App` currently starts the shared Windows application host whenever it is
invoked. The existing immutable legal/About model can be projected through the
window dispatcher, but users and packaging checks have no application entrypoint
that can inspect this information without creating a window or native host.

## Decision

Add two bounded application arguments:

- `--help` (and `-h`) prints the exact supported application argument shape;
- `--about` prints the existing embedded legal/About model.

Both commands return before `NovaWindowsStartupComposition` is invoked. Unknown
or multiple arguments print only the usage line to standard error, return exit
code `2`, and also return before application startup.

No new legal source is introduced. `--about` reads only
`NovaAboutLegalMaterial`, so it preserves the exact project license, external
runtime distribution, notice, provenance, and runtime-replacement text already
reviewed for the portable application.

Invocation with no arguments preserves the existing startup and exit-code
mapping.

## Consequences

Users can inspect application usage and legal/runtime information on any
supported .NET host without starting a Windows window, dispatcher, message
loop, native output resource, or FFmpeg runtime.

This is one application information feature. It does not complete the
user-facing GUI, playback selection, channel navigation, settings, installer,
signing, or updater.

## Non-goals

No physical output, runtime discovery, native activation, network access,
downloader, resident process, or packaging mechanism is added.
