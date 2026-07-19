# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.8p — Playback Cutover Architecture Review**

受信・中継・再生基盤を小さな有限境界に分け、所有権、失敗時の状態、reset／rollback、resource cleanupをテストで固定している段階です。

診断専用の再生経路では、H.264／AAC各1packetについて次の有限処理まで到達しました。

```text
native context open
→ packet send / frame receive
→ callback-scoped metadata and plane/sample view
→ owned managed frame copy
→ managed canonical conversion
→ awaited decoded sink callback
```

この経路はproduction decoder factoryから独立しており、FFmpeg backend activationや配布承認を変更しません。

0.0.8pでは、既存scheduler・timing・recovery・Windows outputへ接続する前にarchitecture reviewを行いました。

- converted callback payloadは保存・queueしない
- future cutover seamはconversion前のowned managed frame
- 既存`BoundedPlaybackScheduler`を再利用し、第二のdecoded queueを作らない
- decoder ingress前にschedulerへaggregate owned-byte budgetを追加する
- frame-count capacityだけではmemory boundとして不十分
- unknown timestampを許す現段階ではduration cutoffを必須化しない
- `Nova.Playback`のproject分割は具体的なplatform／packaging要求まで延期
- `net10.0`を維持し、supported servicing updateで検証する
- production dependencyや第三者assetを安易に追加しない

次の実装は**0.0.8q Bounded Scheduler Aggregate Byte Budget**です。

## 実装済みの主な領域

### PCP互換・Channel受信

- PeerCastStation互換のHTTP relay entryとPCP handshake
- CHAN control decodeとpayload受信
- bounded buffer、backpressure、有限timeout／停止
- 再接続を含むChannel Session
- privacy-safeな固定形式診断snapshot

### Relay

- bounded fan-out
- downstream relay session／server
- upstream、fan-out、downstreamを束ねるrelay coordinator
- 有限drainとcleanup
- controlled real-media relay verification

### 再生基盤

- FFmpeg runtime候補の検証境界とlease管理
- native decoder context、parameter transfer、codec open、packet/frame境界
- callback-scoped frame、metadata、plane/sample境界
- owned managed frame copyとcanonical format conversion
- serial awaited sink delivery
- bounded decoded-frame scheduler
- A/V timing、late-item、recovery、deadline-aware run policy
- Windows D3D11／DXGI／WASAPI output境界
- finite continuous pump boundary
- diagnostic H.264/AAC path through awaited sink delivery

詳細は以下を参照してください。

- [CHANGELOG.md](CHANGELOG.md) — milestoneごとの変更履歴
- [NAVIGATION.md](NAVIGATION.md) — 現在地、重要文書、次の作業
- [ROADMAP.md](ROADMAP.md) — 実装済み／未実装の全体計画
- [docs/adr](docs/adr) — Architecture Decision Records
- [docs/reviews](docs/reviews) — validationとreview記録

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

未実装または未承認の主な範囲:

- bounded FLV／MP4 demux
- production decoderからschedulerへのmanaged-frame ingress
- schedulerのaggregate byte budget
- decoded-buffer duration cutoff
- complete A/V synchronization、resampling、drift correction
- precise multimedia timerとin-flight preemption
- approved FFmpeg backend activation
- NovaによるFFmpeg runtime同梱／再配布
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg backend activation、runtime bundling／distribution、license approvalは、ADRで明示的に承認されるまで未承認です。

## 必要環境

- .NET SDK 10
- Windows 11はWindows output境界の対象
- Linux／WindowsのCIでbuild、test、formatを検証

Windows native outputを必要としない境界は、実deviceなしでもテストできるよう設計しています。

## ビルドとテスト

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

CLIの起動:

```powershell
dotnet run --project src/Nova.Cli/Nova.Cli.csproj -c Release
```

各診断コマンドの目的と制約は、`NAVIGATION.md`および`docs/reviews`を参照してください。

## 設計原則

- 1回の公開操作を有限化する
- resident loopや無限retryを暗黙に導入しない
- countだけでなくresource量も明示的にboundする
- 所有権を明示し、借用したresourceをdisposeしない
- 成功した処理だけをconsume／commitする
- undefined enumや不正値はfail closed
- resetの途中失敗を成功として隠さない
- lower boundaryのpointer、handle、HRESULTをpublic contractへ漏らさない
- 実装していない機能を完了扱いしない
- 第三者assetはprovenanceとredistribution permissionを記録する

## コントリビューション

プロジェクトは段階的に開発中です。変更は小さなmilestoneとADR、テスト、validation文書をセットにして進めます。

IssueやPull Requestを作成する際は、変更範囲、所有権、有限性、memory／time bound、失敗時の状態、検証方法を明記してください。

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。
