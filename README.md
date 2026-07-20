# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9h — PCP Chunk-to-FLV Parser Composition Review**

awaited PCP packet callbackから0.0.9g one-event FLV parser façadeと0.0.9e source-bound submission state machineまでの有限compositionをレビューしました。runtime wiringはまだ追加せず、callback memory、suffix、decoder backpressure、finalization、discontinuity、generation ownershipを先に確定しています。

- PCP readerはconsumerをawaitし、callback完了までpayload memoryを保持
- payloadは借用のみで、callback完了後にpacket／memoryを保持しない
- callbackを開いたまま処理し、caller suffixを別queueへコピーしない
- local suffix offsetはparserの`ConsumedByteCount`だけで前進
- parser completionがprefixで返った場合は、そのcompletionを処理後に同じcallback suffixを再開
- parser／decoder completionがactiveな間は新しいparser inputと次のPCP Atomを停止
- decoder `EAGAIN`はbounded state-machine `Advance`だけで進行
- no receive progress、work budget超過、contradictory accountingはfail closed
- exact PCP channel／session／packet sequenceとexact parser-created source generationを要求
- `StreamDescriptionAvailable`後にbinding、native session、fresh state machineを1 generationとして作成
- transport close、reconnect、stop、timeout、cancellation、failureをEOSと推測しない
- ownerが別の明示finalization操作でvideo／audio EOSを送る
- explicit discontinuityはretained submission、state machine、native session解放後にparserをreset
- reconnectだけではparser／decoder generationをresetしない
- second encoded queue、caller-suffix retention、resident loop、production activationなし

診断専用のframe copy、conversion、bounded scheduler、late-aware dispatch経路は引き続きproduction decoder factoryから独立しています。FFmpeg backend activationやruntime配布も未承認のままです。

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
- reviewed awaited PCP-to-FLV composition contract

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- finite awaited PCP-to-FLV composition implementation
- MP4／fragmented MP4 parser
- production decoder ingress
- decoded-frame ownership and delivery for the real FLV path
- decoded-buffer duration cutoff
- continuous decode and per-stream EOS drain
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9i — Finite Awaited PCP-to-FLV Composition Boundary**

`IPcpChannelPacketConsumer`を実装するserialized boundaryを追加し、callback内local suffix offset、exact identity／sequence、injected decoder-generation factory、bounded parser／decoder transitions、別操作の明示finalization、discontinuity generation replacement、fixed terminal snapshot、ordered cleanupをcross-platform focused testで実装・検証します。production backend activation、decoded-frame delivery、output wiringはまだ追加しません。

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
