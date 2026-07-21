# Third-Party Code and Protocol Provenance

## Project license

PeerCast Nova source code is licensed under Apache License 2.0.

## PeerCastStation

PeerCastStation is used as an interoperability reference and test peer.
Its source repository uses a GPL-compatible contribution policy.

**Decision:** PeerCast Nova does not copy or translate PeerCastStation source
code into the Apache-licensed codebase.

## Independent PCP implementation references

Public PCP implementations may be consulted to identify behavior worth testing.
The current `Nova.Protocol.Pcp` code was written independently from documented
wire observations and contains no copied third-party implementation code.

If code is later copied, translated, or substantially derived from another
project, the contributor must:

1. verify license compatibility;
2. preserve required notices;
3. document the exact files and origin;
4. update third-party notices before merge.

## Visual assets

The Apache License covering Nova code does not automatically grant rights to the
PeerCast name, logo, icons, or other inherited visual assets.

Before public distribution, the project must establish and document permission
for every reused asset. Until then, the repository should use text-only
placeholders or newly created assets.

## Decoder backend boundary

Milestone 0.0.5c defines only original Apache-2.0 lifecycle interfaces,
ownership code, and test fakes. It does not include FFmpeg, GStreamer, VLC,
Media Foundation wrappers, codec binaries, headers, generated bindings, or
copied decoder source.

Milestone 0.0.5d adds only an original CLI-internal counting decoder and
content-free diagnostic composition. It does not decode, inspect, retain, or
transform media payload and does not change the backend licensing boundary.

Before adding a real backend, the change must record:

1. the exact upstream project, version, source or binary origin, and checksum;
2. the license for every linked or redistributed component;
3. static/dynamic/runtime-loading behavior and relevant build flags;
4. required copyright, attribution, source-offer, relinking, or notice files;
5. whether patent-encumbered or nonfree codec components are enabled;
6. the assembly/package boundary that prevents backend types from entering
   Network, Core, or UI contracts;
7. the behavior when that optional backend is absent.

A strong-copyleft backend or configuration must not become an undocumented
part of the default Apache-2.0 distribution. LGPL configurations require an
explicit compliance plan. Operating-system media APIs still require documented
SDK/runtime terms and redistribution assumptions. Legal review is required
before publishing a package that bundles a third-party decoder.

Milestone 0.0.5i enforces this boundary in code. Every backend descriptor must
declare origin, distribution form, license-review state, and native-code use.
The default policy accepts only approved first-party managed backends. Approved
third-party, bundled, and native backends each require explicit host policy;
pending or rejected license review can never be enabled by policy.

Milestone 0.0.5k additionally requires third-party, bundled, and native backend
descriptors to carry a bounded artifact manifest with upstream identity,
version, license identifier, and SHA-256. Free-form paths and URLs are excluded
from the runtime contract; detailed source-location evidence remains in review
documents and release provenance records.

Milestone 0.0.6a adds bounded SHA-256 verification of an external artifact
before activation. It does not select, download, redistribute, load, or execute
an FFmpeg build. An exact build and compliance plan remain required separately.

Milestone 0.0.6b pins one BtbN FFmpeg 8.1 LGPL shared Windows candidate for
technical evaluation. Its archive is not distributed with Nova and its runtime
provenance initially remains `Pending`. Pinning and technical verification do
not by themselves constitute legal approval.

Milestone 0.0.6c pins the five shared libraries required by the proposed runtime
boundary. The inventory is technical integrity evidence only; the libraries are
not bundled or approved for redistribution.

ADR-0051 fixes the current distribution policy: Nova does not bundle or
redistribute FFmpeg. A user or host supplies one explicit installation directory,
and Nova uses the verified libraries only through dynamic linking. Nova does not
search `PATH`, the registry, or conventional install locations.

ADR-0052 hardens this host-provided route by retaining verified DLL handles
and local-directory guard handles until the next gate finishes. This is an
integrity boundary and does not change the no-bundling policy.

Milestone 0.0.9v completes the license and distribution architecture review.
The exact BtbN `win64-lgpl-shared` build class is acceptable in principle for
external dynamic use because it selects LGPLv3 shared libraries and excludes
the GPL and nonfree build variants.

The permanent default distribution decision is:

- Nova does not bundle, mirror, rehost, download, extract, update, or install
  FFmpeg binaries;
- the user or application host supplies one explicit runtime directory;
- production policy permits third-party native code only when bundling remains
  disabled;
- GPL and nonfree FFmpeg variants remain prohibited;
- production registration occurs at the application composition root, not
  through the generic `PlaybackDecoderBackendFactory`;
- a third party that redistributes FFmpeg with Nova is independently responsible
  for the complete binary／source compliance package.

Milestone 0.0.9w implements the remaining compliance and replacement conditions.
`PlaybackFfmpegExternalRuntimeManifest` now provides immutable built-in and
host-supplied manifest routes. Both routes accept only `LGPL-3.0-or-later`,
require the five reviewed DLL roles, recognizable filenames, ABI majors,
positive lengths, and SHA-256 digests, and pass through the existing retained
runtime, dependency, ABI, and required-symbol boundaries.

The user or application host can authorize an interface-compatible modified
LGPL build with `CreateHostSupplied`. Replacement does not disable integrity
verification: the selected files remain bound to the host-provided lengths and
hashes, and later byte changes fail with a digest mismatch.

The exact reviewed built-in candidate is now `LicenseReview.Approved` for this
external-only route. Approval does not authorize bundling, mirroring, rehosting,
automatic acquisition, GPL or nonfree variants, or generic backend registration.
Application policy must still explicitly allow third-party native code and keep
bundled code disabled.

Activated CLI build and publish output includes `THIRD-PARTY-LICENSES/FFmpeg`
with a prominent notice, replacement instructions, verbatim LGPLv3 text, a
deterministic GPLv3 text archive, and fixed text hashes. Nova terms do not
prohibit reverse engineering performed to debug modifications to the LGPL
library.

See `FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md`, ADR-0132, ADR-0133, PCN-0140,
and PCN-0141.