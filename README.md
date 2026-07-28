# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.10d — Controlled Physical Windows Video／Audio Output Validation — In Progress**

0.0.10a〜0.0.10cを経て、exact approved external FFmpeg runtime、controlled native decode、Windows output validation architectureは実装・検証済みです。

- active build ID `btbn.ffmpeg.20260721.win64.lgpl-shared`
- 0.0.10b controlled native decode: H.264 `10` packets／frames、AAC `48` packets／frames、audio samples `49,152`、decode errors `0`、complete cleanup
- 0.0.10c controlled Windows video／audio output validation review: complete
- corrected D3D11 texture-copy／production upload probes: visible `Yes`
- corrected controlled fixture video: `Completed／Accepted`、`VideoAccepted`、`Video visible: Yes`
- known 80 ms managed tone and decoded 80 ms fixture slice: recognizable audio
- ordinary controlled fixture audio: two consumed readiness-gated runs produced a recognizable beep with `Audio audible: Yes`
- stable deterministic delivery、canonical completed audio evidence、formal audio acceptance、and paired video／audio admission remain unestablished
- Issue #129 remains open

The one authorized direct render-evidence helper invocation at main `4abadd280ceeae310b2528b6ea62f377211b1273` returned a bounded unexpected helper failure, retained neither requested record, and had operator observation `Audio audible: No`. That authorization is consumed and the exact trigger cannot be reconstructed.

A later separately authorized run at exact main `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d` ended with child exit `1`, `DeliveryRejected`, and terminal rejection `PreCleanupCheckpointRejected`. It retained no canonical completed or terminal record, but the operator separately reported a recognizable beep and `Audio audible: Yes`.

The latest separately authorized run at exact main `77a947f98e7a9449523b49a39bc8b4e8d7ed430b` also ended with child exit `1` and `DeliveryRejected`, but the merged granular gate identified `PreCleanupTraceCardinalityRejected`. Static source review established that the lifecycle maps `DeliveryRejected` to `OutputRejected`, clears and stops production before the terminal snapshot, and thereby loses the current-generation scheduler trace while the retained buffer/native audio trace remains available. The rejection is therefore a terminal-evidence retention asymmetry, not proof of an extra native write. The run produced no canonical completed or terminal record and again had operator observation `Audio audible: Yes` for a recognizable beep. Both authorizations are consumed; physical completion and formal audio acceptance remain unestablished.

Prospective non-physical hardening is implemented: bounded scheduler／WASAPI traces, completed／terminal／failure records, offline strict preflights, readiness validation, atomic readiness capture, the readiness-required one-run authorization entrypoint, bounded controlled-run result and terminal-rejection retention, exact stream-format validation, bounded readiness failure disclosure, and granular pre-cleanup rejection diagnostics. No new physical run is currently authorized.

Current-state review:

- [PCN-0166 Controlled Audio Render Authorization State](docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md)
- [Issue #129](https://github.com/Matsuda7144/PeerCast-Nova/issues/129)

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
- Windows D3D11／WASAPI probe、activation、binding、stream、buffer境界
- bounded demux output contract
- pure managed bounded FLV v1 parser
- exact FLV session decoder-parameter binding
- source-bound opened native decoder session
- metadata-aware native packet／flush boundary
- finite one-event packet submission state machine
- controlled retained FLV fixture validation
- one-event final EOS parser façade
- finite awaited PCP-to-FLV composition boundary
- source-bound one-frame managed receive and independent EOS drain
- finite awaited PCP／FLV／decode composition boundary
- exact-active one-shot decoded completion detach
- generation-wide scheduler ingress ownership boundary
- finite generation-matched scheduler delivery／output lifecycle boundary
- policy-gated production decoder generation factory boundary
- finite production generation ingress／replacement composition boundary
- FFmpeg candidate license／registration／distribution review
- external-only LGPL runtime compliance policy
- project-reviewed built-in／host-declared host-attested unreviewed FFmpeg runtime manifest
- checksum-preserving LGPL user replacement boundary
- built-in-approved／explicit-host-attested application-composition-root entry
- readable GPLv3／LGPLv3 notice, provenance, replacement, and release-terms guard packaging
- approved runtime／Windows lifecycle activation architecture review
- explicit application owner-thread dispatcher contract
- dispatcher-backed production output process／reset path
- finite approved-runtime Windows lifecycle activation boundary
- generation-matched initial／replacement output configuration
- explicit retained resize and terminal device-loss teardown
- controlled retained-fixture production lifecycle integration validation
- approved 2026-07-21 FFmpeg candidate manifest and superseded-candidate history
- complete candidate inventory／buildconf／license text evidence
- controlled real external-runtime native H.264／AAC decode validation
- controlled application-owned Windows runner and finite physical-run composition
- accepted visible controlled fixture video
- bounded controlled audio render evidence, terminal evidence, failure retention, offline review, and readiness-required authorization boundaries

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)
- [FFmpeg external runtime compliance policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md)
- [FFmpeg distribution legal material](THIRD-PARTY-LICENSES/FFmpeg)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- non-physical retention correction for the scheduler delivery trace before production cleanup
- stable controlled ordinary audio delivery evidence
- canonical completed／accepted audio record from the same run as a recognizable audible observation
- independent completed-record preflight for that run
- paired video／audio admission and completion of 0.0.10d
- user-facing Windows application window／message-loop composition
- application-provided dispatcher implementation for the final app surface
- legal／about surfaceからのFFmpeg notice表示
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- GUI、installer、recorder

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次のゲート

**0.0.10d — Retain terminal scheduler trace before production cleanup**

`DeliveryRejected`時のlifecycle cleanupは、terminal snapshotより先にproduction ownerを外すため、generation-local scheduler traceだけを失います。次は最後のimmutable delivery traceをproduction cleanup前に保持し、buffer/native traceと同じterminal snapshotへ投影する非物理修正とfocused testsです。delivery、timing、endpoint、buffer、sample、audio output behaviorは変更しません。readiness capture、physical output、probe、diagnosticは自動実行しません。

その後に新しい物理実行を検討する場合は、次をすべて満たす必要があります。

1. separately merged exact `main` commit
2. final-head／post-merge CircleCI success
3. exact commitとoperation boundsに対する新しいcanonical readiness record
4. new repository-external completed／terminal／failure paths
5. new explicit one-run authorization
6. `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1`からexactly once実行

The underlying helper must not be invoked directly. The literal authorization statement required by the entrypoint is an accidental-invocation guard and does not itself grant authorization.

Until a new explicit authorization exists, historical helpers／diagnostics must not be rerun and 0.0.10d must not be marked complete.

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
- Windows native resourceはowner threadで所有しproduction decode pathはdispatcher-backed portだけを借用する
- output generationはactive decoder generationと一致させる
- device lossは旧generation revivalなしのterminal lifecycle resultとして扱う
- controlled fixture validationをphysical native activationの証拠へ読み替えない
- readiness evidenceをphysical authorizationへ読み替えない
- consumed one-run authorizationを再利用しない

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。built-in candidateのNova reviewはexact identityだけに限定され、host-supplied runtimeはhost declaration／attestationでありNova approvalではありません。
