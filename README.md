# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9b — Source-bound Opened Native Decoder Session**

0.0.8xでpure managed bounded FLV parser、0.0.8zでexact source-session identityを持つdecoder-parameter bindingを実装しました。0.0.9bでは、そのbindingをopened native H.264/AAC context pairへ有限に接続しました。

- exact source-session reference、video/audio stream IDs、time basesをnative work前にsnapshot
- distinct video／audio ABI gate ownershipを要求
- H.264 contextのcreate／parameter transfer／open完了後にAACへ進む
- bindingと両gate leasesをsuccess／failureに関係なくconsume
- partial configured／opened stateはsuccessとして返さない
- source disposal、context／transfer／open rejection、unexpected faultを明示
- success sessionはopened contextsだけを所有
- source demux sessionはnon-owning identity reference
- cleanupはaudio→video順でidempotent
- packet submission、AVPacket metadata、flush、drain、PCP ingress、backend activation、outputは未変更

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

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- AVPacket metadata／flush ABI review and implementation
- finite one-event timestamp-aware packet submission
- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- decoded-buffer duration cutoff
- continuous decode、flush、EOS drain
- complete A/V synchronization、resampling、drift correction
- approved FFmpeg backend activation
- FFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 次のマイルストーン

**0.0.9c — AVPacket Metadata and Flush ABI Review**

FFmpeg runtime identity、AVPacket layoutまたはverified setter seam、PTS／DTS／duration／time base／keyframe／unknown value／stream mapping、null-packet flush、ABI gate strengtheningをreviewします。major-version一致だけではdirect field writeを承認しません。

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
