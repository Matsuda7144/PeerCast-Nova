# PCN-0170: Shared Windows Application Ownership

## Status

Implemented for milestone 0.0.10e.

## Decision boundary

The project explicitly defers 0.0.10d without claiming completion. Issue #129,
canonical completed audio evidence, independent acceptance, stable delivery,
formal audio acceptance, and paired admission remain open.

The existing finite application-owned Windows runner implementation was moved
from `Nova.Cli` into `Nova.Windows`. `Nova.Cli` now consumes that shared
assembly. No second Win32 window, dispatcher thread, message queue, or native
pump was introduced.

## Ownership

`Nova.Windows` owns the application window, owner thread, message loop, bounded
dispatcher admission, and application-level shutdown. `Nova.Playback` continues
to borrow only the dispatcher contract and window token.

The controlled physical commands remain in `Nova.Cli`; moving the host does not
make those commands part of normal application startup and does not authorize
their execution.

## Files

- `src/Nova.Windows/Nova.Windows.csproj`
- `src/Nova.Windows/PlaybackWindowsPhysicalRunnerHost.cs`
- `src/Nova.Windows/SystemPlaybackWindowsPhysicalRunnerNative.cs`
- `src/Nova.Windows/Properties/AssemblyInfo.cs`
- `src/Nova.Cli/NovaWindowsGlobalUsings.cs`
- `src/Nova.Cli/Nova.Cli.csproj`
- `tests/Nova.Cli.Tests/PlaybackWindowsPhysicalRunnerHostTests.cs`
- `tests/Nova.Cli.Tests/Nova.Cli.Tests.csproj`
- `PeerCast-Nova.sln`

## Safety

This change does not alter delivery, timing, decode, endpoint selection, native
buffering, observation hold, cleanup ordering, evidence schemas, readiness, or
authorization.

No readiness script, capture wrapper, standalone preflight, authorization
entrypoint, physical helper, probe, diagnostic, paired verification, or command
capable of audio-device output was executed.

## Next

Add one user-facing startup composition above the shared host, followed by a
bounded legal／About projection. Do not duplicate the Win32 pump or make
`Nova.Playback` own application resources.
