# PCN-0229: Exact Rational ISO BMFF Sample/edit Movie Overlap

## Scope

Review exact projection of bounded sample/edit media overlaps onto the movie
timeline without rounding.

## Findings

- Absolute movie bounds use `Int128` numerators and a nonzero parsed media
  timescale denominator.
- Fractional and integral endpoints remain distinguishable.
- Empty, non-normal-rate, reordered, or mismatched edit associations fail
  closed.
- Preceding empty edits contribute to the absolute movie origin.
- Repeated edit appearances remain distinct and traversal is linear.
- No native or physical operation occurs.

## Decision

Accept exact rational movie-overlap projection. Rounding and partial-sample
admission policy remain incomplete.
