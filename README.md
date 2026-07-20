# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8s — Diagnostic Scheduler Dispatch Architecture Review**

診断専用のH.264／AAC経路は、native context openからowned managed frame copy、既存bounded schedulerへの有限ingressまで到達しています。本番decoder factoryからは独立しており、FFmpeg backend activationや配布承認を変更していません。

0.0.8sでは、scheduler ownershipから既存timing／late-disposition／Windows output境界へ進む前に、次の安全条件を固定しました。

- `PlaybackLateAwareTimedDispatchBoundary.DispatchOneAsync`を既存composition seamとして再利用
- 0.0.8m completionでtiming readinessを検査してから0.0.8r ingressへ委譲
- copied frameにはknownかつnon-negativeなpresentation timestampが必要
- timestampやdurationを合成しない
- freshでcaller-ownedのschedulerをtransaction中はexclusive use
- accepted frameごとに最大1回、合計最大2回だけdispatch
- 全accepted frameがconsumeされるか明示cleanupされるまでnative leaseを保持
- retained、recovery、reset、device、environment、cancel、failureをfull successとして隠さない
- continuous pumpやaudio recoveryを暗黙に開始しない

0.0.8sはarchitecture reviewであり、runtime wiringやWindows device activationは追加していません。

## 実装済みの主な領域

### PCP互換・Channel受信

- PeerCastStation互換HTTP relay entryとPCP handshake
- CHAN control decodeとbounded payload受信
- backpressure、有限timeout、停止、再接続
- official Stable／developmentとの互換性検証

### Relay

- bounded fan-outとslow child isolation
- downstream relay session／server
- owned relay coordinatorと有限drain
- controlled real-media relay verification

### 再生基盤

- verified FFmpeg runtime locator／leaseとABI gate
- native decoder context、parameter transfer、codec open、packet/frame境界
- callback-scoped metadata／plane／sample view
- owned managed frame copyとcanonical conversion
- serial awaited sink delivery
- count／byte bounded decoded-frame scheduler
- diagnostic managed-frame scheduler ingress
- A/V timing、late-item、recovery、deadline-aware run policy
- Windows D3D11／WASAPI output境界
- finite continuous pump

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- bounded FLV／MP4 demux
- production decoder ingress
- diagnostic schedulerからlate-aware outputへのruntime transaction
- decoded-buffer duration cutoff
- continuous decode、flush、EOS
- complete A/V synchronization、resampling、drift correction
- precise multimedia timerとin-flight preemption
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.8t — Diagnostic Late-aware Scheduler Dispatch Transaction**

0.0.8m completionのtiming readinessを検査し、既存0.0.8r ingressへ委譲した後、accepted frameごとに最大1回だけ既存late-aware dispatchを実行する有限transactionを追加します。full／partial advancement、outstanding media、lease lifetime、cancellation、failure、cleanupを明示します。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- routine CIはUbuntu

## ビルドとテスト

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## 設計原則

- 1回の公開操作を有限化する
- resident loopや無限retryを暗黙に導入しない
- countだけでなくresource量もboundする
- ownership transferとcleanupを明示する
- 成功した処理だけをconsume／commitする
- undefined enumや不正値はfail closed
- reset途中の失敗を成功として隠さない
- native pointer、handle、HRESULTをpublic contractへ漏らさない
- 実装していない機能を完了扱いしない
- 第三者assetはprovenanceとredistribution permissionを記録する

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。
