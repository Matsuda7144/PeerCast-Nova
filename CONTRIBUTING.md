# Contributing

PeerCast Nova への貢献を歓迎します。小さくレビューしやすい変更を作り、利用者が既存の PeerCast／PeerCastStation ネットワークを使えることを優先してください。

## 開発で守る方針

- P2P First、Simplicity、Lightweight を優先し、MVP に不要な必須依存や常駐処理を増やさない。
- PeerCastStation との互換性を最初に確認する。MVP に Nova 固有の必須 wire protocol を追加しない。
- Core と UI を分離し、payload を control plane に流さない。
- 境界、ownership、backpressure、停止と cleanup を明確にする。
- 実装済みの範囲と検証範囲を区別し、未実装を完了扱いしない。
- Protocol やネットワークの実例を記録するときは、channel ID、peer address、個人情報を公開前に除去する。

## ローカル開発

必要環境は .NET 10 SDK です。標準の restore、Release build、full test、format 検証は次のとおりです。

~~~powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
~~~

Windows の日常アプリを起動する場合:

~~~powershell
dotnet run --project src/Nova.App
~~~

MVP の再生は PCRPlayer／DSPlayer など対応 external player を使います。現在の `Nova.App` は既定で `PCRPlayer64.exe` を PATH から探します。別の実行ファイルを使う場合は `--pcrplayer-executable <path>` または `NOVA_PCRPLAYER_EXE` を指定してください。

GitHub Actions は main push と Pull Request で自動実行されます。CircleCI は GitHub Actions が起動前に利用できない場合の手動 fallback です。CI の結果は作業中の最新 SHA に結び付けて確認してください。Routine CI は Windows native output の物理確認を行いません。

## 変更とレビュー

- 変更目的を一つに絞り、レビュー可能な大きさにする。
- 実装を変更する場合は、影響する動作を自動テストで確認し、必要な文書も同じ変更に含める。
- 文書のみの変更は、リンク、コマンド、差分が実装と矛盾しないことを確認する。
- 公開 API、protocol compatibility、lifecycle／ownership など長期的な境界判断は ADR に記録する。PeerCastStation wire behavior を変更・拡張する提案では、互換性と negotiation 方法を RFC として説明する。
- ADR 等のドキュメントを追加・更新した場合は、ファイル名と見出し番号（`ADR-xxxx` / `PCN-xxxx`）および関連 Markdown リンクの整合性を確認する。Repository documentation checks が番号形式とリンク先を確認します。

## Definition of Done

- 変更された動作と利用者への影響が説明されている。
- 関連する自動テストと format check が通っている。物理 Windows output の証拠は別途記録する。
- 文書と実装の対応が取れている。
- 差分に無関係な生成物や実験記録を含めない。
