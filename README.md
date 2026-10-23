# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、既存 PeerCast ネットワークとの互換性を保ちながら、P2P ライブ配信の視聴とリレーをシンプルに行えるソフトウェアを目指すオープンソースプロジェクトです。

Project Founder: **7144**  
License: **Apache License 2.0**

## プロジェクトの方針

| 方針 | 内容 |
| --- | --- |
| P2P First | 中央サービスを必須にせず、既存のピアネットワーク上で受信・リレーできることを優先します。 |
| Simplicity / Lightweight | MVP に必要な機能と依存関係に絞り、使い方と障害の追跡を簡単にします。 |
| Compatibility | PeerCastStation との互換性を最優先し、MVP では Nova 固有の必須 wire protocol を導入しません。 |
| Open / Community | Apache-2.0 を採用し、公開可能な protocol／設計資料を整えて、コミュニティが参加しやすい形を目指します。 |
| Transparency | 実装済み、検証済み、未実装を区別し、継続的な設計判断を ADR と記録に残します。 |
| Core / UI Separation | ネットワークや制御のコアを UI から独立させ、別の host からも利用できる境界を保ちます。 |

## MVP の確定状態 (Version 0.1.0)

PeerCast Nova Version 0.1.0 の MVP は完成し、main に統合済みです。受入済みの MVP は既存 PeerCast／PeerCastStation ネットワーク上でチャンネルを再生し、選択した配信を他の peer へ relay すること、ならびにチャンネル選択、開始・停止、接続状態と利用者が対処できるエラーを扱うことです。

- MVP PLAYBACK: ESTABLISHED / INTEGRATED INTO MAIN
- MVP RELAY: ESTABLISHED / INTEGRATED INTO MAIN
- MVP OVERALL: ESTABLISHED / INTEGRATED INTO MAIN
- PEERCAST NOVA MVP IMPLEMENTATION: COMPLETE

main `8caf7069c54c879fd1a7eb2683ab214a337aa883` に PR #245/#246 が landing 済みです。resulting-main CI run `36020495159` / job `107703845407` は Restore、Build、Test、Format がすべて SUCCESS で、実機受入も完了しています。CI と実機受入は独立した証拠です。

採用済みの再生経路は、PCP 入力を loopback 限定の HTTP-FLV endpoint 経由で PCRPlayer／DSPlayer などの対応 external player に handoff する経路です。この external-player playback path が MVP の再生要件を満たします。Relay 操作は選択チャンネルの bounded PCP relay listener を開始し、LAN peer に共有する endpoint を表示します。

Nova 内の managed decoder、scheduler、Windows output を使う playback の追加経路、managed 経路による regular MP4 production playback、Windows output の拡張、ならびに将来の productization／post-MVP hardening は MVP 完了後の作業です。これらは完成済み MVP の blocker ではありません。実装履歴と追加範囲は [ROADMAP.md](ROADMAP.md) を参照してください。

## 現在の再生経路

通常配信の開発方針はOBSでキャプチャ・合成・H.264/AACエンコードを行い、Novaはingest / FLV / PCP publish / relay / YPを担当する構成です。[ADR-0491](docs/adr/ADR-0491-loopback-OBS-RTMP-FLV-ingest-foundation.md) と [OBS ingest foundation](docs/obs-flv-ingest-foundation.md) はloopback限定のライブラリ基盤を記録しています。アプリの配信開始操作、YP、実OBSと物理A/Vの受入は後続sliceです。

[Headless publish channel foundation](docs/headless-obs-publish-channel.md) は同じchannel generationを既存relayとHTTP-FLVへ接続します。managed localhost roundtripまでが検証範囲で、実OBS相互運用は未検証です。

MVP の再生要件を満たす採用済み経路は external-player handoff です。PCRPlayer と DSPlayer を含む対応 player に PCP 入力を loopback 限定の HTTP-FLV で渡します。現在の `Nova.App` で PCRPlayer を使う場合は、実行ファイルを次の順で解決します。

1. `--pcrplayer-executable <path>`
2. `NOVA_PCRPLAYER_EXE`
3. PATH 上の `PCRPlayer64.exe`

明示した実行ファイルが無効な場合はエラーを表示し、別の player へ自動で切り替えません。

## 公開 Relay のポート自動開放

PCP downstream relay listener の IPv4 TCP ポートを、NAT-PMP 対応ルーターへ
自動で開放・更新・閉鎖する foundation を追加しています。既定は OFF です。
日常アプリの「ポート自動開放 (ON / OFF)」で切り替えます。設定は
`%LOCALAPPDATA%\PeerCast\Nova\settings.json` に保存します（未設定は OFF）。
起動時に `NOVA_AUTO_PORT_MAPPING` が指定されている場合は、その `on` / `off` を
保存設定より優先します。起動後の GUI 変更は現在の desired state と保存設定へ反映します。

~~~powershell
$env:NOVA_AUTO_PORT_MAPPING = "on"
dotnet run --project src/Nova.App
~~~

公開 listener が実際に bind したポートを対象とし、最後の公開要求がなくなると
削除を要求します。loopback の HTTP-FLV 再生用 listener は対象外です。
最初の backend は NAT-PMP で、複数 adapter など経路が曖昧な場合は自動選択しません。
ルーターの mapping 応答とインターネット側からの到達確認は別の状態です。
GUI にルーター設定・外部接続の別表示、lease の証拠、診断コード付き要約を追加しています。
実ルーターで NAT-PMP の Map/Renew/Unmap と restart 後の Map を確認済みです。
外部到達は `NotChecked`、GUI の実画面 physical acceptance は GUI 操作環境の制約で未完です。
UPnP、IPv6、外部アドレス取得・到達確認は後続 slice に残ります。

管理境界・競合テスト・cleanup の証拠は
[NAT Port Mapping Foundation](docs/architecture/NAT-PORT-MAPPING-FOUNDATION.md) を参照してください。

## Post-MVP の追加範囲

- `Nova.App` から managed decoder、scheduler、Windows output までを結ぶ追加 playback 経路と、managed 経路による regular MP4 production playback。
- Windows output の拡張、完全な A/V synchronization、resampling、drift correction と幅広い codec support。
- recorder、advanced GUI、installer、code signing、updater、将来の productization と post-MVP hardening。

詳細な scope は [ROADMAP.md](ROADMAP.md) を参照してください。

## 必要環境とビルド

- .NET 10 SDK
- 日常アプリを起動する場合は Windows
- 再生には PCRPlayer64、DSPlayer、または利用者が指定する対応 player が必要です。

~~~powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
~~~

Windows アプリは次のように起動します。

~~~powershell
dotnet run --project src/Nova.App
dotnet run --project src/Nova.App -- --pcrplayer-executable "C:\path\PCRPlayer64.exe"
~~~

## CI と検証の境界

GitHub Actions が main への push と Pull Request で自動実行され、restore、Release build、full solution tests、format verification を行います。CircleCI は GitHub Actions が開始できない場合に限る手動 fallback です。PR の判断には対象 SHA の結果を確認してください。

CI は managed build／tests を検証します。Windows 上の native device output や人による映像・音声の確認は routine CI の結果から推定できません。詳細は [NAVIGATION.md](NAVIGATION.md) と [ROADMAP.md](ROADMAP.md) を参照してください。

## 構成と詳細資料

- [システムアーキテクチャ](docs/architecture/PCN-0004-SYSTEM-ARCHITECTURE.md)
- [Solution と project 構成](docs/architecture/PCN-0005-SOLUTION-STRUCTURE.md)
- [Control plane と data plane](docs/architecture/CONTROL-AND-DATA-PLANES.md)
- [PeerCastStation 互換性計画](docs/protocol/PCP-COMPATIBILITY-PLAN.md)
- [Protocol と Relay の検証記録](docs/protocol/PCP-RELAY-COMPATIBILITY.md)
- [設計判断 ADR](docs/adr)
- [実装・検証レビュー](docs/reviews)
- [CHANGELOG.md](CHANGELOG.md)
- [ROADMAP.md](ROADMAP.md)
- [NAVIGATION.md](NAVIGATION.md)

現在の managed playback 技術境界: [ADR-0489](docs/adr/ADR-0489-bounded-iso-bmff-regular-movie-native-decoder-discontinuity-reset.md) ／ [PCN-0499](docs/reviews/PCN-0499-BOUNDED-ISO-BMFF-REGULAR-MOVIE-NATIVE-DECODER-DISCONTINUITY-RESET.md)。これは post-MVP の追加 playback 経路に関する bounded slice であり、採用済み MVP 経路の範囲を変更したり、regular MP4 の一般向け再生全体を主張したりするものではありません。

録画Coreは、[ADR-0490](docs/adr/ADR-0490-shared-manual-and-automatic-recording-runtime.md) ／ [PCN-0500](docs/reviews/PCN-0500-SHARED-MANUAL-AND-AUTOMATIC-RECORDING-RUNTIME.md) に記録したGUI非依存の手動・自動録画ランタイムを基盤にしています。PR #242のcloseoutでは managed 検証に加え、**END-USER RECORDING ACCEPTANCE — PROVEN** と **FLV TIMESTAMP NORMALIZATION — PHYSICAL ACCEPTANCE PROVEN** を確認しました。Run-IDと実行時のコードHEADは[PR #242](https://github.com/Matsuda7144/PeerCast-Nova/pull/242)に記録しています。GUI録画操作とルール編集は別作業の[PR #230](https://github.com/Matsuda7144/PeerCast-Nova/pull/230)に残ります。

## ライセンス

PeerCast Nova のコードは [Apache License 2.0](LICENSE) の下で公開されています。FFmpeg は別ライセンスの外部 runtime であり、既定の Nova 配布物には FFmpeg binary を含めません。runtime の境界と法務資料は [FFmpeg External Runtime Compliance Policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md) を参照してください。
