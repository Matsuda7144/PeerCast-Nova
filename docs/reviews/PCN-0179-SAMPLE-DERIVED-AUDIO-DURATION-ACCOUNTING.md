# PCN-0179: Sample-derived Audio Duration Accounting

## Status

Implemented and verified as a non-physical scheduler prerequisite.

## Implemented behavior

- explicit opt-in beside an enabled duration cutoff;
- conservative upward tick rounding from sample count and sample rate;
- known frame duration remains authoritative;
- derived audio participates in aggregate duration ownership;
- unknown video remains rejected rather than receiving an invented duration;
- production generation options validate the dependent option pair;
- existing default and controlled-command configurations remain unchanged.

## Verification

Focused managed tests cover exact `1024 / 48000` audio accounting, upward
rounding at the cutoff boundary, continued unknown-video rejection, and
invalid option combinations. Routine solution build, tests, and format checks
remain required.

No physical run or readiness action is involved.

## Remaining work

Production still has no selected cutoff value. Unknown video duration remains
the unresolved policy input; complete decoded-buffer duration policy must not
be claimed.
