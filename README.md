# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9f — Controlled Retained FLV Fixture Validation**

実bounded FLV bytesをpure managed parserへ渡し、parserが作成したexact source sessionとsource-owned completionを0.0.9e state machineへ接続するcontrolled fixtureを検証しました。

- video→audio→video→audioのpacket interleaveを保持
- video／audioのsequenceを独立保持
- AVCC／AAC payloadを変更せず保持
- FLV DTSとsigned composition-time PTSを保持
- video keyframe／non-keyframe状態を保持
- packet `EAGAIN`で元のnative packet＋parser completionを保持
- flush `EAGAIN`で元のper-stream EOS completionを保持
- retained中のparserは`ActiveCompletion`を返しinputを消費しない
- retained transitionはreceive 1回＋retry 1回だけ
- explicit video EOS後のfinal audio EOSを独立flushとして受理
- native FFmpeg、decoded output、PCP composition、backend activationは未接続

検証により、両streamが未終了の通常final inputはvideoとaudioのEOSを1 completionへまとめる一方、one-event state machineはそのcompletionをnative work前にfail closedすることも確認しました。PCP compositionの前にfinal EOSを1イベントずつsequencingする必要があります。

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

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- one-event final EOS sequencing
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- decoded-frame ownership and delivery for the real FLV path
- decoded-buffer duration cutoff
- continuous decode and per-stream EOS drain
- discontinuity reset semantics
- complete A/V synchronization、resampling、drift correction
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9g — One-event Final EOS Sequencing**

両streamが未終了のfinal inputでも、parserが1操作につき1つのsource-owned per-stream EOS completionだけを返すようにします。video EOS completionの解放後に次のfinal操作でaudio EOSを返し、sequence commitとactive-completion backpressureを保ちながらdetached EOS copyやsecond queueを追加しません。

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
