# PCN-0159: Controlled D3D11 Texture-copy Output Probe

## Scope

Validate a diagnostic comparison path for the 0.0.10d Phase 5 black-output boundary and correct the shared D3D11 device-context COM ABI mapping proven defective by the physical probe.

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

The two physical texture-copy attempts demonstrated that `PreserveSig` alone cannot repair a shifted vtable. The missing `OMSetBlendState` and `OMSetDepthStencilState` slots caused the managed `CopyResource` declaration to invoke `RSSetScissorRects` and caused the production `UpdateSubresource` declaration to invoke `CopySubresourceRegion`.

Reflection coverage must fail if either declaration loses the critical sequence, if probe `CopyResource` loses its native `void`／two-pointer contract, or if production `UpdateSubresource` and `OMSetRenderTargets` lose their preserved native signatures.

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
