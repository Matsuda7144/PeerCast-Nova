# PeerCast Nova Navigation

## 現在地

**0.0.8t — Diagnostic Late-aware Scheduler Dispatch Transaction — Verified**

0.0.8m completionから既存0.0.8r scheduler ingressとlate-aware timed dispatchへ進む有限診断transactionを追加しました。

- preflight: source state、live lease、known/non-negative PTSを検査
- scheduler: 初期queue／drop／delivery／owned byteが全て0
- ingress: 既存0.0.8rへ委譲、video sequence 0、audio sequence 1
- dispatch: accepted frameごとに最大1回、合計最大2回
- consumed: VideoProcessed／VideoDropConsumed／AudioProcessed
- stopped: retained／wait／recovery／reset／device／environment／cancel／dispose
- partial: accepted数、consumed数、outstanding media、last resultを保持
- cleanup: scheduler-owned frameを先にdisposeし、その後native leaseを解放
- retry: continuous pumpやaudio recoveryの暗黙開始なし

Cross-platform testsではdispatchを注入し、D3D11、WASAPI、FFmpeg、decoder factoryをactivationしていません。

FFmpeg backend activation、Windows device activation、production decoder ingressは未承認・未接続のままです。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackDiagnosticLateAwareSchedulerDispatchTransaction.cs`
- `tests/Nova.Playback.Tests/PlaybackDiagnosticLateAwareSchedulerDispatchTransactionTests.cs`
- `src/Nova.Playback/PlaybackDiagnosticManagedFrameSchedulerIngressTransaction.cs`
- `src/Nova.Playback/PlaybackLateAwareTimedDispatchBoundary.cs`
- `docs/adr/ADR-0104-diagnostic-late-aware-scheduler-dispatch-transaction.md`
- `docs/reviews/PCN-0112-DIAGNOSTIC-LATE-AWARE-SCHEDULER-DISPATCH-VALIDATION.md`

## 次の作業

0.0.8dから0.0.8tまでの有限診断経路を再確認し、bounded demuxまたはproduction compositionへ進む前に残る前提条件を整理します。

## 未完了

- FFmpeg activation／distribution
- bounded demux
- production decoder ingress
- continuous decode、flush、EOS
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
