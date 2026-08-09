# PCN-0428: Bounded ISO BMFF Track Fragment Header

## Decision

Accept strict FullBox-v0 `tfhd` parsing with exact flag-derived optional fields
and semantic flags, without fragment traversal, default or offset resolution,
streaming, decoder access, or physical execution.
