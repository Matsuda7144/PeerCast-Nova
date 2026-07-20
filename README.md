# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9i — Finite Awaited PCP-to-FLV Composition Boundary**

awaited PCP packet callbackからone-event FLV parser façade、exact source-bound decoder generation、finite packet-submission state machineまでを1つのserialized boundaryとして接続しました。

- `PlaybackPcpFlvCompositionBoundary`が`IPcpChannelPacketConsumer`を実装
- exact PCP channel ID／session ID／packet sequenceを検証
- callback payloadは借用のみで、callback完了後にpacket／memoryを保持しない
- callback内local suffix offsetはparserの`ConsumedByteCount`だけで前進
- 1 callback内の複数FLV recordとbounded parser carryによるsplit recordを処理
- parser completionを処理してから同じcallback suffixを再開
- `StreamDescriptionAvailable`後にinjected factoryからexact source-bound generationを作成
- generationはfinite submission state machineとnative sessionを所有し、parser sourceは非所有参照
- packet／flush `EAGAIN`は明示frame progress後のbounded `Advance`だけで進行
- no progress、accounting矛盾、identity／sequence／generation不一致、work limit超過をfail closed
- `CompleteInputAsync`だけがvideo／audio EOSを開始し、transport状態やempty packetをEOSと推測しない
- explicit discontinuityではold state machine／native session解放後にparser generationを置換
- fixed enum snapshot、fixed-message failure、idempotent ordered cleanup
- second encoded queue、resident loop、production backend activation、decoded output wiringなし

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
- finite awaited PCP-to-FLV composition boundary

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- bounded decode iteration and per-stream EOS drain
- decoded-frame ownership and delivery for the real FLV path
- production generation factory and decoder ingress
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

**0.0.9j — Bounded Decode Iteration and Per-stream EOS Drain Review**

0.0.9iでacceptedになったpacket／flush transitionからdecoded frameを受け取る有限iterationをレビューします。per-stream receive、packet acceptance後の追加出力、independent EOS drain、native-frame ownership、no-progress、operation budget、cancellation、discontinuity reset、cleanup順を先に確定し、runtime実装は次のmilestoneへ分離します。

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
