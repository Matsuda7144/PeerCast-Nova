# PCN-0112: Diagnostic Late-aware Scheduler Dispatch Validation

## Scope

0.0.8t composes the 0.0.8m managed-frame completion, the existing 0.0.8r
scheduler ingress, and at most two calls to the existing late-aware timed
dispatch seam.

## Acceptance criteria

### Preflight

- Only an undisposed `Advanced` source with a live native lease is accepted.
- `Copied` requires one undisposed frame.
- `NeedMoreInput` requires no frame.
- Video and audio frames must occupy their exact media slots.
- Every copied frame has a known, non-negative presentation timestamp.
- Unknown timestamps fail closed before ownership transfer.
- Unknown duration remains valid.
- No timestamp or duration is synthesized.
- Scheduler queue length, drop count, delivery count, and owned-byte count all
  start at zero.
- Invalid dispatch options fail before ingress.

### Ingress and dispatch

- Ownership transfer delegates to the existing 0.0.8r transaction.
- Video uses sequence 0 and audio uses sequence 1.
- A faulted ingress never advances into dispatch, including after video was
  accepted before an audio-ingress failure.
- At most one dispatch is attempted per accepted frame.
- No more than two dispatch calls occur.
- `VideoProcessed`, `VideoDropConsumed`, and `AudioProcessed` consume the
  corresponding outstanding media.
- Scheduler queue and byte accounting match the outstanding-media set after
  every consumption.
- Retained, wait, recovery, reset, device, environment, cancellation, disposal,
  and failure outcomes do not trigger an implicit retry.
- Partial ingress and partial dispatch remain explicit.
- No continuous pump or automatic audio recovery is started.

### Ownership and cleanup

- Full advancement releases the native lease only after all accepted frames are
  consumed and scheduler owned bytes return to zero.
- Partial, stopped, cancelled, and faulted completions retain the native lease
  while any scheduler-owned frame remains.
- Completion records accepted count, consumed count, outstanding media, ingress
  result, and last dispatch result.
- Completion disposal is idempotent.
- Cleanup disposes the scheduler and its managed frames before releasing the
  native lease.
- Source disposal after ingress cannot destroy scheduler-owned frames.

### Safety boundaries

- No second decoded-frame queue or scheduler is introduced.
- Tests inject ingress and dispatch behavior and do not activate Windows output
  resources.
- `PlaybackDecoderBackendFactory` and production activation paths are unchanged.
- FFmpeg activation, bundling, redistribution, and approval remain pending.
- Demux, continuous decode, flush, EOS, duration cutoff, and complete A/V
  synchronization remain outside scope.

## Focused tests

The milestone test suite covers:

- two-frame video-then-audio consumption;
- consumed video drop;
- unknown timestamp rejection;
- wrong-media-slot rejection;
- non-fresh scheduler rejection;
- zero-frame `NeedMoreInput` completion;
- retained video ownership;
- video consumption followed by audio recovery requirement;
- cancellation with outstanding ownership;
- audio ingress rejection after video acceptance;
- faulted audio ingress after video acceptance without dispatch;
- injected dispatch failure after partial consumption;
- invalid timed-dispatch options;
- scheduler-first cleanup and native-lease disposal order.

## Automated validation

The final branch head must pass restore, Release build, the full test suite, and
format verification on the routine Ubuntu workflow before merge.

Status: Verified by focused review; final branch-head CI is the merge gate.
