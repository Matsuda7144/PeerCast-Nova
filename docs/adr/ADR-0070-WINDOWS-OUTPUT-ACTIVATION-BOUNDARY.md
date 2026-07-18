# ADR-0070 Windows Output Activation Boundary

## Status

Accepted for 0.0.6u.

## Decision

Add one finite activation boundary for the Direct3D 11 and WASAPI
shared/event-driven adapters selected in 0.0.6t. `Available` from the 0.0.6t
probe remains only a prerequisite-API result; it does not imply that activation,
device selection, or format negotiation will succeed.

The public boundary returns only fixed `Activated`, `PlatformUnsupported`,
`ApiUnavailable`, `DeviceUnavailable`, `PermissionDenied`,
`InvalidEnvironment`, `Failure`, and `Disposed` results. Undefined native
results, exceptions, and unknown HRESULTs fail closed as `Failure`. No raw
HRESULT, COM pointer, or OS handle crosses the public contract.

Native work is isolated behind an injectable internal interface. Production
activation uses the published Windows contracts `CoInitializeEx`,
`D3D11CreateDevice`, `CoCreateInstance`, `IMMDeviceEnumerator`, `IMMDevice`, and
`CreateEventW`. Unit tests substitute managed resources and do not require a
physical video or audio device. Non-Windows returns `PlatformUnsupported`
before accessing the native interface.

## Ownership and cleanup

The calling worker owns the boundary and must activate and dispose it on the
same thread. It initializes MTA COM, creates the D3D11 device and immediate
context, activates the default render endpoint and audio client, and creates
the future event-driven service handle. The boundary retains all resources
until disposal.

Cleanup runs in exact reverse acquisition order: audio event/client/endpoint/
enumerator, D3D11 context/device, then COM uninitialization. Each native step
also releases its own partial acquisitions before returning failure. Public
disposal is idempotent, closes further activation admission, and cleanup
exceptions cannot expose native details or cause a second release.

## Non-goals

No window, swap chain, texture, upload, `Present`, audio-client stream
initialization, buffer write, playback start, default-device monitoring,
scheduler connection, A/V synchronization, FFmpeg activation, demux, media
playback, runtime bundling, or licensing decision is included.
