# PCN-0159: Controlled D3D11 Texture-copy Output Probe

## Scope

Validate a diagnostic comparison path for the 0.0.10d Phase 5 black-output boundary and correct the shared D3D11 device-context COM ABI mapping proven defective by the physical probe.

## Review result

`CompletedTextureCopyVisibleAndProductionPatternVisible`

## Required behavior

- Use the existing application-owned Windows runner, owner dispatcher, activated D3D11 device, swap chain, and cleanup order.
- Generate the fixed `Bgra32QuadrantsV1` 320×240 opaque non-black frame.
- Create one exact BGRA D3D11 texture from the managed bytes.
- Copy that texture to swap-chain back buffer 0.
- Attempt Present exactly once.
- Keep the result visible for one bounded observation interval without another native output operation.
- Dispose the diagnostic boundary, stream, binding, and activation before stopping the borrowed runner.

## COM ABI requirement

Both private `ID3D11DeviceContext` declarations must preserve the official method order through the invoked upload methods. The required critical sequence is:

1. `OMSetRenderTargets`;
2. `OMSetRenderTargetsAndUnorderedAccessViews`;
3. `OMSetBlendState`;
4. `OMSetDepthStencilState`;
5. `SOSetTargets`;
6. `DrawAuto`;
7. `DrawIndexedInstancedIndirect`;
8. `DrawInstancedIndirect`;
9. `Dispatch`;
10. `DispatchIndirect`;
11. `RSSetState`;
12. `RSSetViewports`;
13. `RSSetScissorRects`;
14. `CopySubresourceRegion`;
15. `CopyResource`;
16. production only: `UpdateSubresource`.

The two failed physical texture-copy attempts demonstrated that `PreserveSig` alone cannot repair a shifted vtable. The missing `OMSetBlendState` and `OMSetDepthStencilState` slots caused the managed `CopyResource` declaration to invoke `RSSetScissorRects` and caused the production `UpdateSubresource` declaration to invoke `CopySubresourceRegion`.

Reflection coverage must fail if either declaration loses the critical sequence, if probe `CopyResource` loses its native `void`／two-pointer contract, or if production `UpdateSubresource` and `OMSetRenderTargets` lose their preserved native signatures.

## Physical validation

Both corrected probes were executed once from clean main `5386637fdca7cab8dfd451491f99422930a67bfb`.

### Texture-copy comparison

- helper exit: `0`;
- `Result: Completed`;
- upload path: `CreateTexture2D+CopyResource`;
- Present attempts／acknowledgements: `1／1`;
- accepted bytes: `307200`;
- WouldBlock／resize／device loss: `0／0／0`;
- runner stopped, output cleanup completed, borrowed owners preserved;
- operator observation: `Texture-copy pattern visible: Yes`.

### Production upload

- helper exit: `0`;
- `Result: Completed`;
- upload path: production direct `UpdateSubresource`;
- Present attempts／acknowledgements: `1／1`;
- accepted bytes: `307200`;
- WouldBlock／resize／device loss: `0／0／0`;
- runner stopped, output cleanup completed, borrowed owners preserved;
- operator observation: `Known pattern visible: Yes`.

Neither run used FFmpeg, the media fixture, audio, paired verification, retry, an additional Present, fallback, or instrumentation.

These results physically validate the corrected texture-copy path, the corrected production upload path, and the shared D3D11／DXGI／Win32 presentation boundary for the exact generated pixels.

## Rejection requirements

Reject before native access for invalid format, dimensions, stride, byte count, generation, initialization, binding, platform, or cancellation. Map occlusion, resize, device loss, unsupported format, and unknown HRESULTs to fixed results.

## Evidence

The sanitized external record contains only the fixed milestone, result, pattern identity, upload-path identity where applicable, dimensions, finite counters, terminal runner state, cleanup／borrowed-owner preservation, and false runtime／publication statements.

It must not contain a repository path, runtime path, media path, record path, device identity, window handle, thread identity, COM pointer, native handle, or pixel payload.

## Validation gate

CircleCI final head must pass:

- solution restore;
- Release build with analyzers and warnings as errors;
- full solution tests;
- format verification.

## Next gate

Execute one controlled physical video run using the approved external runtime, retained fixture, corrected clean main, and a new external canonical record. Audio and paired verification remain blocked until that video record is independently accepted and the operator records `Video visible: Yes`.