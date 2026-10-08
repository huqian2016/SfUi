# 項目の使用箇所（フィールド影響分析）設計書（C3）

最終更新: 2026-10-08 / ステータス: **実装完了（単体テスト + 実 org スモーク + UIA 14/14 PASS）**
対象: 組織情報ウィンドウ（オブジェクト項目タブ）から起動 + 新規ウィンドウ（WPF / Avalonia）

---

## 1. 目的・背景

- 「**この項目を消すと何が壊れる？**」「どこで使われている？」に答える横断ビューが Salesforce 標準に無く、
  Apex・フロー・入力規則・レイアウト・数式・リストビュー・権限を**手作業で**確認するしかない。
- 項目の削除・型変更前の安全確認と、項目整理（クリーンアップ）プロジェクトでの工数を大幅に削減する。

## 2. 検索ソースと方式（acc 組織で実機プローブ済み 2026-10-08）

| # | ソース | 方式 | 検証結果 |
|---|---|---|---|
| S1 | Apex クラス | Tooling `SELECT Id, Name, Body FROM ApexClass WHERE NamespacePrefix = null` を**行単位**検索 | ✅ 取得 OK |
| S2 | Apex トリガー | 同上（`ApexTrigger`） | ✅（同系） |
| S3 | フロー | Tooling `Flow` の `Metadata`（**構造化 JSON**）を再帰走査 | ✅ Metadata 取得 OK |
| S4 | 入力規則 | Tooling `ValidationRule` の `Metadata`（XML 文字列）を XML 走査 | ✅ 列・オブジェクト有効（acc は 0 件 → 実装時に hks4sand1 の実データで内容確認） |
| S5 | レイアウト | Tooling `Layout` の `Metadata`（XML）を XML 走査 | ✅ 取得可（内容の十分性は実装時に確認。不足時は `sf project retrieve` フォールバックを検討） |
| S6 | 数式項目（同オブジェクト） | REST describe の `calculatedFormula`（`DataIoField` を拡張して取得） | 実装時に確認（標準機能・確度高） |
| S7 | 権限（参照している権限） | **通常 SOQL**（Tooling 不可を実機確認済み）`FieldPermissions WHERE Field = 'Object.Api'` → PS/プロファイル名 + R/E | ✅ 方式確定 |
| S8 | レポート | **対象外**（Analytics API で実用的な横断検索が不可） | スコープ外として明記 |

- マネージドパッケージの Apex/Flow（変更不可）は除外する。
- 未実装ソースがあっても**部分結果を返す**（失敗は Warnings に記録して UI に表示）。

## 3. モデル / 一致仕様

- `FieldUsageSourceKind`（enum）: ApexClass / ApexTrigger / Flow / ValidationRule / Layout / FormulaField / Permission
- `FieldUsageHit`: SourceKind / ComponentName / ComponentId? / LineNumber? / Path?（フロー等の階層パス）/ Excerpt（前後を切った抜粋）/ OpenUrl?
- `FieldUsageResult`: ObjectApiName / FieldApiName / FieldLabel? / Hits / Warnings（`FieldUsageWarning(SourceKind, Message)`）/ Duration / CountBySource
- **一致仕様**: 単語境界つき完全一致（大小文字無視）
  `(?<![A-Za-z0-9_])Field(?![A-Za-z0-9_])` — `Account.Field__c` のような参照は `.` の後ろでも一致、`MyField__c` や `Field__cExtra` は不一致。
- 抜粋: 一致行（Apex は行番号付き。XML/JSON は該当テキストノード/値）を最大 200 文字で表示。

## 4. Core サービス

`src/SfUi.Core/Services/FieldUsageService.cs`

```csharp
Task<FieldUsageResult> AnalyzeAsync(string targetOrg, string objectApiName, string fieldApiName,
                                    bool forceRefresh = false, CancellationToken ct = default)
```

- ソースごとに独立したメソッド + try/catch（失敗は Warnings へ）。並列取得（`Task.WhenAll`）。
- Apex/Flow/ValidationRule/Layout の全件取得は**組織単位でメモリキャッシュ**（`forceRefresh` で更新）。
  初回は API 消費が大きい旨をステータスに表示。マネージド除外（`NamespacePrefix = null` は ApexClass/ApexTrigger、Flow は `NamespacePrefix` 相当を確認）。
- ファイル: `FieldUsageModels.cs` + `FieldUsageService.cs`（`OrgRecordCompareService` と同じ構成）。
- `OrgInfoUrlBuilder` に Setup リンク追加: ApexClass `lightning/setup/ApexClasses/page?address=/{id}`、Flow `/setup/Flows/page?address=/{id}`（既存方式）、ValidationRule / Layout は best-effort。

## 5. UI

### 5.1 新規ウィンドウ `FieldUsageWindow`（非モーダル・複数同時表示可）

- ヘッダー: 対象 `オブジェクト / 項目（ラベル (API)） / 組織` + **再検索ボタン** + **CSV 出力**
- サマリ行: `N 件の使用箇所（M ソース / x.x 秒）`、警告があれば黄色で `一部のソースを取得できませんでした: …`
- 結果: **ソース別 Expander**（見出しに件数バッジ `Apex クラス (3)`）+ グリッド
  （列: コンポーネント / 行・パス / 抜粋 / ↗ で Salesforce を開く）
- フィルタ: 検索テキスト（コンポーネント名・抜粋を部分一致）
- `FieldUsageViewModel`（Presentation）: `Initialize(org, objectApiName, fieldApiName, fieldLabel?)` → `RunCommand` /
  `Groups`（`FieldUsageGroupViewModel`: SourceKind / Header / Hits / CountText）/ `ExportCsvCommand` / `StatusText`

### 5.2 導線（入口）

- **組織情報 → オブジェクト項目タブ**: グリッドで項目行を選択 → ツールバーの **「使用箇所を検索」** ボタン
  （未選択時は無効 + ヒント）。押すと `FieldUsageWindow` を開き自動検索。
- `IAppWindowService.OpenFieldUsage(org, objectApiName, fieldApiName, fieldLabel?)` を追加（WPF/Avalonia 実装 + Factory）。

### 5.3 Avalonia

- `FieldUsageWindow.axaml(.cs)`（Expander + DataGrid + ↗ ボタン）。既存の動的 UI パターンを踏襲。

## 6. UiText 新キー（4 言語）

`OrgInfo_Fields_FindUsage`（ボタン）/ `FieldUsage_*`（タイトル・ヘッダー・ステータス・ソース名 ×7・列名・ボタン・CSV・警告・
フィルタ・0 件メッセージ）。動的キーは作らない。

## 7. フェーズ計画

| Phase | 内容 | 完了条件 |
|---|---|---|
| 1 | モデル + `FieldUsageService`（S1/S2/S3 + 一致・抜粋ロジック）+ 単体テスト | `dotnet test` 緑 |
| 2 | S4/S5/S6/S7 追加 + 実 org probe（hks4sand1 / acc で ValidationRule/Layout 実データ確認） | 実 org で警告なし（または明示的スコープ外） |
| 3 | `--smoke --smoke-fieldusage <org> <Object> <Field>`（ソース別件数・警告・所要時間をログ出力） | smoke exit 0 |
| 4 | WPF UI（ウィンドウ / Factory / DI / オブジェクト項目タブ導線 / キー） | 実機で検索→結果表示 |
| 5 | Avalonia UI | Avalonia 実行で表示 |
| 6 | UIA 検証（`sfui-field-usage-check.ps1`）+ ドキュメント更新 | 全 PASS |

## 8. 検証方法

- 単体: 一致判定（`Field__c` vs `MyField__c` vs `Field__cX`）、抜粋切り出し、Flow JSON 走査（パス付き）、XML 走査、Warnings 集約
- スモーク: `--smoke --smoke-fieldusage hks4sand1 Account Name` など（ソース別件数表示）
- UIA: 組織情報 → オブジェクト項目 → 行選択 → 「使用箇所を検索」→ ウィンドウの件数テキスト検証
- 目視: 権限ソースの表示 / CSV / ↗ リンク

## 9. リスクと対策

| リスク | 対策 |
|---|---|
| ApexClass 全件 Body 取得が重い（API 消費・時間） | 組織単位キャッシュ + 明示更新。初回のみ注意文言。件数上限（例: 2,000 クラス超は警告） |
| Tooling `Layout`/`ValidationRule` の Metadata が不完全な可能性 | 実装時に実データ確認。不足時は `sf project retrieve`（一時 sfdx プロジェクト）フォールバックを Phase 2 で判断 |
| 誤検出（部分一致） | 単語境界一致のみ。完全一致以外は出さない |
| 権限ソースの大量行 | R/E いずれかが true の行のみ表示、名前空間パッケージ由来は除外 |

## 10. 実装メモ（2026-10-08 完了時点・実測で確定した制約）

| # | 制約 / 判明事項 | 対応 |
|---|---|---|
| 1 | Tooling REST で **Metadata / FullName を含むクエリは 1 行制限**（2 行以上でエラー） | 一覧（メタデータ無し）→ 1 件ずつ `WHERE Id = '...'` で取得（並列 6・組織単位キャッシュ） |
| 2 | Tooling `Layout` は **`WHERE EntityDefinitionId = 'X'` が必須**（無絞り込みは 1 行のみ返る） | 対象オブジェクトで絞って一覧 → 1 件ずつ Metadata |
| 3 | Tooling `Flow` は `WHERE Status = 'Active'` で複数行取得可。acc は Active が 0 件（異常ではなく実データ） | hks4sand1 のスモークで動作確認 |
| 4 | `FieldPermissions` は `Field = 'Object.Api'` で **0 件**になる（この API では照合不可） | `SobjectType` で取得しクライアント側で Field 照合（`PermissionAccessService` と同じ方式） |
| 5 | 数式項目は REST describe の `calculatedFormula`（`DataIoField` に追加） | FormulaField ソースとして検索 |
| 6 | Apex はコメント行（`//` `/*` `*` 始まり）をスキップしてノイズ削減 | `ScanText(skipCommentLines: true)` |

- 検証: 単体テスト 10 件 / スモーク（hks4sand1 `Account.Description` = 54 件・3 ソース・6.9 秒・警告 0）/ UIA
  `C:\SfUiDemo\sfui-field-usage-check.ps1` **14/14 PASS**（Org Info → Object Fields → Account → Description → 検索 → 権限フィルタ「Read: True / Edit: True」まで検証）。
- 入力規則（ValidationRule）はテスト org に存在せず 0 件（コード経路は実装済み・スモークで正常終了を確認）。
