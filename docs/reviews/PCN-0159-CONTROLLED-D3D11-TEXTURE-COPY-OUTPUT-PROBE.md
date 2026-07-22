# PCN-0159: Controlled D3D11 Texture-copy Output Probe

## Scope

Validate a diagnostic comparison path for the 0.0.10d Phase 5 black-output boundary without changing production playback.

## Required behavior

- Use the existing application-owned Windows runner, owner dispatcher, activated D3D11 device, swap chain, and cleanup order.
- Generate the fixed `Bgra32QuadrantsV1` 320×240 opaque non-black frame.
- Create one exact BGRA D3D11 texture from the managed bytes.
- Copy that texture to swap-chain back buffer 0.
- Attempt Present exactly once.
- Keep the result visible for one bounded observation interval without another native output operation.
- Dispose the diagnostic boundary, stream, binding, and activation before stopping the borrowed runner.

## COM ABI requirement

`ID3D11DeviceContext.CopyResource` must retain the exact native `void` return contract with `PreserveSig` and two pointer-sized resource arguments. The first physical run demonstrated that an implicitly HRESULT-translated declaration is not an acceptable diagnostic boundary: it terminated with access violation before a canonical record could be emitted.

Reflection coverage must fail if the return type, `PreserveSig` attribute, or parameter types change.

## Rejection requirements

Reject before native access for invalid format, dimensions, stride, byte count, generation, initialization, binding, platform, or cancellation. Map occlusion, resize, device loss, unsupported format, and unknown HRESULTs to fixed results.

## Evidence

The sanitized external record must contain only the fixed milestone, result, pattern identity, upload-path identity, dimensions, finite counters, terminal runner state, cleanup／borrowed-owner preservation, and false runtime／publication statements.

It must not contain a repository path, runtime path, media path, record path, device identity, window handle, thread identity, COM pointer, native handle, or pixel payload.

## Validation gate

CircleCI final head must pass:

- solution restore;
- Release build with analyzers and warnings as errors;
- full solution tests;
- format verification.

Physical visibility remains a post-merge Windows observation and is not claimed by CI.
