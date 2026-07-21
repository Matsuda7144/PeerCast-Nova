# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、PeerCast の思想を受け継ぎながら、現代的な設計と安全な境界管理で再構築するオープンソースの P2P ライブ配信プラットフォームです。

Project Founder: **7144**  
License: **Apache License 2.0**

## 現在地

**Milestone 0.0.9v — FFmpeg Candidate License, Backend Registration, and Distribution Review**

0.0.9uで完成したproduction compositionを実際のFFmpeg runtimeへ接続する前に、pinned BtbN `win64-lgpl-shared` candidateのlicense class、配布、利用者による差し替え、production登録位置をレビューしました。

- reviewed candidateはLGPLv3 shared-library build class
- GPL variant／nonfree variantは対象外で、引き続き拒否
- Novaの既定配布物にはFFmpeg executable／DLL／archiveを一切含めない
- NovaはFFmpegをmirror／rehost／download／extract／install／updateしない
- user／application hostが一つのexplicit runtime directoryを供給
- `PATH`、registry、system directory、conventional locationを探索しない
- shared DLLのfilename、SHA-256、ABI major、required symbolsをfail closedで検証
- built-in fixed manifestだけを唯一のruntime identityにはしない
- 利用者がinterface-compatible modified LGPL buildを明示承認できるhost-owned manifestを次に実装
- checksum検証を無効化せず、差し替え自由とsupply-chain integrityを両立
- production FFmpegはgeneric `PlaybackDecoderBackendFactory`へ登録しない
- application composition rootがpolicy、manifest、runtime directory、source generation、borrowed Windows outputを明示して構築
- bundling／binary rehostingは別ADRとcomplete corresponding-source compliance archiveなしでは禁止
- H.264／AAC等のpatent clearanceはこのcopyright-license reviewの対象外

この構成は法的に採用可能なarchitecture classとして確定しました。ただし、GPLv3／LGPLv3本文、third-party notice、利用者向けreplacement手順、host-manifest validationがまだruntimeへ実装されていないため、pinned candidateは引き続き`LicenseReview.Pending`です。

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
- source-bound one-frame managed receive and independent EOS drain
- finite awaited PCP／FLV／decode composition boundary
- exact-active one-shot decoded completion detach
- generation-wide scheduler ingress ownership boundary
- finite generation-matched scheduler delivery／output lifecycle boundary
- production generation factory／activation architecture review
- policy-gated production decoder generation factory boundary
- production generation ingress／replacement composition architecture review
- finite production generation ingress／replacement composition boundary
- FFmpeg candidate license／registration／distribution architecture review
- external-only LGPL runtime compliance policy

詳細:

- [CHANGELOG.md](CHANGELOG.md)
- [NAVIGATION.md](NAVIGATION.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/adr](docs/adr)
- [docs/reviews](docs/reviews)
- [FFmpeg external runtime compliance policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md)

## まだ完了していないもの

PeerCast Novaは現在、一般利用向けの完成版プレイヤー／配信アプリではありません。

- external LGPL runtime compliance and user-replacement boundary
- GPLv3／LGPLv3 license text and third-party notice packaging
- pinned candidate `LicenseReview.Approved` transition
- approved production decoder ingress activation
- Windows application／window／device lifecycle composition
- controlled real-media decode and output validation
- MP4／fragmented MP4 parser
- decoded-buffer duration cutoff
- discontinuity-time native reset semantics
- complete A/V synchronization、resampling、drift correction
- controlled audible／visible real-media playback
- GUI、window message loop、installer、recorder

FFmpeg binary bundling／redistributionは既定方針として禁止です。別runtime packを将来検討する場合は、新しいADRとexact corresponding source／build／notice compliance archiveが必要です。

## 次のマイルストーン

**0.0.9w — External LGPL Runtime Compliance and User Replacement Boundary**

bounded built-in／host runtime manifestを実装し、`LGPL-3.0-or-later`、exact DLL role／filename／SHA-256／ABI major／required symbolsを検証します。GPLv3／LGPLv3本文、third-party notice、replacement手順を配布物へ追加し、利用者がinterface-compatible modified buildを自分のmanifestで承認できることをtestします。これらが通過した後だけpinned candidateを`LicenseReview.Approved`へ変更します。

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

## ライセンス

PeerCast Novaは[Apache License 2.0](LICENSE)で公開されています。FFmpegはNovaのApache License 2.0には含まれず、外部runtimeとして別のlicense termsに従います。
