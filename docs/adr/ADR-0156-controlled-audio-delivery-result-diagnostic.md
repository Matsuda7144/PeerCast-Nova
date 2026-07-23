# ADR-0156: Controlled audio delivery-result diagnostic

- Status: Accepted
- Milestone: 0.0.10d Phase 5
- Date: 2026-07-23

## Context

The four-frame controlled physical-audio run on clean main `63f8be0cc5663ccda0f82bd5eee1c5a3dd3201d2` stopped at its second outer delivery with one acknowledged write, zero retained WouldBlock, and `Audio audible: No`.

PR #120 treated the second result as normal `RetainedWait` and added a bounded controlled-audio timing resume. The separately approved run on clean main `955ffcdad693391eed1535987c96589eb479741e` reproduced the same canonical boundary:

- `DeliveryRejected / None`;
- decode operations `3`;
- outer delivery attempts `2`;
- delivered `1`;
- retained WouldBlock `0`;
- stopped runner and disposed lifecycle;
- operator observation `Audio audible: No`.

That reproduction disproves the claim that the second outer result was necessarily `RetainedWait`. The existing canonical run record intentionally collapses every non-delivered, non-cancelled result to `DeliveryRejected`, so it cannot identify the actual fixed lifecycle result.

## Decision

Add a separate controlled Windows delivery-diagnostic command and record.

The command reuses the existing physical-run composition through an operations decorator. The decorator delegates every operation unchanged and records only the fixed result returned by each outer `DeliverOneAsync` call.

The diagnostic record contains:

- the existing run result and counters;
- the exact ordered outer delivery-result sequence;
- runner and lifecycle terminal states;
- borrowed-owner preservation flags;
- explicit statements that the command performed one physical run, published no external binary or payload, and makes no audio-acceptance claim.

The existing canonical physical-run schema, accepted visible video record, audio preflight, and paired-verification schema remain unchanged.

## Bounds

- output kind: audio only;
- required controlled target: four outer deliveries;
- maximum serialized delivery results: four;
- one result is recorded per outer delivery attempt;
- overflow makes the diagnostic record invalid;
- maximum record size: 16 KiB;
- runtime, fixture, records, and temporary files remain repository-external.

## Non-effects

The decorator does not:

- decode or transfer a frame;
- enqueue, reserve, release, or replace a scheduler item;
- resume or retry a result;
- change timing, timestamps, tolerances, audio buffer duration, or media selection;
- perform conversion, resampling, amplification, synthesis, silence insertion, fallback, or device recreation;
- expose paths, device identities, handles, pointers, HRESULTs, timestamps, packets, samples, payload bytes, raw stdout, or raw stderr.

The existing controlled-audio `RetainedWait` resume remains unchanged. The diagnostic observes only the final result returned to the outer composition.

## Physical gate

This implementation PR executes no physical output.

After merge and routine main CI success, one new diagnostic execution may be separately approved from the exact merged main commit. It must use the retained approved runtime and fixture, the accepted visible video record, new external diagnostic and failure paths, and no retry or paired verification.

## Retained diagnostic outcome

The diagnostic was executed exactly once from clean main
`fcab3837722efb716decbc175e1dee678c939d9b`, after the failed
post-EOS result had been retained by PR #128 and both final-head and post-merge
CircleCI succeeded.

The retained diagnostic record reported:

- `Run-Result: Completed`;
- `Evidence: Accepted`;
- fixture bytes / packets: `66,939 / 2`;
- decode operations: `4`;
- delivery attempts / delivered: `4 / 4`;
- retained WouldBlock: `0`;
- ordered results: four consecutive `AudioDelivered` values;
- runner / lifecycle: `Stopped / Disposed`;
- borrowed window / dispatcher preserved: `True / True`;
- external binary or payload published: `False`;
- physical runs executed by the command: `True`;
- audio acceptance claimed: `False`.

The operator heard no sound and recorded `Audio audible: No`. No retry, alternate
endpoint, timing change, buffer change, fixture change, ordinary audio helper,
additional probe, or paired verification was performed.

## Interpretation

The diagnostic did not reproduce the earlier third-attempt failure from clean main
`d3cf3a32c66bb5a8dc322e6b1e524bd807c518b4`, which had reported three attempts,
two deliveries, and a noise-like audible transient. Because the diagnostic's third
result was `AudioDelivered`, the exact historical third result is not recoverable
from retained evidence.

The documentation-only change between `d3cf3a32...` and `fcab383...` did not alter
production audio behavior. The two retained physical outcomes therefore establish a
non-deterministic or timing-sensitive controlled physical boundary. They also show
that four caller-visible `AudioDelivered` results and accepted counters do not by
themselves establish audible fixture output.

Issue #127 is closed as not planned because its requested historical result did not
reproduce. Investigation continues in Issue #129. No further physical execution is
authorized until a new bounded, payload-free evidence boundary is implemented,
merged, and reviewed.