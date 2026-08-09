# PCN-0388: Exact ISO BMFF Track Sample-Description Selection Projection

## Decision

Accept exact source-ordered `stsd` selection for every bounded edited-sample
packet candidate, with shared parsed-box and handler provenance and no partial
projection, codec composition, runtime access, or physical execution.
