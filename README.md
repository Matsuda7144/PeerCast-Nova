# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9m — Finite Awaited Decoded Completion Composition Boundary**

callback-scoped PCP payloadをreal bounded FLV parserへ渡し、0.0.9k source-bound one-frame decode boundaryでpacket submission、retained relief、ordinary receive、independent EOS drainまで有限に接続しました。

- 新しい`PlaybackPcpFlvAwaitedDecodeCompositionBoundary`を追加し、既存0.0.9i boundaryは互換／比較用に維持
- generationは0.0.9k boundaryを唯一のpacket submission／native receive authorityとして所有
- PCP callback payload offsetはparserのexact `ConsumedByteCount`だけで前進
- retained packet／flush reliefはcallbackまたはexplicit finalizationを開いたまま処理
- 1 cycleは最大1 native receive→1 awaited settlement→completion release→1 retry-only transition
- settlement未完了中はparser suffix、新しいPCP input、retained retryを停止
- repeated `EAGAIN`はshared decode-operation／frame-count／managed-byte budget内で繰り返す
- decoded completionはexact source、decoder／discontinuity generation、media、stream、sequence、managed bytesをawait前後に再検証
- 0.0.9m settlementは`Consumed`だけを成功として受理し、frame transferは後続へ分離
- ordinary post-accept receiveはcallback外の1回1frame operation
- accepted-flush video／audio drainもcallback外で独立して各decoder EOFまで進行
- encoded input finalized、stream flush accepted、per-stream EOF、both decoders drainedを別状態として保持
- cancellation、settlement rejection、no progress、work-limit、identity／sequence mismatchをfail closed
- discontinuityはold settlement／decode generationを解放してからparser resetとnew generation作成
- second encoded queue、decoded queue、resident loop、background pumpなし
- 11 focused cross-platform testsでcallback blocking、retry ordering、budget、cancellation、EOS drain、replacementを検証

診断専用のmanaged conversion、bounded scheduler、awaited sink delivery、late-aware dispatch、Windows output境界は実装済みですが、real FLV decoded frameのscheduler ownership transferとproduction decoder factoryにはまだ接続されていません。FFmpeg backend activationやruntime配布も未承認です。

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
- finite awaited PCP／FLV／decode composition boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- decoded completion one-shot transfer／bounded scheduler ingress
- real FLV decoded scheduler delivery and output lifecycle
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

**0.0.9n — Decoded Completion Transfer and Scheduler Ingress Review**

0.0.9mのawaited-consumption契約を維持しながら、decoded completionからmanaged frameをone-shotでdetachして既存`BoundedPlaybackScheduler`へ移す所有権契約をレビューします。video／audio別のstream-local sequenceとscheduler用generation-wide cross-media sequenceを分離し、accepted／rejected transfer、native context lifetime、partial failure、discontinuity時のold-generation queue cleanupを実装前に固定します。

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
