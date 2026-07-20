# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9c — AVPacket Metadata and Flush ABI Review**

0.0.9bでexact source identityを持つopened native H.264/AAC context pairを実装しました。0.0.9cでは、そのcontextへpacket／EOSを渡す前に必要なFFmpeg ABI境界を確定しました。

- exact verified FFmpeg 8 runtime identityだけを承認
- major-version-onlyまたはarbitrary runtimeのdirect `AVPacket` field writeを拒否
- packet storageは`av_packet_alloc`／`av_packet_free`で所有
- managed `sizeof(AVPacket)` allocationは禁止
- reviewed metadata prefixはPTS、DTS、data、size、stream index、key flag、duration、time baseだけ
- unknown PTS／DTSは`AV_NOPTS_VALUE`
- unknown durationは`0`
- exact source stream IDとsnapshotted time baseを要求
- EOSはempty packetではなく`avcodec_send_packet(context, NULL)`
- `avcodec_flush_buffers`はEOS drainから分離
- 1回のsendだけを行い、`EAGAIN`ではactive parser completionとnative packet ownershipを保持
- second encoded queue、sleep、spin、hidden retry／drain loopは禁止
- packet implementation、submission、flush execution、drain、PCP composition、backend activation、outputは未変更

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
- AVPacket metadata／flush ABI decision

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- metadata-aware native packet／flush boundary implementation
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

**0.0.9d — Metadata-aware Native Packet and Flush Boundary**

exact source／stream／time-base validation、FFmpeg-owned padded packet allocation、PTS／DTS／duration／keyframe mapping、1回のpacket sendまたはnull flush、`EAGAIN` ownership retention、packet-first cleanupを実装します。receive loop、second queue、PCP composition、backend activationは追加しません。

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