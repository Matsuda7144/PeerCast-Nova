# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestones 0.0.10d／0.0.10e — Complete**

PeerCast Nova は、exact approved external FFmpeg runtime、controlled native decode、Windows video／audio output validation、shared Windows application ownership、legal／About projection、framework-dependent `win-x64` portable packaging boundaryまで実装・検証済みです。

### 0.0.10d — Controlled Physical Windows Video／Audio Output Validation

- active FFmpeg build ID: `btbn.ffmpeg.20260721.win64.lgpl-shared`
- controlled native decode: H.264 `10` packets／frames、AAC `48` packets／frames、audio samples `49,152`、decode errors `0`、complete cleanup
- controlled fixture video: `Completed／Accepted`、independent preflight `VideoAccepted`、operator observation `Video visible: Yes`
- final accepted audio run: exact main `6eb3558ec0efd434d9ee1e7221ba86d3c115684f`
- audio result: canonical `Completed／Accepted`、delivery `4／4`、strict 26-line record
- completed audio record SHA-256: `C6CD53C053F99FF43A1514E1162D3C90FE307C5FE32285F73013A612C2FBD100`
- independent completed-record preflight: `RenderEvidenceRecordValidated`
- same-run operator observation: `Audio audible: Yes`
- recognizable retained-fixture beep: `Yes`
- final independent paired video／audio admission: completed on merged main `4f38de92ce66d988479f5e22b263de9f84b32086`
- Issue #129: closed as `completed`

Historical contradictory and rejected runs remain preserved as historical evidence and are not reinterpreted as successful. Their authorizations and paths are consumed and must not be reused.

Current-state reviews:

- [PCN-0166 Controlled Audio Render Authorization State](docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md)
- [PCN-0167 Managed Audio Render Failure Boundary](docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md)
- [Issue #129](https://github.com/Matsuda7144/PeerCast-Nova/issues/129)

### 0.0.10e — Shared Windows Application Ownership and Portable Packaging Boundary

- `Nova.Windows` is the sole owner of the Win32 window、HWND lifetime、message loop、owner thread、bounded dispatcher、shutdown、and native cleanup
- `Nova.App` provides finite startup／close／cancel shutdown above one shared host
- `Nova.App` projects the reviewed bounded FFmpeg legal／About notice and provenance material
- `Nova.Playback` borrows only the dispatcher contract and window token
- `Nova.Cli` continues to use the same shared host for explicitly selected controlled commands
- framework-dependent `win-x64` portable packaging is the only implemented application distribution boundary
- FFmpeg remains external; no binary、archive、runtime pack、acquisition、installation、update、or invocation path is bundled
- installer、code signing、and updater mechanics remain separate future ADR decisions

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
- native decoder context、parameter transfer、codec open、packet／frame境界
- pure managed bounded FLV v1 parserとexact session decoder-parameter binding
- managed frame copy、conversion、awaited delivery
- count／byte bounded decoded-frame scheduler
- finite late-aware scheduler dispatchとgeneration-matched output lifecycle
- Windows D3D11／WASAPI activation、binding、stream、buffer、device-loss境界
- controlled real external-runtime H.264／AAC decode validation
- accepted visible controlled fixture video
- completed／terminal／failure audio evidence、strict offline preflight、readiness-required one-run authorization boundary
- completed audio evidenceとindependent paired video／audio admission

### Windows application boundary

- shared `Nova.Windows` application window／message-loop／bounded-dispatcher ownership assembly
- minimal `Nova.App` startup composition with finite close／cancel shutdown
- bounded legal／About projection
- framework-dependent `win-x64` portable packaging contract
- exact reviewed legal-file allowlist

## まだ完了していないもの

PeerCast Nova は現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- user-facing GUI and application features
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、and drift correction
- recorder
- installer
- code signing
- updater

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次の開発候補

0.0.10d／0.0.10eは完了済みです。次の開発スライスはまだ自動選択されていません。

候補:

- user-facing application機能
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- A/V synchronization／resampling／drift correction
- installer境界の別ADR／設計
- code-signing境界の別ADR／設計
- updater境界の別ADR／設計

All readiness and physical-run authorizations used to establish 0.0.10d are consumed. Historical helpers／diagnostics and consumed paths must not be reused. Any future physical run remains a separate reviewed authorization decision.

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
- LGPL replacement freedomとruntime integrityを明示manifestで両立する
- exact built-in candidateだけをNova review approvalとし、host attestationをapprovalへ読み替えない
- Windows native resourceはowner threadで所有し、production decode pathはdispatcher-backed portだけを借用する
- output generationはactive decoder generationと一致させる
- device lossは旧generation revivalなしのterminal lifecycle resultとして扱う
- controlled fixture validationをphysical native activationの証拠へ読み替えない
- readiness evidenceをphysical authorizationへ読み替えない
- consumed one-run authorizationを再利用しない

## 詳細資料

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [Architecture Decision Records](docs/adr)
- [Implementation and validation reviews](docs/reviews)
- [FFmpeg external runtime compliance policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md)
- [FFmpeg distribution legal material](THIRD-PARTY-LICENSES/FFmpeg)

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。built-in candidateのNova reviewはexact identityだけに限定され、host-supplied runtimeはhost declaration／attestationでありNova approvalではありません。
