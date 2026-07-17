# PCN-0065 Native Parameter Transfer Validation

## Scope

Milestone 0.0.6g verifies fixed H.264/AAC native parameter transfer into an
existing decoder context. It does not claim codec open, packet/frame processing,
demux, conversion, redistribution, activation, or license approval.

## Implemented evidence

- context and parameter codec identifiers must match before native access;
- non-Windows and non-64-bit execution returns fixed `Unsupported`;
- the verified gate requires the fixed allocation, layout, and transfer symbols;
- managed owned extradata is copied with its 64 zero padding bytes into one
  temporary native `AVCodecParameters` owner;
- H.264 transfers bounded width and height;
- AAC transfers bounded sample rate and a default channel layout;
- temporary native parameters and extradata are freed on every return path;
- success clears the managed parameter lease while retaining the context;
- native allocation failure leaves both owners retryable;
- rejected or exceptional native transfer discards the context fail closed;
- codec mismatch, missing symbols, disposed owners, and unsupported platforms
  avoid native mutation;
- fixed results expose no extradata, padding, native pointer, path, hash, symbol
  address, or native error text;
- no codec open, packet/frame operation, binary, or dependency was added.

## Completed validation

- Windows and Ubuntu Release build, full test, and format jobs passed.
- Related Release tests completed successfully for 20 of 20 Windows runs.
- After milestone records were finalized and the standard read-only CI workflow
  was restored, the final pull-request head again passed Windows and Ubuntu
  build, full test, and format jobs.
