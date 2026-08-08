# PCN-0310: Bounded ISO BMFF Codec Packet Projection

## Decision

Accept the codec-neutral managed packet projection for exact AVC-selected or
AAC-original payload bytes with retained edited timing and sample-description
identity. No demux loop, decoder ownership, native operation, rendering, or
physical execution occurs.
