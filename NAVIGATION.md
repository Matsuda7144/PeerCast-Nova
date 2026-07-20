# PeerCast Nova Navigation

## 現在地

**0.0.8r — Diagnostic Managed Frame Scheduler Ingress — Verified**

0.0.8mのowned managed frameを既存`BoundedPlaybackScheduler`へ渡す有限診断transactionを追加しました。

- video: sequence 0
- audio: sequence 1
- discontinuity: None
- NeedMoreInput streamはskip
- accepted frameはscheduler ownershipへ移行
- partial acceptanceでは必要なnative leaseを保持
- atomic two-stream enqueueは主張しない

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。

## 最新の入口

- `.github/workflows/ci.yml`
- `src/Nova.Playback/PlaybackDiagnosticManagedFrameSchedulerIngressTransaction.cs`
- `tests/Nova.Playback.Tests/PlaybackDiagnosticManagedFrameSchedulerIngressTransactionTests.cs`
- `docs/adr/ADR-0102-diagnostic-managed-frame-scheduler-ingress.md`
- `docs/reviews/PCN-0110-DIAGNOSTIC-MANAGED-FRAME-SCHEDULER-INGRESS-VALIDATION.md`

## 次の作業

scheduler ownershipへ移した診断frameを、既存timing／output境界へ有限にdispatchする接続点をreviewします。

## 未完了

- FFmpeg activation／distribution
- bounded demux
- production decoder ingress
- continuous decode、flush、EOS
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
