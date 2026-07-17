# PCN-0067 Packet / Frame Processing Boundary Validation

## Scope

Milestone 0.0.6i verifies bounded packet submission and decoded-frame state
receipt for an already-opened H.264 or AAC decoder context. It does not claim
frame extraction, conversion, demux, playback output, redistribution,
activation, or license approval.

## Implemented evidence

- empty and greater-than-4-MiB packets fail before native access;
- only a live opened context may send or receive;
- the verified gate requires packet, memory, frame, send, and receive symbols;
- encoded bytes are copied once into a temporary native buffer with 64 zero
  padding bytes;
- packet/data/frame ownership is released on every success and failure path;
- `0`, `EAGAIN`, EOF, allocation failure, and rejection map to fixed results;
- missing symbols and unsupported platforms avoid native mutation;
- unexpected exceptions dispose the context fail closed;
- results expose no packet bytes, pointers, paths, hashes, symbols, or native
  return values;
- frame contents are not read or retained.

## Completed validation

- Windows and Ubuntu Release build, full test, and format jobs passed during
  implementation validation.
- Related Windows Release tests completed successfully for 20 of 20 bounded runs.
- Final standard read-only Windows and Ubuntu CI passed on the permanent PR head.
