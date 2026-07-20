# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9j — Bounded Decode Iteration and Per-stream EOS Drain Review**

0.0.9iのsource-bound packet／flush compositionからdecoded frameを失わずに受け取る方法と、video／audioを独立してdecoder EOFまでdrainする有限contractをレビューしました。runtime decode iteration自体は次の0.0.9kへ分離しています。

- 現行production `Advance`のstate-only receiveは成功した一時`AVFrame`をreturn前に解放するため、second receiveでは同じframeをcopyできない
- 次のboundaryをexact decoder generation唯一のproduction receive authorityにする
- 1 public operationは最大1回の`avcodec_receive_frame`と最大1つのmanaged frame ownership transferだけを行う
- callback-scoped native frameから既存managed frame copyまでを同じreceive内で完了し、native pointerを保持しない
- copied frameはexact source、generation、stream、media、independent decoded sequence、discontinuityを持つowned completionになる
- generation全体でactive decoded completionは最大1件
- retained packet／flushはproduced frameが受理・解放された後だけ最大1回retry
- frame backpressure中はdecoded frameとencoded submissionの両ownershipを保持し、新しいparser／PCP inputを停止
- packet acceptance後の通常receiveはcaller-drivenで1回1frame、`NeedMoreInput`は正常な有限終了
- parser EOS、null flush acceptance、flush drain、decoder EOFを別状態として扱う
- accepted flush後はvideo／audioを別々にdrainし、各contextのEOFでのみstream完了
- relief／flush drain中の`NeedMoreInput`はno-progress failure
- frame count／managed byte budget、cancellation、discontinuity replacement、decoded-frame-first cleanupを要求
- second decoded queue、resident drain loop、double receive、`avcodec_flush_buffers`によるEOS処理なし

診断専用のmanaged frame copy、conversion、bounded scheduler、late-aware dispatch経路は引き続きproduction decoder factoryから独立しています。FFmpeg backend activationやruntime配布も未承認のままです。

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
- reviewed bounded decode iteration and independent per-stream EOS drain contract

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- source-bound one-frame receive and per-stream EOS drain implementation
- decoded-frame conversion／scheduler ingress／delivery for the real FLV path
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

**0.0.9k — Source-bound One-frame Receive and EOS Drain Boundary**

exact source-bound decoder generationに1回1frameのmanaged receive boundaryを追加します。pending packet／flush relief、ordinary post-accept receive、video／audio independent flush drainを同じ有限contractで実装し、source／generation identity、decoded sequence、active-completion backpressure、mode-specific no-progress、frame／byte budget、cancellation、failure、discontinuity、cleanupをcross-platform focused testで検証します。production backend activation、scheduler／output wiring、audible／visible playbackはまだ追加しません。

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