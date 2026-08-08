# PCN-0242: Bounded ISO BMFF AVC Sample Framing

## Scope

Review framing of one owned ISO BMFF AVC sample using the selected `avcC` NAL
length width.

## Findings

- Invalid configuration is rejected before candidate access.
- Candidate and decoder configuration must refer to the same sample-description
  index and an AVC video entry.
- All one-to-four-byte unsigned big-endian length widths are supported.
- Empty, zero-length, truncated, partial-suffix, and over-count payloads fail
  closed.
- Returned NAL units are bounded views over the already owned sample payload;
  their bytes are not copied again.
- No Annex-B conversion, decoder, demux event, native operation, or physical
  execution occurs.

## Decision

Accept bounded AVC sample framing. Parameter-set injection, demux-event
projection, and production MP4 demux composition remain incomplete.
