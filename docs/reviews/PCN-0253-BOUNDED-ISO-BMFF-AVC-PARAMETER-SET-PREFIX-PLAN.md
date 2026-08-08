# PCN-0253: Bounded ISO BMFF AVC Parameter-set Prefix Plan

## Scope

Review bounded non-materialized composition of `avcC` parameter sets before one
classified sample NAL sequence.

## Findings

- Invalid limits and missing input fail before payload access.
- SPS and PPS presence is required and each configuration kind must match its
  NAL header type.
- Parameter-set lengths must fit the selected sample length-prefix width.
- Configuration and sample units retain exact order and source labels.
- Total count and projected encoded bytes are bounded before any allocation.
- No byte generation, decoder, native operation, demux event, or physical
  execution occurs.

## Decision

Accept the bounded parameter-set prefix plan. Materialization and production
injection policy remain incomplete.
