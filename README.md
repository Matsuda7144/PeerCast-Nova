# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.7c — Late Item Disposition Policy Boundary**

現在は、受信・中継・再生基盤を小さな技術境界に分け、所有権、有限実行、失敗時の状態、reset／rollbackをテストで固定している段階です。

0.0.7cでは、既存のA/V timing判断が返す`VideoLate`／`AudioLate`を直接受け取り、次の方針を決める純粋なpolicy境界を追加します。

- 既定では遅れたvideo／audio itemを保持
- 明示設定時だけvideoを`VideoDropCandidate`として推薦
- 明示設定時だけaudioを`AudioRecoveryRequired`として推薦
- timestampやplayback positionを再計算しない
- scheduler reservationをconsumeしない
- frameをdrop／disposeしない
- audioをresample／stretchしない
- generation mismatchとdiscontinuityは明示reset要求へ固定

`VideoDropCandidate`と`AudioRecoveryRequired`は**判断結果のみ**です。実際のdrop、acknowledgement、resampling、drift correctionはまだ実装していません。

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
- loopback限定の実機診断gate

### 再生基盤

- FFmpeg runtime候補の検証境界とlease管理
- decoder lifecycle、managed frame、ownership境界
- bounded decoded-frame scheduler
- Windows D3D11／DXGI／WASAPI output境界
- one-item scheduler adapter
- 有限continuous pump policy
- monotonic playback clockによるA/V timing判断
- bounded cancellable Waitとsuccess-only dispatch
- late item disposition policy

詳細は以下を参照してください。

- [CHANGELOG.md](CHANGELOG.md) — milestoneごとの変更履歴
- [NAVIGATION.md](NAVIGATION.md) — 現在地、重要文書、次の作業
- [ROADMAP.md](ROADMAP.md) — 実装済み／未実装の全体計画
- [docs/adr](docs/adr) — Architecture Decision Records
- [docs/reviews](docs/reviews) — validationとreview記録

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

未実装または未承認の主な範囲:

- 実際のvideo frame drop／duplication
- audio resampling／stretch
- drift accumulation correction
- precise multimedia timer
- 完全なA/V同期
- FFmpeg backend activation、demux、codec wiring
- GUI、window message loop、ユーザー向け操作画面
- default-device監視と自動復旧
- NovaによるFFmpeg runtime同梱／再配布
- license reviewの完了
- 一般利用向けreal-media playbackの完了宣言

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
- 所有権を明示し、借用したresourceをdisposeしない
- 成功した処理だけをconsume／commitする
- undefined enumや不正値はfail closed
- resetの途中失敗を成功として隠さない
- lower boundaryのpointer、handle、HRESULTをpublic contractへ漏らさない
- 実装していない機能を完了扱いしない

## コントリビューション

プロジェクトは段階的に開発中です。変更は小さなmilestoneとADR、テスト、validation文書をセットにして進めます。

IssueやPull Requestを作成する際は、変更範囲、所有権、有限性、失敗時の状態、検証方法を明記してください。

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。
