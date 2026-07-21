# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9o — One-shot Decoded Completion Transfer and Scheduler Ingress Boundary**

0.0.9nで確定したownership契約をruntimeへ実装し、source-bound decoded completionから既存`BoundedPlaybackScheduler`へmanaged frameを1回だけ移譲する境界を追加しました。

- completion ownershipをattached／detached／disposedで明示
- exact active decode boundaryだけがone-shot detachを承認
- detach前にsource、decoder／discontinuity generation、media、stream、stream-local sequence、managed bytes、frame livenessを再検証
- detach後のcompletion disposalはdecode backpressureだけを解放し、scheduler-owned frameを破棄しない
- scheduler ingressはvalidate→detach→enqueueを1回だけ実行
- enqueue後のrollback、frame再構築、retryを禁止
- accepted transferは`Enqueued`／`EnqueuedAfterDroppingOldest`のみ
- video／audio別stream-local sequenceはidentityとして維持
- scheduler用generation-wide cross-media sequenceはaccepted enqueue後だけcommit
- replacement generationの最初のaccepted frameだけ`DecoderReset`
- cancellation before detachはownership、sequence、reset markerを変更しない
- rejected／undefined／invalid／exhausted／exception pathはgeneration terminal
- scheduler ownershipをmatching decoder context lifetimeより先にcleanup
- second decoded queue、resident pump、production output activationは追加しない

診断専用のmanaged conversion、bounded scheduler、awaited sink delivery、late-aware dispatch、Windows output境界は実装済みです。0.0.9oでreal decoded frameのone-shot scheduler ingress境界まで到達しましたが、generation-matched scheduler delivery、production decoder factory、FFmpeg backend activation、runtime配布、audible／visible playbackにはまだ接続していません。

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
- source-bound one-frame managed receive and independent EOS drain
- finite awaited PCP／FLV／decode composition boundary
- decoded completion transfer／scheduler ingress architecture review
- exact-active one-shot decoded completion detach
- generation-wide scheduler ingress ownership boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- generation-matched scheduler delivery and output lifecycle
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

**0.0.9p — Generation-matched Scheduler Delivery and Output Lifecycle Review**

0.0.9oのscheduler-owned frameとmatching decoder context lifetimeを、既存のreservation、late-aware dispatch、managed conversion、awaited sink、Windows output境界へどう接続するかをレビューします。delivery、drop、stop、discontinuity、output failureのownershipとcleanup順を確定し、runtime compositionは次の実装マイルストーンへ分離します。

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
