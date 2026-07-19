# PCN-0103 Diagnostic Frame Metadata Transaction Validation

## Scope

Milestone 0.0.8k submits one bounded packet per stream and receives at most one callback-scoped frame per stream through the existing metadata boundary. It retains only validated managed metadata.

## Implemented evidence

- state-only frame receipt is not reused after its temporary native frame has been freed;
- video send and callback metadata extraction precede audio send and extraction;
- `Extracted` requires captured metadata;
- `NeedMoreInput` requires no captured metadata;
- rejected, contradictory, or faulted paths dispose the complete aggregate lease audio-first then video;
- success retains the same aggregate lease;
- codec and non-empty payload validation occurs before operations;
- no native pointer, plane/sample data, packet bytes, native error value, path, hash, or symbol is returned;
- no packet loop, flush, demux, conversion, output delivery, production activation, redistribution, or approval-state change is introduced.

## Validation pending

Windows and Ubuntu Release build, full tests, and format verification must pass on the final pull-request head.
