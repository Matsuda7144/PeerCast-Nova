# PCN-0392: Exact ISO BMFF Track Codec-Packet Binding Projection

## Decision

Accept exact source-ordered codec-packet binding to one caller-provided `tkhd`
and source-retained `mdhd` / `hdlr`, without movie binding, runtime access, or
physical execution.
