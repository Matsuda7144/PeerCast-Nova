# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.10e — Shared Windows Application Ownership Boundary — Complete**

0.0.10a〜0.0.10cを経て、exact approved external FFmpeg runtime、controlled native decode、Windows output validation architectureは実装・検証済みです。

- active build ID `btbn.ffmpeg.20260721.win64.lgpl-shared`
- 0.0.10b controlled native decode: H.264 `10` packets／frames、AAC `48` packets／frames、audio samples `49,152`、decode errors `0`、complete cleanup
- 0.0.10c controlled Windows video／audio output validation review: complete
- corrected D3D11 texture-copy／production upload probes: visible `Yes`
- corrected controlled fixture video: `Completed／Accepted`、`VideoAccepted`、`Video visible: Yes`
- known 80 ms managed tone and decoded 80 ms fixture slice: recognizable audio
- ordinary controlled fixture audio: seven consumed readiness-gated runs produced a recognizable beep with `Audio audible: Yes`
- the latest run at exact main `6eb3558ec0efd434d9ee1e7221ba86d3c115684f` produced canonical `Completed / Accepted` audio render evidence, passed its independent completed-record preflight, and had the same-run operator observations `Audio audible: Yes` and recognizable retained-fixture beep `Yes`
- final merged-main paired video/audio admission and milestone reconciliation remain pending
- Issue #129 remains open
- 0.0.10d is explicitly deferred without claiming physical completion, formal audio acceptance, paired admission, or milestone completion
- the existing application-owned Win32 host is extracted into `Nova.Windows` so the user-facing application can reuse one window／message-loop／dispatcher ownership model
- `Nova.App` provides finite startup／shutdown, bounded legal／About projection, and a framework-dependent `win-x64` portable package
- installer, signing, and update mechanics remain unselected behind a separate reviewed boundary

The one authorized direct render-evidence helper invocation at main `4abadd280ceeae310b2528b6ea62f377211b1273` returned a bounded unexpected helper failure, retained neither requested record, and had operator observation `Audio audible: No`. That authorization is consumed and the exact trigger cannot be reconstructed.

A later separately authorized run at exact main `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d` ended with child exit `1`, `DeliveryRejected`, and terminal rejection `PreCleanupCheckpointRejected`. It retained no canonical completed or terminal record, but the operator separately reported a recognizable beep and `Audio audible: Yes`.

The separately authorized run at exact main `77a947f98e7a9449523b49a39bc8b4e8d7ed430b` also ended with child exit `1` and `DeliveryRejected`, but the merged granular gate identified `PreCleanupTraceCardinalityRejected`. Static source review established a two-stage terminal-evidence retention asymmetry: production terminal cleanup cleared the active decoder generation before returning to the lifecycle, and later lifecycle cleanup cleared the production owner before the terminal snapshot. The independently retained buffer/native audio trace remained available, so the rejection did not prove an extra native write. The run produced no canonical completed or terminal record and again had operator observation `Audio audible: Yes` for a recognizable beep.

PR #201 merged at main `7e31627151f5a6dba7fb9e3f0b03cd08be3b14cf` and retains the latest immutable scheduler delivery trace across both ownership-cleanup boundaries. Live generation／production traces remain preferred, retained values are used only after the corresponding owner is unavailable, and new owners reset the retained state. The change is non-physical and does not alter delivery、timing、endpoint、buffer、sample、native write、or cleanup behavior.

The latest separately authorized run at exact main `774342a03c33713b1b7f70e2cb664397c04f8d04` ended with child exit `1`, stdout／stderr byte counts `0／3122`, and final native stage `ActivationCleanupCompleted`. It produced neither canonical completed nor terminal evidence and retained a valid version 5 sanitized child failure record with SHA-256 `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`. The operator again reported a recognizable beep and `Audio audible: Yes`. The retained record cannot prove the private stderr classification, so the exact historical managed exception source remains unestablished.

PR #203 merged as main `e265a2b47731214a2f26a0b4a4227721158490b2` and prospectively bounds unexpected direct managed-command failures. When no canonical outcome has already been written, it emits one exact `Result: Faulted` line for the sanitized child-failure codec, uses the existing `ProjectionRejected` category for snapshot／terminal-projection failures, and publishes no raw exception or private process detail. It changes no physical-output behavior.

PR #205 merged as main `46a9ff163e8b0fb4582175d4ff1d56cd753fdeb9` and adds a strict 22-line child failure record version 6. The real child-capture path can retain exactly one allowlisted `Managed-Failure-Phase`; unknown, numeric, extended, or duplicate markers collapse to `None`. Versions 2 through 5 remain readable with effective phase `None`, and legacy v5 serializer/preflight report shape remains compatible.

The later separately authorized run at exact main `847b0ea4cff8aca48c103e499c0bf2bca3fbc6b5` again produced a recognizable beep with `Audio audible: Yes`, but ended `DeliveryRejected / PreCleanupTraceCardinalityRejected` with no canonical completed or terminal record. Static tracing found that the dispatcher-backed Windows generation path omitted the already-selected `CaptureDeliveryObservations` option, so native audio writes were retained while its scheduler delivery trace remained empty. PR #213 merged as main `a6ec858162b1170cc35a5bbc94ae4068f4335090` and forwards that option without changing delivery, timing, native writes, cleanup, readiness, or authorization behavior.

The next separately authorized run at exact main `abb071f5fcb6e78f86ad5f185387adee060e29fb` again produced a recognizable beep with `Audio audible: Yes`. It ended with child exit `1`, exact fallback `Result: Faulted`, final native stage `ActivationCleanupCompleted`, and managed failure phase `OutcomeWrite`; no canonical completed or terminal record was produced. Static review established that the fixed four-slot scheduler trace recorded both video and audio reservations even though the controlled record contract requires four audio observations. With the mixed H.264/AAC fixture, video delivery could consume or overflow the audio evidence slots before the four native audio writes. PR #215 merged as main `94878943dfbd7df2c059bf5f347bcfbf0f24f272` and restricts the opt-in trace to audio observations without changing delivery, timing, native writes, cleanup, readiness, or authorization behavior.

The later separately authorized run at exact main `e0809e9b711d5bb5101130a26ca19944a94a0d31` produced a recognizable beep with `Audio audible: Yes` and retained a canonical terminal record with SHA-256 `859651D2DED3417F4F55BBAD1E210D1B48AC9E274681D669D6CF1C516E851C78`. It ended `DeliveryRejected / None` after two acknowledged native writes. The third audio sequence first requested an approximately `6 ms` timing wait, but the controlled wrapper delayed by the fixed `21.3334 ms` AAC cadence and the resumed reservation became `AudioLate`. Static review also found that retained-wait and resumed observations for one sequence could consume multiple slots in the four-sequence completed-evidence trace. ADR-0165 and PCN-0175 define the non-physical correction: use only a validated bounded observed wait for the opt-in controlled evidence path and coalesce same-sequence trace updates without changing production timing, native writes, buffers, or cleanup.

The next separately authorized run at exact main `6eb3558ec0efd434d9ee1e7221ba86d3c115684f` completed four deliveries and retained a strict 26-line `Completed / Accepted` audio render-evidence record with SHA-256 `C6CD53C053F99FF43A1514E1162D3C90FE307C5FE32285F73013A612C2FBD100`. Its independent completed-record preflight returned `RenderEvidenceRecordValidated`, and the operator separately reported `Audio audible: Yes` and a recognizable retained-fixture beep. The readiness and one-run physical authorizations are consumed.

ADR-0166 and PCN-0176 connect that strict completed record directly to the independent paired run-set verifier while preserving legacy audio-record compatibility. The connector is non-physical and creates no synthetic legacy record. Final paired admission must use the immutable retained records with a build from the final merged main.

Prospective non-physical hardening is implemented: bounded scheduler／WASAPI traces, completed／terminal／failure records, offline strict preflights, readiness validation, atomic readiness capture, the readiness-required one-run authorization entrypoint, bounded controlled-run result and terminal-rejection retention, exact stream-format validation, bounded readiness failure disclosure, granular pre-cleanup rejection diagnostics, terminal scheduler-trace retention, dispatcher-backed delivery-trace capture, audio-only and same-sequence trace selection, bounded observed timing resume, managed command failure bounding, and managed failure phase retention. No new physical run is currently authorized.

Current-state review:

- [PCN-0166 Controlled Audio Render Authorization State](docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md)
- [PCN-0167 Managed Audio Render Failure Boundary](docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md)
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
- shared `Nova.Windows` application window／message-loop／bounded-dispatcher ownership assembly
- minimal `Nova.App` startup composition with one shared host and finite close/cancel shutdown

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

- paired video／audio admission and completion of 0.0.10d
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- GUI、installer、recorder

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次のゲート

**After 0.0.10e — Deferred physical gate and future release mechanics**

0.0.10e is complete as an application ownership and portable packaging boundary. `Nova.App` provides the normal user-facing start/close/cancel boundary above the single shared `Nova.Windows` host, projects the exact bounded FFmpeg notice/provenance/replacement material, and has a framework-dependent `win-x64` portable publish contract with the exact reviewed legal-file allowlist. Installer, signing, and update mechanisms remain separate future ADR decisions.

0.0.10d is explicitly deferred, not completed. Its evidence gaps and Issue #129 remain open. The completed 0.0.10e work adds no FFmpeg binary, acquisition, installation, update, or invocation behavior and does not change controlled physical commands.

新しい物理実行を検討する場合は、次をすべて満たす必要があります。

1. separately merged exact `main` commit
2. final-head／post-merge CircleCI success
3. exact commitとoperation boundsに対する新しいcanonical readiness record
4. new repository-external completed／terminal／failure paths
5. new explicit one-run authorization
6. `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1`からexactly once実行

The underlying helper must not be invoked directly. The literal authorization statement required by the entrypoint is an accidental-invocation guard and does not itself grant authorization.

Until a new explicit authorization exists, historical helpers／diagnostics must not be rerun and deferred 0.0.10d must not be marked complete.

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
