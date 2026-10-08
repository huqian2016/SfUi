# 組織情報ウィンドウ「エクスポート」機能（定義書出力）設計書

最終更新: 2026-10-08 / ステータス: **Step 1–3 完了（Core + WPF/Avalonia UI 実装済み・実組織 / UI E2E 検証済み）**
対象リポジトリ: `c:\huqian\vscode\SfUi`

参考: Chrome 拡張「Salesforce DevTools」（xgeek.net）の Export 機能 —
オブジェクト定義書 / 項目定義書 / 画面設計書 / リストビュー設計書 / フロー定義書を Excel で自動生成する機能。
本設計はその「組織情報ウィンドウ版」を SfUi に追加する。

---

## 1. 確定事項（2026-10-08 ユーザー確認済み）

| 項目 | 決定 |
|---|---|
| 出力形式 | **Excel (.xlsx) + CSV の両方** を同時出力 |
| 対象ドキュメント | **5 種すべて**: ①オブジェクト定義書 ②項目定義書 ③画面レイアウト定義書 ④リストビュー定義書 ⑤フロー定義書 |
| まとめ方 | **1 ブックに複数シート**（項目定義書はオブジェクトごとにシート分割）。CSV はシートごとに 1 ファイル |

### 対象外（今回のスコープ外）

- クエリ結果の Excel 出力（SOQL 実行結果 → Excel）: データ入出力ウィンドウの CSV/JSON/TSV で代替可能。需要があれば将来対応
- レイアウト/リストビューのテスト・取得以外の操作（編集・作成・削除）は行わない（閲覧専用の原則を維持）

## 2. API 実現性の検証結果（2026-10-08 / 実組織 hks4sand1・API v68.0 で確認）

| 定義書 | データ源 | 検証結果 |
|---|---|---|
| オブジェクト定義書 | Tooling `EntityDefinition` | ✅ 既存（組織情報のオブジェクトタブで使用中）。列を定義書用に拡張 |
| 項目定義書 | Tooling `FieldDefinition` | ✅ 既存（オブジェクト項目タブで使用中） |
| 画面レイアウト定義書 | Tooling `Layout` + `Metadata` | ✅ `SELECT Id, Name, EntityDefinitionId, Metadata FROM Layout WHERE EntityDefinitionId='01I…'` で **Metadata 全文取得可**（`layoutSections[].layoutColumns[].layoutItems[]`） |
| リストビュー定義書 | REST `/sobjects/{Object}/listviews` + `/{id}/describe` | ✅ Tooling に ListView は無い（INVALID_TYPE）が、**標準 REST で一覧 + describe（列・ソート・フィルタ）取得可** |
| フロー定義書 | Tooling `Flow` + `Metadata` | ✅ `SELECT Id, MasterLabel, Metadata FROM Flow` で **Metadata 全文取得可**（要素・接続・条件） |

## 3. 出力仕様

### 3.1 ファイル構成

出力先フォルダへ、実行日時ベースのファイル一式を書き出す:

- `SfUi_定義書_<alias>_<yyyyMMdd-HHmm>.xlsx` — 1 ブック複数シート
- `SfUi_定義書_<alias>_<yyyyMMdd-HHmm>_<シート名>.csv` — シートごとに 1 CSV（UTF-8 BOM。Excel で直接開ける）

### 3.2 シート構成（xlsx）／ CSV 対応

| シート | 内容 | 行の単位 |
|---|---|---|
| オブジェクト定義 | 選択オブジェクトの定義 | 1 行 = 1 オブジェクト |
| 項目_&lt;Object&gt; | 選択オブジェクトごとの項目定義 | 1 行 = 1 項目 |
| 画面レイアウト | 選択オブジェクトのページレイアウト | 1 行 = 1 項目配置（セクション×列×行） |
| リストビュー | 選択オブジェクトのリストビュー | 1 行 = 1 リストビュー（列・フィルタを集約） |
| フロー一覧 | フローの概要 | 1 行 = 1 フロー |
| フロー要素 | フローの要素明細（Metadata を展開） | 1 行 = 1 要素 |

- シート名は Excel の制約（31 文字 / `[]:*?/\` 禁止）に合わせてサニタイズ + 重複時サフィックス

### 3.3 列案（実装時に既存セクションの列定義と整合させる）

- **オブジェクト定義**: API 名 / ラベル / 複数形ラベル / カスタム / キープレフィックス / 内部共有モデル / 外部共有モデル / 説明 / 作成日時 / 最終更新日時
- **項目定義**: ラベル / API 名 / 型 / 長さ・精度 / 必須 / 参照先 / 数式 / 既定値 / ヘルプ / カスタム / 作成日時 / 最終更新日時
- **画面レイアウト**: オブジェクト / レイアウト名 / セクション / 列番号 / 行番号 / 項目 / 種別（項目 / 空欄 / カスタムリンク / ボタン等）/ 属性（必須 / 読取専用 / 編集可）
- **リストビュー**: オブジェクト / リストビュー名 / 開発者名 / フィルタ範囲 / フィルタ条件 / 列（API 名・表示順・ソート）/ SOQL 互換
- **フロー一覧**: フロー名 / API 名 / 種別 / 状態 / 最終更新 / 要素数
- **フロー要素**: フロー名 / 要素種別 / 要素名 / ラベル / 対象オブジェクト / 接続先 / 条件・設定の要約

## 4. UI 設計（組織情報ウィンドウ）

組織情報ウィンドウのタブ末尾に **「エクスポート」タブ** を追加（`OrgInfoExportView`）。AI 添付・横断検索の対象外。

```
┌─ エクスポート ──────────────────────────────────────────────┐
│ 対象オブジェクト:  [検索: ______] [☑ カスタムのみ]         │
│  [すべて選択] [全解除] [標準も含めてすべて選択]             │
│  ┌──────────────────────┐                                  │
│  │ ☑ Account            │  ← 既存の objects キャッシュから │
│  │ ☑ Contact            │     チェックボックス一覧         │
│  │ ☐ ACM_Settings__c …  │                                  │
│  └──────────────────────┘                                  │
│ 定義書: ☑オブジェクト ☑項目 ☑画面レイアウト ☑リストビュー ☑フロー │
│ フロー: ☑ アクティブのみ                                    │
│ 出力形式: ☑ Excel (.xlsx)  ☑ CSV                           │
│ 出力先: [C:\work\sfui-docs        ] [参照…]  [▶ エクスポート] │
│ 進捗: 画面レイアウト取得中 (12/40) … [キャンセル]            │
│ 結果: 6 ファイル出力（…定義書.xlsx ほか）                    │
└───────────────────────────────────────────────────────────────┘
```

- 対象オブジェクトの選択は **複数選択制**（「全選択」ボタンあり。フローはオブジェクト非依存のため対象外）
- 進捗（n/m）とキャンセルを表示。ドキュメント単位で部分成功を許容し、失敗は結果欄に集約
- 完了後は出力フォルダを開くボタン（`ToolLauncherService.LaunchExplorer`）

## 5. アーキテクチャ

| 層 | 追加/変更 |
|---|---|
| SfUi.Core | `DocumentFormat.OpenXml`（MIT）を追加。`OrgInfoExportService`（取得 + 変換 + 出力のオーケストレーション）、`DefinitionsExcelWriter`（xlsx 生成）、シートモデル `ExportSheet/ExportColumn`、マッパー（Layout/ListView/Flow Metadata → 行） |
| SfUi.Presentation | `OrgInfoExportViewModel`（選択・オプション・進捗・キャンセル・結果。既存 `OrgInfoViewModel` から objects キャッシュを共有）。`IFilePickerService` に **フォルダ選択** を追加（`PickFolderAsync`） |
| SfUi.App / SfUi.Avalonia | フォルダピッカーの実装（WPF: `Microsoft.Win32.OpenFolderDialog` / Avalonia: `IStorageProvider.OpenFolderPickerAsync`）、`OrgInfoExportView`（XAML/axaml）、OrgInfoWindow へのタブ追加、UiText キー追加（En/Ja/Zh/Ko 4 言語） |

- 取得は **都度実行**（キャッシュなし）。将来、レイアウト等を候補セクション化する場合はキャッシュを再利用
- CSV は既存 `CsvExporter`（UTF-8 BOM）を再利用
- 大量データ対策: FieldDefinition / Layout は選択オブジェクトのみ・チャンク実行（200 件単位）、Flow は「アクティブのみ」既定 ON・進捗 + キャンセル対応

## 6. リスクと対処

| リスク | 対処 |
|---|---|
| 全オブジェクト×項目で API コールが膨大（数百〜千件） | 選択制 + 進捗 + キャンセル。標準オブジェクトは明示的に選ばない限り対象外にできる |
| Flow Metadata の合計サイズ（フロー数百件で数十 MB） | 「アクティブのみ」既定 ON。ページング + 進捗 + キャンセル |
| Layout の EntityDefinitionId（01I…）との突合 | objects 取得時に EntityDefinition ID も保持し API 名と対応付け |
| 権限不足のオブジェクト | セクション単位でスキップし、結果に警告として件数表示 |
| Excel シート名制約 | 31 文字へ切り詰め + 重複サフィックス |

## 7. 実装フェーズ

- ✅ **Step 1（Core 基盤）**: OpenXml 3.5.1 / `ExportSheet` / `ExcelExporter`（シート名サニタイズ・枠固定・オートフィルタ・共有文字列・列幅 CJK 対応）+ テスト 6 件
- ✅ **Step 2（取得 + マッパー）**: `OrgInfoQueryBuilder`（Layout / Flow）/ `OrgExportMapper` / `OrgExportWriter` / `OrgExportService` + テスト 16 件（全 504 件グリーン）。実組織 hks4sand1 で E2E 検証済み: 8 シート / 9 ファイル / 56 秒（オブジェクト 3・レイアウト 4・リストビュー 12・フロー 63・要素 535 行、警告なし）
- ✅ **Step 3（UI）**: `OrgInfoExportViewModel`（オブジェクト複数選択・絞り込み・カスタムのみ・全選択/全解除、定義書 5 種、アクティブのみ、Excel/CSV、出力先 + 参照、進捗バー・進捗テキスト、キャンセル、完了/警告表示、フォルダーを開く）/ WPF + Avalonia の `OrgInfoExportView` / 両ウィンドウへタブ追加 / `OrgExportService` の DI 登録 / UiText 4 言語（UI 25 キー）
  - **UI E2E 検証済み（WPF / UIA 自動操作）**: Org Info → Export タブ → 一覧再読み込み → `Engagement` で絞り込み → 全選択（8 件）→ 出力先指定 → Flows OFF → 実行 → **12 ファイル出力・「Done: exported 12 file(s).」表示を確認**（スクリーンショット `C:\SfUiDemo\export-ui\export-tab.png`、検証スクリプト `C:\SfUiDemo\sfui-export-ui2.ps1`）
  - 発見・修正したバグ: 完了文言が `IsBusy=true` 中に設定されて表示されない → `UpdateResultText()` を finally 後に移動
  - 既知の小ネタ: UIA 上での Export タブの Name は VM 型名になる（WPF TabItem の automation peer の仕様）。スクリプト側は最終タブを index で選択して回避。画面表示は「Export」で正常
- **Step 4（残り・仕上げ）**: Avalonia での UI 実行検証（起動スモークは確認済み・クラッシュなし）、xlsx の目視確認、README スクリーンショット差し替え、バージョン bump（v0.12.0 候補）とリリース

### 実装メモ（確定した API 仕様・実測）

- **Tooling API は Metadata / FullName を含むクエリを単一行に限定**（MALFORMED_QUERY: "Result size: N"）。レイアウト・フローは「一覧（Metadata なし）→ 個別 Metadata 取得」の 2 段階。
- Layout: `EntityDefinitionId` は API 名で WHERE / IN 可能（応答も API 名）。1 オブジェクトに複数レイアウトあり（例: Account = Business Account + Person Account Layout）。
- Flow: Tooling `Flow` の Status は Active / Obsolete / Draft。一覧 = `MasterLabel, DefinitionId, Status, ProcessType, VersionNumber, LastModifiedDate`。単一行取得で `FullName`（API 名）+ `Metadata`。非アクティブ含む場合は DefinitionId ごとに最新版のみに集約。
- Flow Metadata の要素は型別配列（`start`, `decisions`, `recordLookups`, `recordCreates`, `actionCalls` …）。要素は `name / label / connector.targetReference / defaultConnector / rules / filters` を持つ。
- ListView: 標準 REST `/sobjects/{obj}/listviews` → `/{id}/describe`。describe は `columns`（fieldNameOrPath / sortDirection / hidden）、`scope`（everything / mine / mru …）、`whereCondition`（`{conditions,conjunction}` または `{field,operator,values}`)、`query`（完全な SOQL）を返す。

## 8. 未決事項

- なし（上記確定事項に基づき実装開始）
- クエリ結果 → Excel 出力（拡張機能の Query Editor 相当）は必要になった時点で別途検討
