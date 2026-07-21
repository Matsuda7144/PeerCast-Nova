# FFmpeg External Runtime Notice

PeerCast Nova can use FFmpeg libraries as an optional external runtime. FFmpeg is a separate project and is not licensed under the Apache License 2.0 that covers PeerCast Nova source code.

## Reviewed built-in identity

The only runtime identity reviewed by the PeerCast Nova project is:

- Build project: BtbN/FFmpeg-Builds
- Release tag: `autobuild-2026-07-21-13-38`
- Release time: `2026-07-21T13:38:40Z`
- BtbN commit: `8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665`
- FFmpeg commit: `703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d`
- Build ID: `btbn.ffmpeg.20260721.win64.lgpl-shared`
- FFmpeg version: `n8.1.2-29-g703dcc25b9-20260721`
- Candidate archive: `ffmpeg-n8.1.2-29-g703dcc25b9-win64-lgpl-shared-8.1.zip`
- Archive size: `70,510,808` bytes
- Archive SHA-256: `50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF`
- Declared license class: `LGPL-3.0-or-later`
- Linkage: external shared libraries

The archive name and checksum are provenance evidence only. PeerCast Nova does not provide that archive or promise that a third-party daily-build asset remains available.

The 2026-07-15 identity remains documented in `PROVENANCE.md` as superseded and is not accepted by the active manifest.

## Host-supplied runtimes

A host-created manifest is a **host declaration and host attestation**, not a PeerCast Nova legal or technical review. It remains `Unreviewed` by Nova. The host is responsible for verifying the actual FFmpeg configuration, dependency licenses, local legal obligations, and suitability of the exact files it authorizes.

Nova accepts the host route only when the application explicitly enables `AllowHostAttestedRuntime`. This authorization does not convert the host declaration into `LicenseReview.Approved`.

## Distribution boundary

PeerCast Nova's default distribution policy is stricter than the minimum conditions that may apply under the LGPL:

- Nova does not bundle, mirror, rehost, download, extract, install, update, or redistribute FFmpeg executables, DLLs, headers, import libraries, or archives.
- A user or application host supplies one explicit local runtime directory.
- GPL, nonfree, unknown, and differently declared runtime classes are rejected by this route.

These are PeerCast Nova project and release rules. They are not a complete statement of every obligation that may apply to a user, host, or third-party distributor. A party that redistributes FFmpeg binaries must independently satisfy the applicable license, source, build, notice, relinking, installation-information, dependency, and source-availability requirements.

## License texts and user freedoms

The GNU Lesser General Public License version 3 is included as `COPYING.LGPLv3`. The GNU General Public License version 3 is included directly as the readable text file `COPYING.GPLv3`; `COPYING.GPLv3.gz` is retained only as a deterministic duplicate. `SHA256SUMS` records the fixed text hashes.

PeerCast Nova distribution terms, EULA text, installer terms, and translated terms must not prohibit reverse engineering performed to debug modifications to the LGPL libraries. A release that introduces such a prohibition must fail the compliance gate.

Users may replace the external libraries with an interface-compatible modified LGPL shared build through the host-attested manifest route. Replacement does not disable exact filename, length, SHA-256, ABI, dependency, or symbol verification. See `RUNTIME-REPLACEMENT.md`.

## Attribution and trademark

FFmpeg and its dependencies retain their own copyrights and licenses. FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project. PeerCast Nova is not affiliated with or endorsed by the FFmpeg project or BtbN/FFmpeg-Builds.

This notice is an engineering compliance record, not legal advice or a patent-clearance statement.
