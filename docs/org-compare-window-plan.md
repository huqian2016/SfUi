# 組織比較ウィンドウ（Org Compare Window）設計書

最終更新: 2026-10-03 / ステータス: **全ステップ完了（テスト 277 件 / スモーク・E2E 検証済み）**

対象リポジトリ: `c:\huqian\vscode\SfUi`（WPF / .NET 9 / SfUi.Core + SfUi.App）

---

## 1. 目的とスコープ

複数の Salesforce 組織を **一括で横並び比較** するための独立ウィンドウを追加する。組織の設定値・OWD・統計に加え、主要レコード（ユーザー・プロファイル・権限セット・ロール・オブジェクト等）を **API 名で突合** し、「片方にのみ存在する」「値が異なる」を差分としてハイライトする。組織情報ウィンドウ（`docs/org-info-window-plan.md`）のキャッシュ・取得ロジックをそのまま共有する。

### 1.1 確定事項（2026-10-03 ユーザー確認済み）

- 比較範囲 = **設定値 + OWD + 統計 + レコード差分**（レコードは **Id ではなく API 名** で突合。ユーザーのみ API 名が無いため Username）
- UI = **行=項目・列=組織のグリッド + 差分行ハイライト + 「差分のみ表示」フィルタ**
- 組織数 = **2〜4 組織をチェックボックスで選択**（メインウィンドウの選択組織には依存しない）
- データ取得 = **キャッシュ優先**（`data/orginfo/<orgKey>.json` を組織情報ウィンドウと共有）。**未取得分は比較時に自動取得**
- 出力 = **CSV エクスポートあり**（AI 添付は今回なし）
- ウィンドウ形式 = **新規の独立ウィンドウ（非モーダル・複数同時可）**。メインウィンドウ上部バーの「組織情報」ボタンの隣に「組織比較」ボタンを追加

### 既存資産の再利用

| 既存 | 用途 |
|---|---|
| `OrgInfoCacheStore`（GetOrgKey / GetSection / UpsertSection） | 組織情報と共有するローカルキャッシュ |
| `OrgInfoService.FetchSectionAsync` | 未取得セクションの自動取得（ページング・403 ハンドリング済み） |
| `OrgInfoSections.ColumnsFor` / `OrgInfoTokens` | 列定義・言語非依存トークン |
| `OrgInfoDisplay.FormatCell` | トークンのローカライズ・項目ラベル解決（セル表示） |
| `OrgInfoCatalog`（Stats / ComputeStat / StatSourceSection） | 統計行の算出 |
| `OrgInfoSearchService.SectionTitle` | セクションのローカライズ済みタイトル |
| `CsvExporter` + `SaveFileDialog`（SoqlViewModel パターン） | CSV 出力 |
| `OrgInfoWindowFactory` パターン | 非モーダルウィンドウ生成（DI transient + Show()） |
| `AtomicJsonFile` | 比較ウィンドウ状態の原子的保存 |

---

## 2. 受け入れ条件

| # | 要件 | 対応 |
|---|---|---|
| 1 | 複数組織（2〜4）を一括比較 | ウィンドウ上部のチェックボックスで選択。5 個目のチェックは拒否しステータス表示 |
| 2 | 行=項目・列=組織のグリッド | カテゴリごとに 1 グリッド（コードビハインドで動的列生成） |
| 3 | 差分行のハイライト + 差分のみフィルタ | `OrgCompareRow.IsDiff` を DataTrigger で行背景に反映。「差分のみ表示」でフィルタ |
| 4 | API 名での突合 | §4 の突合キー定義（Id 突合はしない） |
| 5 | キャッシュ優先・未取得は自動取得 | カテゴリを開いた時点で不足セクションのみ `FetchSectionAsync` |
| 6 | 取得時刻の把握 | ステータス領域に最終更新時刻・取得件数を表示（余裕があれば各行にツールチップ） |
| 7 | CSV 出力 | 表示中カテゴリ（フィルタ適用後）を DataTable → CSV（UTF-8 BOM） |
| 8 | 日英対応 | `UiText` にキー追加（En/Ja 両方）。言語切替時は表を再構築 |

---

## 3. 画面設計

```
┌ 組織比較 ─────────────────────────────────────────────────────────────────┐
│ 比較する組織: ☑ hks4sand1 (user@example.com)  ☑ acc (user@example.com)     │
│               ☐ hks4 (…)  ☐ EduCloud (…)        ← 最大 4 組織              │
│ [このタブを再取得]   ☐ 差分のみ表示   [CSV 出力]                           │
│ ステータス: hks4sand1: 権限セット を取得中… (1/5)                          │
├───────────────────────────────────────────────────────────────────────────┤
│ [概要] [設定] [OWD] [統計] [ユーザー] [プロファイル] [権限セット] [ロール]  │
│ [オブジェクト] [Apex クラス] [Apex トリガー] [フロー] [レコードタイプ]      │
│ ┌──────────────────┬───────────────────┬───────────────────┐             │
│ │ API 名           │ hks4sand1         │ acc               │             │
│ ├──────────────────┼───────────────────┼───────────────────┤             │
│ │ MyObject__c      │ カスタム ・ …      │ —                 │ ← 差分(黄)   │
│ │ Account          │ 標準 ・ …          │ 標準 ・ …          │             │
│ └──────────────────┴───────────────────┴───────────────────┘             │
└───────────────────────────────────────────────────────────────────────────┘
```

- 組織チェックボックスのラベル = `OrgInfo.DisplayName`（Alias (Username)）。選択状態は永続化
- 2 組織未満のときは表を構築せず「2 つ以上選択してください」をステータス表示
- タブ = 比較カテゴリ（§4）。タブ切替時にキャッシュ読み → 不足分を自動取得 → 表再構築
- セルの状態表示: 値あり / **—**（その組織に存在しない）/ **未取得** / **取得失敗**
- ツールチップ: 比較列の「列ラベル: 値」一覧（クリックで Setup リンクを開くのは将来拡張)

---

## 4. 比較カテゴリと突合キー

| # | カテゴリ | セクション | 種別 | 突合キー | 表示・比較列 |
|---|---|---|---|---|---|
| 1 | 概要 | overview | Items | 行 Id（項目 ID） | value |
| 2 | 設定 | settings | Items | 行 Id（項目 ID） | value |
| 3 | OWD | owds | Keyed | 行 Id（`org:X` / API 名） | internal, external |
| 4 | 統計 | （算出） | Stats | 統計 ID（7 項目） | `OrgInfoCatalog.ComputeStat` |
| 5 | ユーザー | users | Keyed | **username**（API 名なし） | name, profile, role, active |
| 6 | プロファイル | profiles | Keyed | **name**（API 名） | userType, activeUsers |
| 7 | 権限セット | permissionSets | Keyed | **apiName** | label, assignedUsers |
| 8 | ロール | roles | Keyed | **developerName** | parentRole |
| 9 | オブジェクト | objects | Keyed | **apiName**（QualifiedApiName） | label, kind, internalOwds, externalOwds |
| 10 | Apex クラス | apexClasses | Keyed | **name** | apiVersion, status, valid |
| 11 | Apex トリガー | apexTriggers | Keyed | **name** | apiVersion, status, valid |
| 12 | フロー | flows | Keyed | **apiName** | label, processType, active |
| 13 | レコードタイプ | recordTypes | Keyed | **sobject + developerName**（複合） | name |

- **Items**: 行集合が固定。最初に見つかった取得済みセクションの行順をテンプレートに使用
- **Keyed**: 全組織の行の **キーのユニオン**。キー突合は `OrdinalIgnoreCase`。キー欠落 = セル「—」
- **Stats**: `OrgInfoCatalog.Stats` の 7 項目。算出元セクション（users / profiles / permissionSets / roles / objects）から算出
- セル内表示は表示列を `FormatCell` でローカライズし「 ・ 」で連結（空値は省略）

### スコープ外（将来拡張）

scheduledJobs / connectedApps / installedPackages / loginHistory / auditTrail / currencies / `fields:` の比較。カタログ（`OrgCompareCategories.All`）に 1 エントリ追加で拡張できる構造にする。

---

## 5. 差分判定

- 対象セル: `Value`（値あり）と `Missing`（存在しない）。`NotFetched` / `Failed` は比較から除外（未取得は差分と表示しない）
- 差分条件:
  1. **存在差**: ある組織にキーがあり、別の組織に無い（`Missing`）
  2. **値差**: 全組織で `Value` のとき、比較列の生値（`null` と空文字は同一視・大文字小文字は無視）が組織間で不一致
- 1 組織分しか値が無い場合は差分なし（比較不能のため）
- タイムスタンプ系（created / lastLogin / lastModified）は比較列に含めない
  - 対象外の例: users の email は表示しない（値を変えている組織でノイズになるため。将来列を追加可能）

---

## 6. 取得ポリシー

1. カテゴリを開いた時点で、選択組織 × 必要セクション（統計は 5 セクション）を順に処理
2. キャッシュ（`OrgInfoCacheStore.GetSection`）に `HasData` があればそれを使用（API なし）
3. 未取得セクションは `OrgInfoService.FetchSectionAsync` → `UpsertSection`（進捗をステータス表示）
4. 「このタブを再取得」= 強制再取得（キャッシュを上書き）
5. 取得失敗時:
   - キャッシュなし → セルは「取得失敗」状態（ログに記録、比較は継続）
   - キャッシュあり → 既存キャッシュを表示（ログに記録。fetchedAt で古さを判断）
6. 言語切替時は再構築のみ（`fetchMissing: false` で API を呼ばない）

---

## 7. 永続化（data/orginfo/compare.json）

```
OrgCompareState
  SchemaVersion: 1
  OrgUsernames: ["user1@example.com", "user2@example.com"]   // 最大 4・重複除去
  CategoryId: "permissionSets"                                // 不明カテゴリは null にリセット
  DiffOnly: false
```

`AtomicJsonFile` で保存（スキーマ不一致は作り直し）。組織は **Username** で保存し、起動時に `Orgs` と突合して復元。

---

## 8. CSV 出力

- 表示中カテゴリの **フィルタ適用後** の行を出力
- 列 = `項目 / API 名` + 組織名（DisplayName）+ `差分`（≠ / 空）
- `CsvExporter.ToCsv(DataTable)` → `SaveFileDialog`（既定名 `org-compare-<category>-yyyyMMdd-HHmmss.csv`、UTF-8 BOM）
- SoqlViewModel.ExportCsv と同じパターン（ファイルを開く処理はしない）

---

## 9. 実装ステップ

### Step 1: Core（本ステップ）
1. `src/SfUi.Core/Models/OrgCompareModels.cs` — カテゴリカタログ・表モデル・セル状態
2. `src/SfUi.Core/Services/OrgCompareService.cs` — 構築（キャッシュ優先 + 自動取得）・差分判定・DataTable 化
3. `src/SfUi.Core/Storage/OrgCompareStateStore.cs` — 状態の保存/読み込み
4. `SfUiServiceCollectionExtensions.cs` に DI 登録 + UiText キー追加（Compare_*）
5. テスト（OrgCompareServiceTests / OrgCompareStateStoreTests）→ build/test green

### Step 2: UI
6. `CompareOrgsViewModel` + `CompareOrgItemViewModel` + `CompareCategoryViewModel`
7. `CompareOrgsWindow.xaml(.cs)` + `CompareCategoryView.xaml(.cs)`（動的列・ハイライト・フィルタ）
8. `CompareOrgsWindowFactory` + App DI 登録
9. `MainViewModel.OpenCompareOrgsCommand` + MainWindow ボタン + UiText キー（Compare_* / Main_Compare*）

### Step 3: 検証・仕上げ
10. `--smoke-compare <org1,org2>` + VS Code タスク、実組織での E2E（UIA）・CSV 検証・スクリーンショット
11. README（EN/JA）・PLAN.md（Phase 12）更新、バージョンは次回リリースで 0.5.0

---

## 10. 検証計画

1. `dotnet build`（0 警告）/ `dotnet test`（既存 251 件 + 新規）
2. `--smoke-compare hks4sand1,acc`: 各カテゴリの行数・差分件数をログ確認（キャッシュ有無の両方）
3. 実アプリ E2E: 組織 2 つ選択 → 差分行ハイライト → 差分のみフィルタ → CSV ファイル検証 → スクリーンショット
4. エッジ: 組織 1 つ / 5 個目のチェック拒否 / 未取得カテゴリの自動取得 / 取得失敗マーカー / 言語切替

---

## 11. 実装状況

- [x] Step 1: Core（モデル・サービス・ストア・テスト）— 2026-10-03 完了
  - `OrgCompareModels.cs` / `OrgCompareService.cs` / `OrgCompareStateStore.cs` / DI 登録 / UiText キー 5 種
  - テスト 25 件追加（`OrgCompareServiceTests` / `OrgCompareStateStoreTests`）→ 全 276 件成功
  - 知見: 差分の値比較は大文字小文字を無視（キー突合と揃える）。レコードタイプの表示ラベルは複合キー「Sobject.DeveloperName」
- [x] Step 2: UI（ウィンドウ・VM・ボタン）— 2026-10-03 完了
  - `CompareOrgsWindow`（組織チェックボックス最大 4・カテゴリタブ 13・差分のみフィルタ・CSV 出力）、`CompareCategoryView`（動的列 + 差分ハイライト + セルツールチップ）、`CompareOrgsViewModel` / `CompareCategoryViewModel`、`CompareOrgsWindowFactory`、メインウィンドウの「組織比較」ボタン、UiText キー 16 種
  - E2E（`C:\huqian\sfui-compare-verify.ps1`）: 12 組織表示 → acc / hks4sand1 を選択（未取得分は自動取得）→ 13 タブ → 権限セット 290 行 → 差分のみ 250 行 → CSV 290 行（ヘッダー = 項目 + 組織 + 差分）を確認
  - 知見: Windows 11 の最新式 SaveFileDialog はファイル名欄が UIA の Pane（ValuePattern 不可）→ 自動化はキーボード操作（Ctrl+A → パス → Enter）で保存する。ValuePattern 失敗時に SendKeys でパスを追記すると Windows が「ファイル名に特殊文字は使用できません」を出す（アプリ側の既定名は有効）
- [x] Step 3: 検証・仕上げ（スモーク・E2E・README/PLAN）— 2026-10-03 完了
  - `--smoke --smoke-compare hks4sand1,acc`: 全 13 カテゴリの行数/差分件数をログ出力（settings 20/0・owds 787/652・stats 7/7・users 31/31・profiles 62/46・permissionSets 290/250・roles 21/21・objects 779/650・apexClasses 558/558・apexTriggers 16/16・flows 356/288・recordTypes 58/58。ExitCode=0）。VS Code タスク `run (smoke compare)` 追加
  - README（EN/JA）・PLAN.md（Phase 12）更新。バージョン: **0.5.0** 化（`SfUi.App.csproj` / `AppxManifest.xml` = 0.5.0.0 / MSIX = `dist\SfUi_0.5.0.0_x64.msix` 再ビルド）
