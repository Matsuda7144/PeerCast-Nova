# PCN-0164: Controlled audio delivery-result diagnostic review

## Scope

Provide a bounded diagnostic for Issue #121 after the post-PR-#120 physical run reproduced the same second-delivery failure.

## Retained evidence

The run from clean main `955ffcdad693391eed1535987c96589eb479741e` is final and is not repeated by this review:

```text
Result: DeliveryRejected
Evidence: None
Decode-Operations: 3
Delivery-Attempts: 2
Delivered: 1
Retained-WouldBlock: 0
Runner-State: Stopped
Lifecycle-State: Disposed
Audio audible: No
```

The operator heard no recognizable fixture audio. The later PowerShell `else` parsing message occurred after the record was produced and does not affect the result.

## Correction to prior diagnosis

PR #120 assumed the second outer delivery returned `RetainedWait`. The unchanged result after adding bounded `RetainedWait` handling means that assumption is unsupported and must be considered disproven.

Possible fixed results include `RetainedAudioRecoveryRequired`, `ClockNotStarted`, reset/configuration retention, or another lifecycle result. None is claimed without a diagnostic record.

## Reviewed design

`PlaybackControlledWindowsDeliveryDiagnosticOperations` decorates the existing controlled operations interface.

- every non-delivery operation is delegated directly;
- each outer `DeliverOneAsync` result is appended once, in order;
- the existing composition remains responsible for counters, stopping, cleanup, and the physical result;
- the decorator does not inspect media buffers or native details;
- the record codec serializes four fixed slots plus an exact count and overflow flag;
- unused slots must be `None`;
- delivery-result count must equal the composition's outer delivery-attempt count.

The command accepts audio only and reuses the existing physical-run option parser. It returns exit `0` when the underlying run completes and exit `1` when a valid diagnostic record is produced for a failed run. Invalid command configuration returns `2`.

## Compatibility

No field is added to `PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD 1`.

Therefore:

- the accepted visible video record remains parseable;
- existing audio records remain parseable;
- single-record preflight is unchanged;
- paired verification is unchanged;
- the diagnostic record cannot be mistaken for an accepted physical-run record.

## Helper review

The dedicated PowerShell helper requires:

- Windows;
- exact clean `main` and an explicitly approved commit;
- the approved external runtime;
- the retained 66,939-byte fixture and exact digest;
- an independently accepted external video record;
- new distinct external diagnostic and failure paths.

It builds the existing runner, performs verify-only video preflight, starts the diagnostic child exactly once, validates the bounded diagnostic envelope, deletes temporary raw stdout/stderr, and performs no paired verification.

## Test coverage

Focused tests prove:

- `AudioDelivered -> RetainedAudioRecoveryRequired` remains ordered and exact;
- `RetainedWait`, `ClockNotStarted`, `ResetRequired`, and `OutputRejected` remain distinct;
- `RetainedWouldBlock` remains counted and is not hidden;
- video is rejected before operations creation;
- non-`None` trailing result slots are rejected;
- diagnostic records explicitly deny audio acceptance and external publication.

## Remaining gate

No physical output is executed by this PR. The exact terminal result remains unknown until one separately approved post-merge diagnostic run is completed without retry.
