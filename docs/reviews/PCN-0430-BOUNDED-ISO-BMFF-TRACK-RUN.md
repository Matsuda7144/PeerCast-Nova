# PCN-0430: Bounded ISO BMFF Track Run

## Decision

Accept strict bounded `trun` parsing with version-sensitive composition offsets,
exact flag/count-derived layout, and immutable entries, without inherited
default resolution, data lookup, traversal, streaming, decoder access, or
physical execution.
