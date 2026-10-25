# Phase 5E: GUI publishing Finish

This independently adapts old PR #281's GUI-only delta onto main
`624afc8ab987c67b7026706d1a894547b4fea20f` after #324. The old stacked
branch/history is retained. Application Finish, generation proof, final identity
fence, cleanup engine and restart gate remain owned by the existing lifecycle.
No Network termination implementation or deadline assertion is transplanted.

## Commands and results

The Publishing page offers Start, Finish, Interrupt in that order. Finish uses
the application's first admitted 30-second monotonic deadline. It requires an
admitted Start, not a merely queued GUI Start. Command and composition duplicates
share the accepted task before checking CanFinish. Replacement and closed
admission reject new Finish work; duplicates do not reset the deadline.

While GUI Finish is pending, Interrupt and application disposal admit forced Stop
before waiting for Start/Finish settlement. Cleanup remains the existing single
owned operation. LocalCompleted, independently proven OBS normal EOS, cancellation,
timeout and unproven cleanup remain distinct. A pending/faulted completion cannot
certify success. Cancellation is not graceful Finish; source EOF does not prove
Recording Writer Finalization or remote playback.

Restart remains explicit and requires a fresh owner and SessionId, settled
commands, terminal cleanup/identity evidence and the existing generation proof
rules. The ScopeText's restart statement is qualified by those safety conditions.

## Asynchronous display and ownership

The ViewModel captures its binding version, references and command target, then
performs borrowed reads outside its admission gate and off the UI thread. Refresh
requests share a single reader and coalesce. A blocked old read can delay fresh
display even after rebinding; no worker is spawned per request to evade it.

The composition supplies matching status/command generation observations. Mixed
generations fail closed. Publication checks binding version, generation and
disposal again. Native PostMessage dispatch publishes on the native UI thread;
property notifications do not execute under the ViewModel admission gate.
Publication/disposal are serialized, and detached/disposed views reject late work.
Outstanding operations remain retained and fault observed; synchronous callbacks
are not assumed to terminate when cancellation is requested.

Each binding has a distinct identity containing its version, status source and
command boundary references. Commands require the identity of the published
status/command pair, checked again when the worker starts. Rebinding does not
authorize the new boundary from an old display, even when both compositions use
generation 1 or a later binding returns to the same boundary. Until the new pair
is published, old buttons may remain visible but their requests are rejected.
Only the initial binding, before any publication, may capture the current
composition generation for an explicit command; the lifecycle still rejects
Finish before Start admission. Read errors or mixed-generation publication do
not provide command authority. No synchronous UI read or extra reader is added.

After admission closes, the same generation's committed Lifecycle Completion
supplies State, Failure, CompletionKind and CleanupCompleted without another
owner/adapter Snapshot read. Only IsCompletedSuccessfully permits Result access.
Cached safe identity/rejection fields do not authorize commands or replace proof.
Pending/faulted completion stays unproven; all closed commands stay disabled.
Diagnostics contain fixed text and safe classifications, not URLs, keys, payloads,
file paths or raw exceptions.

## Native surface and font

Start retains ID 1134 and Interrupt 1135; Finish uses unused ID 1136. Relay IDs
1131-1133 remain unchanged. Finish begins hidden/disabled and is shown only on the
Publishing page. Dispatch checks the clicked HWND, notification type, page and
detach state. Existing Japanese font ownership, dark owner draw and Interrupt's
danger color are retained; Finish uses the normal owner-draw color.

Buttons use the existing 78 by 26 logical layout, separated by 84 units. This
source arrangement is not physical DPI/resize acceptance. Clipping, minimum size,
scaling, disabled readability and actual rendering require the separate Windows
Native acceptance task.

The font coverage test includes GenesisGuiNextPublishingRefresh.cs. Runtime
JapaneseSample retains all previous characters and adds only the eleven missing
visible-text characters: あ / 付 / 件 / 功 / 性 / 拠 / 期 / 条 / 立 / 等 / ＝.
Its length increases from 528 to 539. The added source's non-ASCII text is a visible
safe diagnostic; no comment-only alphabet was added. GetGlyphIndicesW now requires
coverage of these actual labels. Message Font preference, existing fallback
families, DPI sizing and HFONT creation/selection/deletion rules are unchanged.
Passing managed membership/probe tests does not prove native glyph rendering.

## Retained recovery history and acceptance boundary

Initial focused 25 PASS / 2 FAIL exposed asynchronous test observations; recovery
separated actual admission from queued UI delivery without reversing assertions.
Later Commands/Lifecycle 55 PASS / 2 FAIL exposed lost closed-terminal evidence;
the combined presentation path was corrected. ViewModel/Generation 81 PASS / 1 FAIL
then exposed the required restart ScopeText phrase. App 1,175 PASS / 1 FAIL exposed
the missing runtime glyph-probe characters. Those logs/snapshots remain preserved;
none is classified flaky without evidence and no failed run is silently replaced.

Physical NOT_RUN. Independent code audit and limited Windows Native physical
acceptance remain separate requirements. No real OBS, WAN/NAT, remote playback,
writer finalization, finalized-file integrity/playback or History acceptance is
claimed. Noncooperative Start/Factory/Read/Dispose/I/O has no forced finite
termination guarantee. No merge, Draft release or old #281 closeout is performed.
