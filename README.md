# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9l — Awaited Decoded Completion Composition Review**

0.0.9iのawaited PCP／FLV compositionと0.0.9kのsource-bound one-frame decode boundaryを接続する所有権・backpressure設計を確定しました。runtime composition自体は次の0.0.9mへ分離しています。

- future generationは0.0.9k boundaryを唯一のpacket submission／native receive authorityとして所有
- old generationの別submission state machine／native session併存を禁止
- decoded completion settlementのasync未完了状態をそのままbackpressureとして使用
- reliefではPCP callbackを開いたまま1 receive→1 settlement→release→1 retry-only transition
- settlement中はparser suffix、新しいPCP packet、別receive、retained retryを停止
- repeated `EAGAIN`はshared frame／managed-byte／advance budget内で次の有限cycleを要求
- ordinary post-accept receiveはcallback外で1回1frame、`NeedMoreInput`は正常
- accepted flush後のvideo／audio drainもcallback外で独立し、各decoder EOFまで1回1frame
- encoded input finalized、decoder drained、downstream ownership remainingを別状態として保持
- `BoundedPlaybackScheduler`だけをdecoded queueとして再利用し、second queueを追加しない
- video／audio別decoded sequenceをschedulerへ直接使わず、cross-media generation-wide sequenceを別commit
- scheduler ownership transferにはone-shot managed-frame detachが必要
- queued managed frameは同じdecoder generationのcontext lifetimeに束縛
- discontinuity時はactive completion／old scheduler framesをdecode boundary／native sessionより先に解放
- direct awaited sink deliveryとscheduler transferを同じframeへ二重適用しない

診断専用のmanaged conversion、bounded scheduler、awaited sink delivery、late-aware dispatch、Windows output境界は実装済みですが、real FLV compositionとproduction decoder factoryにはまだ接続されていません。FFmpeg backend activationやruntime配布も未承認です。

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
- managed frame copy、conversion、awaited delivery
- count／byte bounded decoded-frame scheduler
- finite late-aware scheduler dispatch
- A/V timing、late-item、recovery、deadline-aware run policy
- Windows D3D11／WASAPI output境界
- bounded demux output contract
- pure managed bounded FLV v1 parser
- exact FLV session decoder-parameter binding
- source-bound opened native decoder session
- exact AVPacket metadata／flush ABI decision
- metadata-aware native packet／flush boundary
- finite one-event packet submission state machine
- controlled retained FLV fixture validation
- one-event final EOS parser façade
- finite awaited PCP-to-FLV composition boundary
- bounded decode iteration and independent EOS drain review
- source-bound one-frame managed receive and EOS drain boundary
- awaited decoded completion composition review

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- finite awaited PCP／FLV／decode composition implementation
- real FLV decoded completionのscheduler ingress／awaited delivery
- production generation factory and decoder ingress activation
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9m — Finite Awaited Decoded Completion Composition Boundary**

`PlaybackPcpFlvCompositionGeneration`を0.0.9k boundary所有へ置き換え、注入式awaited settlement seamを実装します。PCP callback内のretained packet／flush relief、callback外のordinary receive、explicit finalizationとvideo／audio drain、shared work budget、cancellation、discontinuity、ordered cleanupをcross-platform fake consumerで検証します。real scheduler transfer、production ABI-gate acquisition、backend／output activation、audible／visible playbackは後続です。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- routine Linux CIはCircleCI

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