# PeerCast Nova Navigation

## 現在地

**0.0.8s — Diagnostic Scheduler Dispatch Architecture Review — Accepted**

0.0.8rでscheduler ownershipへ移した診断frameを、既存timing／late-disposition／Windows output境界へ安全に接続する条件を固定しました。

- composition seam: `PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync`
- source seam: 0.0.8m completionから開始し、timing preflight後に0.0.8r ingressを内部実行
- copied frame: knownかつnon-negativeなpresentation timestampが必須
- timestamp synthesis: 不可
- scheduler: fresh、caller-owned、transaction中はexclusive use
- initial scheduler snapshot: queue、drop、delivery、owned byteの全値が0
- dispatch: accepted frameごとに最大1回、合計最大2回
- lease: accepted frameが全てconsumeされるか明示cleanupされるまで保持
- retained／recovery／reset／device／environment／cancel／failureは明示停止
- continuous pumpとaudio recoveryの自動開始なし

FFmpeg backend activation、Windows device activation、production decoder ingressは未承認・未接続のままです。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackDiagnosticManagedFrameSchedulerIngressTransaction.cs`
- `src/Nova.Playback/PlaybackLateAwareTimedDispatchBoundary.cs`
- `src/Nova.Playback/PlaybackLateAwareContinuousPumpBoundary.cs`
- `src/Nova.Playback/PlaybackRecoveryAwarePumpOrchestrator.cs`
- `docs/adr/ADR-0103-diagnostic-scheduler-dispatch-architecture-review.md`
- `docs/reviews/PCN-0111-DIAGNOSTIC-SCHEDULER-DISPATCH-ARCHITECTURE-REVIEW.md`

## 次の作業

**0.0.8t — Diagnostic Late-aware Scheduler Dispatch Transaction**

0.0.8m completionのtiming readinessを検査し、既存0.0.8r ingressへ委譲した後、accepted frameごとに最大1回だけ既存late-aware dispatchを実行する有限transactionを追加します。consume済みmedia、outstanding media、partial advancement、lease lifetime、cleanupを固定します。

## 未完了

- FFmpeg activation／distribution
- bounded demux
- production decoder ingress
- continuous decode、flush、EOS
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
