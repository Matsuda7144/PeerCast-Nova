# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9e — Finite One-event Packet Submission State Machine**

0.0.9dのmetadata-aware packet／flush completionを1つだけ保持し、初回send、bounded frame receive progress、retained retry、sequence／EOS commitを明示する有限state machineを実装しました。

- `Begin`はpacket sendまたはnull flushを1回だけ実行
- `EAGAIN`ではnative packet／flush状態とactive parser completionを保持
- `Advance`はframe receiveを最大1回だけ実行
- frame progress後だけretained packet／flushを最大1回再試行
- repeated `EAGAIN`でもactive submissionとencoded payload copyは1つだけ
- per-stream sequenceはnative acceptance後だけcommit
- video／audioのflush acceptedとdecoder EOFを独立管理
- accepted flush／EOF後の同一stream inputを拒否
- source mismatch、duplicate sequence、concurrent inputをnative work前に拒否
- cancellation／failure／disposeはpacket-first cleanup
- source sessionとnative decoder sessionは非所有
- resident loop、decoded-frame delivery、PCP composition、backend activationは未追加

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

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- controlled retained FLV fixture validation
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

**0.0.9f — Controlled Retained FLV Fixture Validation**

bounded FLV parserの実completion、source-bound native session、0.0.9e state machineをcontrolled fixtureで接続し、packet order、PTS／DTS、keyframe、audio／video interleave、parser backpressure、retained retry、per-stream EOSをproduction activationなしで検証します。

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
