# ADR-0282: Bounded ISO BMFF ER Error Protection Third Set

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Decision

Traverse exactly the third set's declared classes, retain each index and owned
end, consume its local `reordered_output`, require any order vector to be an
exact class-index permutation, and produce no partial set on truncation or an
invalid order.

## Non-goals

Fourth and later sets, general aggregation, header protection, complete config
traversal, decoder or native integration, and physical execution are not included.
