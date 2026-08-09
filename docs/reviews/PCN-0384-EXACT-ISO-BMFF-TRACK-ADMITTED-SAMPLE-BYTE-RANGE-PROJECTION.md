# PCN-0384: Exact ISO BMFF Track Admitted Sample Byte-Range Projection

## Decision

Accept exact source-ordered absolute byte-range and sample-description identity
projection for bounded admitted samples through reconciled chunk and size
metadata, without reading file payload bytes, constructing packets, runtime
access, or physical execution.
