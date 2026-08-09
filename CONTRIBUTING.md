# Contributing

PeerCast Nova への貢献を歓迎します。

## 基本方針

- 誰の案かではなく、より良い案を選ぶ
- 小さく明確な Pull Request を作る
- 公開 API の変更には RFC を用意する
- 仕様変更の理由を ADR に残す
- コードと同時にテストと文書を更新する

## 開発手順

```powershell
dotnet restore
dotnet build
dotnet test
```

## ブランチ

- `main`: 常にビルド可能
- `feature/*`: 機能開発
- `fix/*`: 修正
- `docs/*`: 文書

## Documentation synchronization

When adding the latest ADR and PCN pair, update `README.md`, `ROADMAP.md`,
`CHANGELOG.md`, and `NAVIGATION.md` in the same change. The repository contract
tests verify filename/header numbering, the latest ADR/PCN title pair, current
document markers, Navigation links, and ROADMAP document paths.

### ADR granularity

Use an ADR when a change records a durable architecture decision, ownership or
lifecycle boundary, irreversible compatibility choice, safety／authorization
boundary, or production policy. Preserve the existing fail-closed,
bounded-ownership, and exact source-provenance principles.

Do not mechanically create one ADR for each metadata projection, cursor wrapper,
or source-binding helper. A cohesive production composition slice may be covered
by one appropriately scoped ADR when its ownership, lifecycle, and failure
boundaries are reviewed together.

## Definition of Done

- 設計意図が説明されている
- 実装されている
- テストがある
- 文書が更新されている
- レビュー可能な大きさである
