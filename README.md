# PeerCast Nova

> **Code for Community. Design for the Future.**

PeerCast Nova は、既存の PeerCast / PeerCastStation ネットワークとの互換性を保ちながら、P2P ライブ配信の視聴・リレー・録画・配信をシンプルに行えるオープンソースソフトウェアです。

- **License**: [Apache License 2.0](LICENSE)

---

## 主な機能（v0.2.0）

- **チャンネル視聴とリレー**
  - Yellow Pages 一覧表示・検索・お気に入り管理・通知設定、PCP によるリレー配信。
- **内蔵プレイヤー（`libmpv`）と外部プレイヤー連携**
  - 内蔵 `libmpv` 再生（初回自動セットアップ対応、再生中の音声出力デバイス切替、スクリーンショット・低遅延再生対応）。
  - 内蔵プレイヤー未導入時や設定に応じて外部プレイヤー（`PCRPlayer64.exe` / `DSPlayer` / 任意のプレイヤー＋カスタム引数テンプレート `NOVA_PCRPLAYER_ARGS`）を利用可能。詳細手順は [内蔵 FLV プレイヤーのセットアップ（Windows x64）](docs/internal-flv-player-setup-ja.md) を参照してください。
- **手動・自動録画と録画ルール**
  - FLV タイムスタンプ正規化対応の手動／ルールベース自動録画、録画履歴およびルール編集・並び替え UI。
- **OBS 配信取り込みと公開支援**
  - ループバック RTMP → FLV → PCP 配信ホスト、YP アナウンス、NAT-PMP によるポート自動開放および WAN 接続診断支援。
- **日本語 UI と高 DPI 対応**
  - `PerMonitorV2` DPI スケーリング、メイン画面の一覧／詳細スプリッター高さ復元、日本語設定ダイアログ。

---

## プロジェクトの方針

| 方針 | 内容 |
| --- | --- |
| P2P First | 中央サービスに依存せず、既存のピアネットワーク上で受信・リレーできることを優先します。 |
| Simplicity / Lightweight | 必要な機能と依存関係に絞り、使い方と障害追跡をシンプルにします。 |
| Compatibility | PeerCastStation との互換性を重視し、独自の必須 wire protocol を導入しません。 |
| Open / Community | Apache-2.0 の下で設計資料・検証記録を公開し、コミュニティが参加しやすい形を保ちます。 |
| Core / UI Separation | ネットワーク・配信・録画コアを UI から分離し、独立したテスト可能性を維持します。 |

---

## 設定・ログ・データの保存先

実行時の設定ファイルや状態データ・ログは、原則として以下に保存されます。

- **メインデータディレクトリ**: `%LOCALAPPDATA%\PeerCast Nova\`
  - 一般／ネットワーク／再生設定: `settings.json`
    - 旧バージョンで使用していた `%LOCALAPPDATA%\PeerCast\Nova\settings.json` が存在する場合は、初回読み込み時に自動でフォールバック読み込みし、設定保存時に `%LOCALAPPDATA%\PeerCast Nova\settings.json` へ移行します。
  - お気に入り・通知・YP・接続・再生・配信プリファレンス・ウィンドウ配置・録画履歴／ルール等も同じディレクトリに保存されます。
  - アプリケーションログ: `%LOCALAPPDATA%\PeerCast Nova\logs\nova.log`

既定の録画保存先は `%USERPROFILE%\Videos\PeerCast Nova\Recordings`（`MyVideos` 未定義時は `%LOCALAPPDATA%\PeerCast Nova\Recordings`）で、設定画面から変更できます。

起動時オプションや環境変数の例:

```powershell
# 外部プレイヤーや libmpv を一時指定して起動
Nova.App.exe --libmpv-library "C:\path\libmpv-2.dll"
Nova.App.exe --pcrplayer-executable "C:\path\PCRPlayer64.exe"

# ポート自動開放を一時有効化して起動
$env:NOVA_AUTO_PORT_MAPPING = "on"
dotnet run --project src/Nova.App
```

---

## 必要環境とビルド・テスト

- **.NET 10 SDK**
- **Windows**（`Nova.App` GUI の実行・発行時。コアライブラリとクロスプラットフォームテストは Linux でも実行可能）

### ソリューション構成とビルド・テスト

デスクトップアプリ本体およびコアライブラリ・テストを含む構成は [PeerCast-Nova.sln](PeerCast-Nova.sln)（または同一構成の [PeerCast-Nova.App.sln](PeerCast-Nova.App.sln)）でビルド・検証できます。

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

---

## 配布・パッケージング

Windows x64 向けに 2 種類の発行プロファイルを用意しています。

- **Portable（.NET 10 ランタイム別途必要・診断用 PDB 同梱）**: [WindowsPortable.pubxml](src/Nova.App/Properties/PublishProfiles/WindowsPortable.pubxml)
- **Self-Contained（ランタイム同梱・診断用 PDB 別パッケージ化対応）**: [WindowsSelfContained.pubxml](src/Nova.App/Properties/PublishProfiles/WindowsSelfContained.pubxml)

```powershell
# Portable 版の発行
dotnet publish src/Nova.App/Nova.App.csproj -p:PublishProfile=WindowsPortable -o artifacts/publish-portable

# Self-Contained 版の発行
dotnet publish src/Nova.App/Nova.App.csproj -p:PublishProfile=WindowsSelfContained -o artifacts/publish-self-contained
```

`v*` タグのプッシュまたは手動トリガーにより、[release.yml](.github/workflows/release.yml) が Portable ZIP・Self-Contained ZIP・PDB シンボル ZIP・`SHA256SUMS.txt` を自動生成します。

---

## ドキュメントと設計資料

- [ナビゲーション (NAVIGATION.md)](NAVIGATION.md)
- [ロードマップ (ROADMAP.md)](ROADMAP.md)
- [変更履歴 (CHANGELOG.md)](CHANGELOG.md)
- [システムアーキテクチャ](docs/architecture/PCN-0004-SYSTEM-ARCHITECTURE.md)
- [Solution と project 構成](docs/architecture/PCN-0005-SOLUTION-STRUCTURE.md)
- [Control plane と data plane](docs/architecture/CONTROL-AND-DATA-PLANES.md)
- [NAT ポートマッピング基盤](docs/architecture/NAT-PORT-MAPPING-FOUNDATION.md)
- [PeerCastStation 互換性計画](docs/protocol/PCP-COMPATIBILITY-PLAN.md)
- [Protocol と Relay の検証記録](docs/protocol/PCP-RELAY-COMPATIBILITY.md)
- [設計判断記録 (ADR)](docs/adr)
- [実装・検証レビュー (PCN)](docs/reviews)

---

## ライセンスと外部ランタイム

PeerCast Nova のソースコードは [Apache License 2.0](LICENSE)（および [NOTICE](NOTICE)）の下で提供されています。

FFmpeg および `libmpv` は外部ランタイムとして扱われ、既定の Nova 配布物にはバイナリを同梱しません。法務・ライセンス境界の詳細は [FFmpeg External Runtime Compliance Policy](docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md) および [THIRD-PARTY-LICENSES/FFmpeg/NOTICE.md](THIRD-PARTY-LICENSES/FFmpeg/NOTICE.md) を参照してください。
