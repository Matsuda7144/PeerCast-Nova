# PCN-0066 avcodec_open2 Boundary Validation

## Scope

Milestone 0.0.6h verifies one fixed codec-open transition for an existing H.264
or AAC decoder context after successful native parameter transfer. It does not
claim packet/frame processing, demux, conversion, playback output,
redistribution, activation, or license approval.

## Implemented evidence

- the verified ABI/symbol gate requires `avcodec_open2`;
- only a live owned context with completed parameter transfer may open;
- the production boundary is fixed to 64-bit Windows;
- the codec and options arguments are null because the decoder was fixed during
  `avcodec_alloc_context3` and no option dictionary is approved;
- zero is the only accepted native success result;
- negative and unexpected positive native results fail closed;
- a rejected or exceptional native open disposes the context, gate, modules,
  and verified runtime;
- successful open marks the context once and retains it for later decoder I/O;
- duplicate open and repeated parameter transfer are rejected before native
  access;
- unsupported platforms, missing symbols, missing parameter transfer, and
  disposed owners avoid native mutation;
- fixed results expose no pointers, paths, hashes, native return values, option
  text, symbol addresses, codec bytes, or native error strings;
- no packet/frame operation, binary, or dependency was added.

## Completed implementation validation

- Windows and Ubuntu Release build, full test, and format jobs passed.
- Related Release tests completed successfully for 20 of 20 Windows runs.
- Strict zero-only success and repeated-transfer preflight tests passed.
- The final pull-request head passed the standard read-only Windows and Ubuntu
  build, full-test, and format workflow after milestone records were finalized.
