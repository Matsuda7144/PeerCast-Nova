# FFmpeg External Runtime Notice

PeerCast Nova can use the FFmpeg libraries as an optional external runtime.

FFmpeg is a separate project and is not licensed under the Apache License 2.0 that covers PeerCast Nova source code. The reviewed PeerCast Nova runtime route accepts only an FFmpeg shared-library build declared as `LGPL-3.0-or-later`.

PeerCast Nova does not bundle, mirror, rehost, download, extract, install, update, or redistribute FFmpeg executables, DLLs, headers, import libraries, or archives. A user or application host supplies one explicit local runtime directory.

The reviewed built-in candidate identity is:

- Build project: BtbN/FFmpeg-Builds
- Build ID: `btbn.ffmpeg.20260715.win64.lgpl-shared`
- FFmpeg version: `n8.1.2-22-g94138f6973-20260715`
- Candidate archive name: `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`
- Candidate archive SHA-256: `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`
- License class: `LGPL-3.0-or-later`
- Linkage: external shared libraries

The candidate archive name and checksum are provenance evidence only. PeerCast Nova does not provide that archive or promise that a third-party daily-build asset remains available.

The GNU Lesser General Public License version 3 is included beside this notice as the verbatim text file `COPYING.LGPLv3`. The GNU General Public License version 3 is included as the deterministic gzip file `COPYING.GPLv3.gz`; `COPYING.GPLv3.README` records how to access it and `SHA256SUMS` fixes the decompressed license-text digests.

Users may replace the reviewed libraries with an interface-compatible modified LGPL shared build. PeerCast Nova does not prohibit reverse engineering performed to debug modifications to the LGPL libraries. The replacement procedure is described in `RUNTIME-REPLACEMENT.md`.

FFmpeg and its individual dependencies retain their own copyrights, licenses, trademarks, and any applicable patent considerations. This notice is an engineering compliance record and not legal advice or a patent clearance statement.
