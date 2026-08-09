# PCN-0347: Exact ISO BMFF Selected Sequence Head Movie-Time Comparison

## Decision

Accept exact rational comparison of selected video/audio head leading movie
intervals using overflow-safe cross multiplication. The result retains exact
sources and classifies equal intervals and single-role/exhausted states without
a tie-break, winner selection, cursor advance, merge, native operation,
rendering, or physical execution.
