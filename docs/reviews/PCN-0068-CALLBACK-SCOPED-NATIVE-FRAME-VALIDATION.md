# PCN-0068 Callback-Scoped Native Frame Boundary Validation

## Scope

Milestone 0.0.6j verifies finite ownership of one received native `AVFrame` for an already-opened H.264 or AAC decoder context. It does not claim frame metadata extraction, plane copying, conversion, demux, playback output, redistribution, activation, or license approval.

## Implemented evidence

- only a live opened context may receive and process a frame;
- required frame allocation, free, and receive symbols are checked before native access;
- the frame pointer remains inside the native boundary;
- the consumer runs only while the frame is valid;
- the frame is released on success, native rejection, consumer rejection, and exception paths;
- success, `EAGAIN`, EOF, allocation failure, native rejection, consumer rejection, unsupported platform, and unavailable context map to fixed results;
- consumer rejection leaves the context usable;
- unexpected native failures dispose the context fail closed;
- results expose no frame content, pointers, paths, hashes, symbols, or native return values.

## Validation

- Windows and Ubuntu Release build, full test, and format jobs passed during implementation validation.
- Related Windows Release tests completed 20 of 20 bounded runs.
- Final standard read-only Windows and Ubuntu CI must pass on the permanent PR head.
