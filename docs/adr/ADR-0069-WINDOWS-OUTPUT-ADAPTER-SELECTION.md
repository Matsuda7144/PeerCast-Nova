# ADR-0069 Windows Output Adapter Selection

## Status

Accepted for 0.0.6t.

## Decision

Windows 11 is the first supported output platform. Video selects Direct3D 11;
audio selects WASAPI shared mode with event-driven servicing. Both use Windows
SDK and standard COM interfaces without a third-party native package or bundled
runtime DLL.

Direct3D 11 is selected because a DXGI BGRA8 surface accepts the existing
canonical BGRA32 input without format conversion, and its device/context/swap
chain model is smaller for this boundary than Direct3D 12's explicit command
queues, fences, and descriptor management. Direct3D 12 is rejected for this
milestone because those responsibilities add no value before rendering begins.
Windows Graphics Capture is rejected because it captures existing content; it
is not a presentation backend.

WASAPI shared event-driven mode is selected because it is the Windows standard
COM path, supports negotiated interleaved S16/F32 input, follows the default
device, and avoids polling. SDL is rejected because it adds a native runtime
and distribution boundary while hiding the device-generation and COM lifetime
contracts this milestone must make explicit.

## Threading and lifetime contract

The UI thread owns only the future window and configuration requests; it does
not upload frames or service audio. A future dedicated render worker initializes
MTA COM if required and exclusively owns the D3D11 device, immediate context,
swap chain, back buffers, views, and upload resources. A separate audio worker
initializes MTA COM and exclusively owns the device enumerator, selected device,
audio client, render client, and event handle. Video and audio do not share a
lock. OS callbacks catch all exceptions and publish only fixed results.

COM initialization and `CoUninitialize` occur on the same owning worker. Native
resources are released in reverse creation order after admission closes and
in-flight work completes. Video releases upload/views/back buffers, swap chain,
context, then device. Audio stops the client, releases render service/client,
device/enumerator, closes event handles, then uninitializes COM. No COM pointer,
OS handle, or native resource crosses into the 0.0.6s public frame contracts.

Device lost increments a device generation and requires reset/reconfigure.
Resize explicitly reconfigures swap-chain resources. Video format changes and
audio format changes require explicit reconfigure. Default-audio-device change
invalidates the active audio generation, stops the old client, and requires
device re-selection and reconfiguration. Undefined HRESULT or API result fails
closed without exposing its raw value.

## Probe boundary

The probe deterministically selects D3D11 and WASAPI shared/event-driven, checks
Windows 11, canonical formats, and activation-prerequisite system APIs through
an injectable resource-free source, and normalizes results to fixed values.
Video requires `d3d11.dll!D3D11CreateDevice` and
`dxgi.dll!CreateDXGIFactory1`. Audio requires
`ole32.dll!CoCreateInstance` and `kernel32.dll!CreateEventW`. COM class
implementation DLL internals are intentionally not treated as public OS
activation contracts. Libraries are loaded from System32 only and
every temporary handle is released before the source call returns.

`Available` means only that these pre-activation DLLs and exports are present;
it does not mean that a usable video or audio device was enumerated. A future
activation boundary may return `DeviceUnavailable`. This probe creates no
window, device, swap chain, texture, audio client, event, or stream. Non-Windows
returns `PlatformUnsupported` without native access.

## Non-goals

No swap chain, texture upload, WASAPI client, audio write, window, presentation,
playback, scheduler integration, demux, backend activation, or FFmpeg bundling
is implemented.
