# PCN-0173: Framework-dependent Windows Portable Package

## Status

Implemented for milestone 0.0.10e.

## Package contract

`Nova.App` has one explicit `WindowsPortable` profile targeting `win-x64`.
It is framework-dependent and keeps single-file, trimming, ReadyToRun, and
debug-symbol publication disabled.

The package contains:

- the ordinary managed application and its existing managed dependencies;
- the root Apache-2.0 `LICENSE` and `NOTICE`;
- exactly twelve reviewed files under `THIRD-PARTY-LICENSES/FFmpeg`.

The FFmpeg legal allowlist is explicit. No wildcard can admit a later DLL,
executable, archive, or other runtime payload.

## Preserved boundaries

- no FFmpeg binary is bundled, downloaded, installed, located, or invoked;
- no installer, updater, signing flow, or release upload is added;
- no HWND owner, owner thread, message pump, or dispatcher is added;
- `Nova.Windows` retains application resource ownership;
- `Nova.Playback` ownership is unchanged;
- default application startup still opens no automatic legal surface.

## Verification

Focused non-native tests parse the exact profile and project items, verify all
allowed legal sources exist, and reject wildcard, runtime-pack,
package-reference, DLL, executable, and ZIP entries. The repository-external
publish inspection confirms the expected legal layout and absence of an
FFmpeg runtime payload.

## Deferred evidence boundary

0.0.10d remains explicitly deferred and incomplete. Issue #129 and every
physical audio and paired-admission gate remain open. Packaging grants no
readiness or physical authorization.
