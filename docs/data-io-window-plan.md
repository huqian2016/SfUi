# データ入出力ウィンドウ（Data Export / Data Import）設計書

最終更新: 2026-10-03 / ステータス: **設計確定（ユーザー確認済み）→ 実装中**

対象リポジトリ: `c:\huqian\vscode\SfUi`（WPF / .NET 9 / SfUi.Core + SfUi.App）

---

## 1. 目的とスコープ

Salesforce Inspector Reloaded の **Data Export / Data Import** 相当の機能を、独立した 1 ウィンドウ（2 タブ）として追加する。SOQL のビルダー／直接入力によるクエリ結果の CSV / JSON / TSV エクスポートと、CSV からの Insert / Update / Upsert / Delete インポート（項目マッピング・進捗表示・行別エラー付き）を REST API と Bulk API 2.0 の両エンジンで実行できるようにする。

メインウィンドウの上部バーボタンと、**組織情報ウィンドウのオブジェクトタブ**（選択行のオブジェクトを対象としてプリセット）から開ける。対象組織はメインウィンドウの選択組織（組織情報ウィンドウと同じ扱い）。

### 1.1 確定事項（2026-10-03 ユーザー確認済み）

- ウィンドウ構成 = **1 ウィンドウ + 2 タブ（エクスポート / インポート）**。オブジェクト選択は両タブで共有
- 操作種別 = **Insert / Update / Upsert（外部 ID 指定）/ Delete**
- 出力形式 = **CSV（UTF-8 BOM 保存）/ JSON / TSV**。読込は UTF-8（BOM 有無）+ Shift-JIS を自動判定
- 実行エンジン = **REST API + Bulk API 2.0 の切替**
- 履歴 = メインの「履歴」タブに **新種別「データ」** で記録（表示のみ。ダブルクリックは対象組織のウィンドウを開く）
- 検証 = スクラッチ／検証組織（acc 等）でテストレコードの作成→削除を行ってよい

### 1.2 含まないもの

- Excel（.xlsx）入出力（追加ライブラリが必要なため）
- Bulk ジョブ自体の中断（CLI に中断コマンドが無い場合はポーリング停止のみ）
- 複数組織の同時実行、ウィンドウ状態の永続化（file/state store）
- 履歴からの詳細な再実行（SOQL 復元までは行わない。ウィンドウを開くのみ）
- スプレッドシート風のセル編集・レコード削除チェックボックス（Inspector の Update/Delete タブ相当）

### 既存資産の再利用

| 既存 | 用途 |
|---|---|
| `SalesforceRestClient.QueryAsync` / `GetPageAsync` | REST エクスポートのページング |
| `SalesforceRestClient.DescribeAsync`（未使用だった） | オブジェクト項目メタデータ（createable / updateable / 必須 / 外部 ID） |
| `SalesforceRestClient.SendRawAsync` | composite/sobjects・sobjects への DML 呼び出し |
| `SfCliRunner.RunAsync`（timeout 上書き・CancellationToken 対応） | Bulk API（sf data * bulk）実行 |
| `SoqlResultFactory` | JSON レコード → DataTable（グリッド表示） |
| `CsvExporter` / `SaveFileDialog`（SoqlViewModel パターン） | CSV / TSV 保存 |
| `CompareCategoryView.xaml.cs` の動的列生成 | エクスポート結果グリッド |
| `OrgInfoWindowFactory` パターン | 非モーダルウィンドウ生成（Singleton factory + Transient window/VM） |
| `ConfirmPolicies.ShouldConfirm` | インポート実行前の確認ダイアログ |
| `OrgInfoQueryBuilder.EscapeSoqlString` | SOQL エスケープ |

---

## 2. 受け入れ条件

| # | 要件 | 対応 |
|---|---|---|
| 1 | メインウィンドウから開ける | 上部バーに「データ入出力」ボタン（EN `Data I/O` / JA `データ入出力`、要: トップバー幅調整） |
| 2 | 組織情報のオブジェクトタブから開ける + 選択行のオブジェクトをプリセット | オブジェクトタブ限定ボタン。`SelectedRow.Id`（API 名）を渡す。未選択でも開ける |
| 3 | エクスポート: オブジェクト/項目ビルダー + SOQL 直接入力 | 2 モード切替。項目複数選択（検索・全選択・クリア）、WHERE / ORDER BY / LIMIT |
| 4 | エクスポート実行（REST / Bulk） | REST: ページング + 進捗 + キャンセル。Bulk: `sf data export bulk` → status ポーリング → 出力ファイル + プレビュー |
| 5 | 保存形式 | REST 結果から CSV / JSON / TSV。Bulk は CSV / JSON 直書き（TSV は プレビューから再保存） |
| 6 | インポート: CSV 読込 + マッピング | エンコーディング自動判定（UTF-8 / Shift-JIS）、列ごとの送信先項目 ComboBox、自動マッピング、サンプル値表示 |
| 7 | 4 操作の実行 | Insert / Update（Id 列必須）/ Upsert（外部 ID 項目必須）/ Delete（Id 列のみ）。REST / Bulk 両対応 |
| 8 | 結果表示 | 行別の成功 / 失敗 + エラーメッセージ、サマリ（成功 n / 失敗 m / 所要時間）、失敗行のみ CSV 出力 |
| 9 | 履歴 | 新種別「データ」。History タブのフィルタに追加。ダブルクリックで対象組織のウィンドウを開く |
| 10 | 日英対応 | `UiText` キー追加（En/Ja 両方・LocalizationUsageTests 対応） |

---

## 3. 画面設計

```
┌ データ入出力 — acc (user@example.com) ─────────────────────────────────────┐
│ ┌ データエクスポート ┐┌ データインポート ┐                                 │
│ │ 対象オブジェクト: [Account          ▾] (編集可・API 名入力可)             │
│ │ モード: (•) ビルダー  ( ) SOQL 直接入力                                   │
│ │ 項目: [検索____] [全選択] [クリア]   WHERE: [_______]                    │
│ │ ┌──────────────────────────┐ ORDER BY: [_______]   LIMIT: [___]         │
│ │ │ ☑ Id  ☑ Name  ☐ Billing… │                                           │
│ │ └──────────────────────────┘ （ListBox + CheckBox・仮想化）               │
│ │ SOQL: [SELECT Id, Name FROM Account WHERE …        ] （生成 / 直接編集）  │
│ │ エンジン: (•) REST  ( ) Bulk    [実行] [キャンセル]  進捗: ▓▓▓░░ 1,234 件 │
│ ├──────────────────────────────────────────────────────────────────────────┤
│ │ 結果: 1,234 / 12,345 件  [CSV 保存] [JSON 保存] [TSV 保存]               │
│ │ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ │ Id                │ Name             │ …                              │ │
│ │ └──────────────────────────────────────────────────────────────────────┘ │
│ └──────────────────────────────────────────────────────────────────────────┘
└────────────────────────────────────────────────────────────────────────────┘

（インポートタブ）
│ ファイル: [C:\…\accounts.csv     ] [参照…]  エンコーディング: [自動 ▾]  (UTF-8, 10 行)
│ 操作: [Insert ▾]  外部 ID 項目: [External_Id__c ▾]（Upsert 時のみ）
│ オプション: ☐ 空セルを null として送信   エンジン: (•) REST ( ) Bulk
│ マッピング: [自動マッピング]   ⚠ Id 列のマッピングが必要です（Update / Delete）
│ ┌──────────────────────────────────────────────────────────────────────────┐
│ │ ☑ │ CSV 列名    │ サンプル値     │ 送信先項目                            │
│ │ ☑ │ Name        │ テスト 1       │ [Name (Name)                          ▾]│
│ └──────────────────────────────────────────────────────────────────────────┘
│ [実行] [キャンセル]   進捗: ▓▓▓░░ 80 / 200 行
│ 結果: 成功 198 / 失敗 2  [失敗行を CSV 出力]
│ ┌──────────────────────────────────────────────────────────────────────────┐
│ │ 行 │ 結果 │ Id                 │ エラー                                │
│ └──────────────────────────────────────────────────────────────────────────┘
```

---

## 4. アーキテクチャ

### 4.1 Core 新規（src/SfUi.Core）

| ファイル | 内容 |
|---|---|
| `Models/DataIoModels.cs` | `DataIoEngine{Rest,Bulk}` / `DataImportOperation{Insert,Update,Upsert,Delete}` / `DataIoObject` / `DataIoField` / `DataIoObjectDescribe` / `ImportColumnMapping` / `ImportRowResult` / `ImportRunResult` / 進捗レコード |
| `CsvParser.cs` | RFC4180 パーサ（引用符・改行・CRLF・エスケープ）+ エンコーディング判定（BOM→UTF-8/UTF-16、無しは厳密 UTF-8 試行→失敗時 Shift-JIS）。`System.Text.Encoding.CodePages` を csproj に追加 |
| `CsvExporter.cs`（拡張） | `ToTsv(DataTable)` 追加（既存 `ToCsv` は不変） |
| `Services/DataIoQueryBuilder.cs` | オブジェクト + 項目 + WHERE/ORDER/LIMIT → SOQL 生成（エスケープは `OrgInfoQueryBuilder.EscapeSoqlString`） |
| `Services/SObjectDescribeService.cs` | DescribeGlobal（一覧）+ オブジェクト describe（項目詳細）。組織単位のメモリ キャッシュ |
| `Services/DataExportService.cs` | REST: `QueryAsync`+`GetPageAsync` ループ（進捗・キャンセル・ページ上限 500）。Bulk: `sf data export bulk`（`--output-file` / `--result-format csv\|json` / `--wait 0` → `sf data bulk status` ポーリング） |
| `Services/DataImportService.cs` | REST: Insert/Update = `composite/sobjects`（200 件/バッチ・allOrNone=false）、Delete = `composite/sobjects?ids=`（200 件）、Upsert = `PATCH /sobjects/<obj>/<外部ID>/<値>`（URL エンコード・逐次）。Bulk: マッピング済み CSV を一時ファイル化 → `sf data import\|update\|upsert\|delete bulk` |
| `ImportFieldMatcher.cs` | 自動マッピング（API 名 → ラベル、大文字小文字無視）。Id / 外部 ID の自動割当 |
| `ImportValueCoercion.cs` | CSV 文字列 → JSON 値（boolean / 数値 / date 等）。空セル = 省略 or null（オプション） |
| `ImportBatchPlanner.cs` | composite ペイロード / Bulk CSV 生成、200 件分割、バリデーション（Id 列必須等） |
| `ImportResultMapper.cs` | composite 応答配列 / Bulk 結果（sf__Id・sf__Error）→ `ImportRowResult` |
| `Storage/HistoryStore.cs`（拡張） | `HistoryTypes.Data = "data"` + `ToLabel` |

### 4.2 App 新規（src/SfUi.App）

| ファイル | 内容 |
|---|---|
| `Views/DataIoWindow.xaml(.cs)` | 2 タブ（`loc:Tr` 見出し）。1280x760 既定・非モーダル・CenterOwner |
| `Views/DataExportView.xaml(.cs)` | ビルダー / SOQL モード・動的列グリッド（CompareCategoryView 方式）・保存ボタン |
| `Views/DataImportView.xaml(.cs)` | ファイル・マッピング・実行・結果 |
| `ViewModels/DataIoViewModel.cs` | Org / オブジェクト一覧 / 共有 SelectedObject / describe キャッシュ。`Initialize(org, objectApiName?, soql?)` → `LoadAsync()` |
| `ViewModels/DataExportViewModel.cs` | エクスポートタブ（子 VM・`Owner` 参照で共有状態にアクセス） |
| `ViewModels/DataImportViewModel.cs` | インポートタブ（子 VM） |
| `ViewModels/DataIoFieldItemViewModel.cs` 他 | 項目チェック・マッピング行・結果行の行 VM |
| `Services/DataIoWindowFactory.cs` | OrgInfoWindowFactory と同型 |

### 4.3 既存変更

| ファイル | 変更 |
|---|---|
| `MainWindow.xaml` | 「データ入出力」ボタン追加 + 幅調整（フォルダコンボ 300→230 / 組織コンボ 240→215 / 余白縮小） |
| `MainViewModel.cs` | `OpenDataIoCommand`（HasSelectedOrg で有効化）、履歴再実行に data case |
| `OrgInfoSectionViewModel.cs` | `IsObjectsSection` + `OpenDataIoRequested` イベント + コマンド |
| `OrgInfoSectionView.xaml` | オブジェクトタブ限定の「データ入出力」ボタン |
| `OrgInfoViewModel.cs` | `DataIoWindowFactory` 注入 + イベント購読 |
| `HistoryViewModel.cs` | 種別フィルタに「データ」追加 |
| `App.xaml.cs` | DI 登録 + `--smoke --smoke-dataio <alias>` |
| `UiText.En.cs` / `UiText.Ja.cs` | 新キー（Main_DataIo* / DataIo_* / DataExport_* / DataImport_* / Type_Data / Msg_DataIo*） |
| `SfUi.App.csproj` / `AppxManifest.xml` | バージョン 0.6.0 / 0.6.0.0 |
| `PLAN.md` / `README.md` | Phase 13 追記 / 機能説明追記 |

---

## 5. API 詳細

### 5.1 REST エクスポート

1. `GET /services/data/v{ver}/query?q={SOQL}` → `records` / `totalSize` / `done` / `nextRecordsUrl`
2. `done=false` の間 `GetPageAsync(nextRecordsUrl)`（進捗: ページ数・累計行数、キャンセル可）
3. ページ上限 500（超過時は警告し打ち切り）。結果は `SoqlResultFactory` で DataTable 化 + 生 JSON を保持

### 5.2 REST インポート

| 操作 | エンドポイント | バッチ |
|---|---|---|
| Insert | `POST /services/data/v{ver}/composite/sobjects` `{ allOrNone: false, records: [...] }` | 200 件 |
| Update | `PATCH /services/data/v{ver}/composite/sobjects` `{ allOrNone: false, records: [{ Id, ... }] }` | 200 件 |
| Delete | `DELETE /services/data/v{ver}/composite/sobjects?ids=id1,id2,…` | 200 件 |
| Upsert | `PATCH /services/data/v{ver}/sobjects/{Object}/{ExternalIdField}/{Uri.EscapeDataString(value)}` | 逐次 |

応答（composite）: `[{ id, success, errors: [{ statusCode, message, fields }] }]`（allOrNone=false では HTTP 200 でも部分失敗あり）。行番号との対応はリクエスト順。

### 5.3 Bulk API 2.0（sf CLI）

- エクスポート: `sf data export bulk --query <soql> --output-file <path> --result-format csv|json --target-org <org> --json`（`--wait` は 0 にして `sf data bulk status --job-id <id> --json` をポーリング）
- インポート: マッピング済み CSV（ヘッダー = API 名）を一時ファイルに書き `sf data import bulk` / `sf data update bulk` / `sf data upsert bulk --external-id <field>` / `sf data delete bulk --file <csv> --sobject <obj> --target-org <org> --json` → status ポーリング → `sf data bulk results --job-id <id> --json`
- ⚠ 正確なフラグは実装時に `sf data export bulk --help` 等（v2.94.6）で確認する
- キャンセル = ポーリング停止（ジョブはサーバー側で継続）

### 5.4 オブジェクト / 項目メタデータ

- 一覧: `GET /services/data/v{ver}/sobjects`（DescribeGlobal: name / label / queryable / createable / updateable / deletable）
- 項目: `GET /services/data/v{ver}/sobjects/{name}/describe`（name / label / type / createable / updateable / nillable / defaultedOnCreate / externalId / referenceTo）
- 必須判定 = `createable && !nillable && !defaultedOnCreate`
- マッピング候補: Insert/Update/Upsert は createable / updateable、Update/Delete には `Id` を常に追加

---

## 6. 実装ステップ

1. **Phase 1: Core 基盤** — モデル / CsvParser / エクスポータ拡張 / DescribeGlobal + キャッシュ / クエリビルダー / エクスポート・インポート サービス / 純関数ヘルパー / 履歴種別 / UiText / DI / 単体テスト
2. **Phase 2: ウィンドウ + エクスポートタブ** — ウィンドウ骨格・共有コンテキスト・ビルダー UI・REST/Bulk 実行・結果グリッド・保存
3. **Phase 3: インポートタブ** — ファイル読込・マッピング UI・オプション・実行・結果/失敗 CS
4. **Phase 4: 統合** — メイン ボタン + トップバー幅調整 + 組織情報オブジェクトタブ起動 + 履歴フィルタ/再実行 + スモーク
5. **Phase 5: 検証・リリース準備** — 全テスト / E2E（acc 実往復 + Bulk + プリセット + 見切れ検証）/ バージョン 0.6.0 / MSIX / PLAN・README

---

## 7. 検証

1. `dotnet test` = 既存 277 + 新規（CsvParser / Coercion / Matcher / Planner / ResultMapper / QueryBuilder）全緑
2. E2E `C:\huqian\sfui-dataio-verify.ps1`（実アプリ + UIA、acc）
   - REST エクスポート → 行数一致 + CSV / JSON / TSV ファイル検証
   - Bulk エクスポート → ファイル生成 + ジョブ完了 + プレビュー行数一致
   - インポート Insert（UTF-8 BOM / Shift-JIS の 2 ファイル）→ Update → Delete → SOQL で 0 件確認
   - Bulk Import 1 行（insert → delete）
   - Upsert はエラーパス確認（外部 ID 未選択時のブロック）
   - 組織情報オブジェクトタブ → Account 選択 → 起動 → コンボにプリセット
   - トップバー: 言語コンボ右端 ≤ ウィンドウ幅（UIA 計測・見切れなし）
3. `--smoke --smoke-dataio acc` ExitCode=0
4. MSIX 0.6.0.0 unpack 検証

---

## 8. リスク / 留意点

- **トップバー幅**が最大の UI リスク（1520 で余白 ~19px、ボタン ~110px 追加）。幅調整の数値は UIA 実測で詰める
- `SalesforceRestClient` は HttpClient 自前でモック不可 → ロジックは純関数 + サービスに分離し、テストは純関数中心（リポジトリの既存流儀）
- composite API 上限 200 件 / Upsert 非対応（逐次）。外部 ID 値の URL エンコード必須
- Bulk は CLI フラグを実装時に確認。長時間ジョブは `SfCliRunner` の timeout 上書き + status ポーリング
- Shift-JIS 読込は `System.Text.Encoding.CodePages` の参照が必要（WindowsDesktop ランタイムには同梱のためサイズ増は実質なし）
- 空セル: 既定「項目を省略」、チェック時「null 送信」（Update の安全性優先）
- 履歴の再実行は「対象組織が現在の一覧にある場合のみウィンドウを開く」
