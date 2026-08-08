# PCN-0247: Bounded ISO BMFF SL Config Descriptor

## Scope

Review the minimal SLConfigDescriptor semantic boundary for the owned `esds`
hierarchy.

## Findings

- Only the descriptor location owned by the elementary-stream result is read.
- Empty and trailing-byte payloads are rejected independently.
- MP4 predefined configuration `2` is projected exactly.
- Custom and other predefined configurations remain explicitly unsupported.
- Missing input and foreign hierarchy are distinct from payload errors.
- No scheduler, decoder, native operation, demux event, or physical execution
  occurs.

## Decision

Accept the bounded predefined-MP4 SLConfigDescriptor boundary. Custom SL
configuration and timestamp scheduling remain incomplete.
