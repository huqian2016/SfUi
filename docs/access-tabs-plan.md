# データ入出力ウィンドウ — アクセス権限タブ（3 タブ）設計書

最終更新: 2026-10-04 / ステータス: **設計確定（ユーザー確認済み・実 API 調査済み）→ 実装中**

対象リポジトリ: `c:\huqian\vscode\SfUi`（WPF / .NET 9 / SfUi.Core + SfUi.App）

---

## 1. 目的とスコープ

データ入出力ウィンドウに、権限（アクセス権）を一覧化する 3 タブを追加する。

1. **オブジェクトアクセス** — Permission Sets / Permission Set Groups / Profiles の、選択中オブジェクトに対する権限を一覧化
2. **項目アクセス** — 同主体の、選択中オブジェクトの項目（フィールド）に対する権限をマトリクス表示
3. **レコードアクセス** — 任意 SOQL で抽出したレコードについて、選択ユーザーごとのレコードアクセス権（UserRecordAccess）を一覧化

### 1.1 確定事項（2026-10-04 ユーザー確認済み）

- 追加先 = **データ入出力ウィンドウ**（「データエクスポート」「データインポート」タブの隣に 3 タブ）
- オブジェクトアクセス / 項目アクセスの対象 = **上部の「対象オブジェクト」で選択中の 1 オブジェクトのみ**
- 項目アクセス = **マトリクス表示（行 = 項目、列 = 各権限主体、セル = R / E）+ 種類フィルタで列を絞る**
- レコードアクセスの権限列 = **読取 / 編集 / 削除 / 転送**（作成はレコード単位に存在しないため列にしない）
- ユーザー選択 = **有効ユーザー全員 + 名前検索付き**（リストの高さは 3 行分・超過分はスクロールバー）
- ページサイズ = **200 件**

### 1.2 含まないもの

- PSG のミューティング権限セットの減算（`PermissionSetGroupComponent` にミューティング識別フィールドが無いため和集合近似。凡例に注記）
- 項目アクセスでの「明示行なし = 標準項目の既定アクセス」の補完表示（明示的な付与行のみ表示。凡例に注記）
- 列の手動並べ替え・列の非表示状態の永続化
- レコードアクセスのエクスポート（CSV 保存）

---

## 2. 実 API 調査結果（2026-10-04 / acc 組織で実測）

| 項目 | 結果 |
|---|---|
| UserRecordAccess の WHERE 条件 | **`UserId = '<単一 ID>'` のみ**（UserId IN は `MALFORMED_QUERY`）+ `RecordId = '<ID>'` または `RecordId IN (リスト)` |
| UserRecordAccess の SELECT | **RecordId / Has*Access / MaxAccessLevel のみ**（UserId は選択不可） |
| 1 クエリの結果上限 | **200 行**（210 レコード → `LIMIT_EXCEEDED: Number of records exceeds limit: 200`）⇒ **1 ユーザー × 200 レコード = ちょうど上限**（ページサイズ 200 と一致） |
| 1 ユーザー × 200 件の実行時間 | 約 2.4 秒（REST / sf CLI） |
| アクセスが無い場合の行 | **全フラグ false の行が返る**（行は存在する。RecordId が結果に無い場合は補完して false 扱い） |
| ObjectPermissions（Account） | 128 行（ParentId = PermissionSet Id。プロファイル所有 PS を含む） |
| FieldPermissions（Account） | 1,576 行（REST 1 ページ 2,000 行に収まる） |
| PermissionSet | 160 件（カスタム 150 + プロファイル所有 10）。`Name` / `Label` / `IsCustom` / `IsOwnedByProfile` / `Profile.Name` が取得可 |
| PermissionSetGroup | 18 件。`MasterLabel` / `DeveloperName` のみ（**IsCustom フィールド無し** → 常にカスタム扱い） |
| PermissionSetGroupComponent | 9 行。`PermissionSetGroupId` + `PermissionSetId` のみ（ミューティング識別なし） |
| その他 | Bulk インポート CSV は **BOM なし UTF-8** が必須（BOM 付きは `Field name not found : ・ｿName`） |

---

## 3. 画面設計

```
┌ データ入出力 — acc ───────────────────────────────────────────────────────────┐
│ 対象オブジェクト: [Account ▾] 項目 71 件   (既存の共通ヘッダー)                 │
│ ┌エクスポート┐┌インポート┐┌オブジェクトアクセス┐┌項目アクセス┐┌レコードアクセス┐│
```

### 3.1 オブジェクトアクセス タブ

```
│ 対象: Account (項目 71 件)   [再取得]                              178 件       │
│ ┌────────────┬──────────────┬──────────────┬──────┬─────┬─────┬─────┬─────┬───┐│
│ │種類        │ラベル        │API名         │カスタム│Read │Create│Edit │…    ││
│ │プロファイル│システム管理者│System Admi…  │  −   │ ✓  │ ✓  │ ✓  │     ││
│ │権限セット  │営業管理者    │Sales_Admin   │  ✓   │ ✓  │ ✓  │ ✓  │     ││
│ │権限グループ│営業セット    │Sales_Group   │  ✓   │ ✓  │ −  │ ✓  │     ││
│ └────────────┴──────────────┴──────────────┴──────┴─────┴─────┴─────┴─────┴───┘│
```

- 行 = 選択中オブジェクトについての全主体（プロファイル所有 PS・通常 PS・PSG の合計。権限行が無い主体も全 false で表示）
- 列 = 種類 / ラベル / API名 / カスタムかどうか / Read / Create / Edit / Delete / View All Records / Modify All Records / View All Fields
- 並び順 = プロファイル → 権限セット → 権限セットグループ、各ラベル順
- データ元: `ObjectPermissions WHERE SobjectType = '<obj>'`。**PSG 行は構成 PS（PermissionSetGroupComponent）の権限の和集合**

### 3.2 項目アクセス タブ

```
│ 種類: [✓]プロファイル [✓]権限セット [✓]権限セットグループ  列絞り込み:[____]     │
│ ┌──────────────────────┬────────────────┬────────────────┬───────────────┐    │
│ │項目                  │システム管理者  │Sales_Admin     │Sales_Group    │ …  │
│ │                      │(System Admin… )│(Sales_Admin)   │(Sales_Group)  │    │
│ │名前 (Name)           │R, E            │R, E            │R              │    │
│ │請求先市町村 (BillingCity) │R, E       │−               │R, E           │    │
│ └──────────────────────┴────────────────┴────────────────┴───────────────┘    │
│ 凡例: R = 読取 / E = 編集。空欄 = 明示的な権限行なし（標準項目の既定値は含みません）│
```

- 行 = 選択中オブジェクトの全項目（describe のラベル (API名)）
- 列 = 種類フィルタに合致する主体（ヘッダ = ラベル(API名)、ツールチップ = 種類）
- セル = `R` / `E` / `R, E`（`FieldPermissions` の PermissionsRead / PermissionsEdit。PSG 列は構成 PS の和集合）
- 種類フィルタ（3 チェックボックス）と列絞り込み（部分一致）で列を削減可能

### 3.3 レコードアクセス タブ

```
│ ユーザー (有効 31 人): [検索____]       対象レコード SOQL:                      │
│ ┌─────────────────────────────┐ [SELECT Id, Name FROM Account LIMIT 200____] │
│ │☑ 山田 太郎 (taro@example.com)│ 表示項目: [Name ▾] 検索: [______] [実行]     │
│ │☑ Bob (bob@example.com)      │ ─────────────────────────────────────────────│
│ │☐ Chatter Expert (chat@…)    │ [最初へ][前へ] 2 / 5 (1,000 件) [次へ][最後へ]│
│ └─────────────────────────────┘ ┌────────────────────────────────────────────┐│
│       (3 行分の高さ・スクロール)  │Id        │表示項目 │リンク│太郎 読取│… │編集││
│                                 │001NS…    │Acme     │  ↗  │ ✓      │  ✓ ││
│                                 └────────────────────────────────────────────┘│
```

- ユーザー選択: 有効ユーザー全員（`SELECT Id, Name, Username FROM User WHERE IsActive = true ORDER BY Name`）。名前/ユーザー名の部分一致検索、全選択 / 全解除。高さは 3 行分で、超過分はスクロールバー
- 対象レコード SOQL: 実行時に REST（`QueryAsync` + `GetPageAsync` の queryMore ループ）で取得。最大 10,000 件（超過時は注記）
- 表示項目: SOQL 結果の列から選択（既定 = `Name` があれば `Name`、なければ Id 以外の先頭列）
- ページング: 200 件 / ページ。`最初へ / 前へ / 次へ / 最後へ` + `n / m (総件数)`
- 検索: 読み込み済みレコードを Id / 表示項目の部分一致で絞り込み（クライアント側・即時）
- グリッド列: Id / 表示項目 / リンク / **選択ユーザー 1 人につき 4 列（読取・編集・削除・転送）**。ヘッダ = 「ユーザー名 読取」等
- アクセス取得: ページ表示時に、選択ユーザー × ページ内レコード（≤200 件）を **1 ユーザー 1 クエリ**（並列 4 まで）で取得し `(userId, recordId)` でキャッシュ。ページ移動・検索では不足分のみ取得
- リンク列: `{InstanceUrl}/lightning/r/{ObjectName}/{Id}/view` を既定ブラウザーで開く（オブジェクト名は REST 応答の `attributes.type` から取得）
- セル = ✓ / −（読み取れなかった場合は「−」。取得中は薄色表示）

---

## 4. 受け入れ条件

| # | 要件 | 対応 |
|---|---|---|
| 1 | データ入出力ウィンドウに 3 タブを追加 | TabControl に「オブジェクトアクセス / 項目アクセス / レコードアクセス」 |
| 2 | PS / PSG / Profiles のオブジェクト権限一覧（指定 11 列） | 種類・ラベル・API名・カスタム・Read・Create・Edit・Delete・View All Records・Modify All Records・View All Fields |
| 3 | 項目アクセスのマトリクス（項目 × ラベル(API名)） | 行 = 項目、列 = 主体、セル = R / E |
| 4 | レコードアクセス: ユーザーをチェックボックス選択（3 行以上でスクロール） | 有効ユーザー一覧 + 検索 + スクロール |
| 5 | 対象レコードの抽出 SOQL | SOQL 入力 + 実行（queryMore 対応） |
| 6 | ページングと検索 | 200 件 / ページ + レコード検索（Id / 表示項目） |
| 7 | 列 = ID / オブジェクト項目 / リンク / ユーザー毎の権限列 | Id / 表示項目 / ↗ リンク / ユーザー毎 読取・編集・削除・転送 |
| 8 | CRUD は UserRecordAccess を利用 | 1 ユーザー × ≤200 レコードの REST クエリ（上限 200 行に一致） |
| 9 | 日英対応 | UiText キー追加（En/Ja 同一キー・LocalizationUsageTests 対応） |

---

## 5. 実装ステップ

### Phase A: Core（モデル / サービス / テスト）

- `Models/AccessModels.cs`: `PermissionSubject`（Id / Kind: Profile|PermissionSet|PermissionSetGroup / Label / ApiName / IsCustom）、`ObjectAccessRow`、`FieldAccessMatrix`、`FieldAccessCell`、`RecordAccessUser`、`RecordAccessQueryResult`（行 = 辞書 / 列名 / truncated）、`UserRecordAccessFlags`（Read / Edit / Delete / Transfer）
- `Services/PermissionAccessService.cs`
  - `ListSubjectsAsync(targetOrg)` — PermissionSet + Profile（`IsOwnedByProfile` → Profile.Name をラベルに）+ PermissionSetGroup + PermissionSetGroupComponent。組織単位のメモリキャッシュ
  - `GetObjectAccessAsync(targetOrg, objectApiName)` — ObjectPermissions + PSG 和集合
  - `GetFieldAccessAsync(targetOrg, objectApiName)` — FieldPermissions + PSG 和集合（`Dictionary<subjectId, Dictionary<field, (bool Read, bool Edit)>>`）
- `Services/RecordAccessService.cs`
  - `ListActiveUsersAsync(targetOrg)`
  - `QueryRecordsAsync(targetOrg, soql, maxRecords)` — QueryAsync / GetPageAsync ループ
  - `GetAccessFlagsAsync(targetOrg, userId, recordIds)` — 200 件ずつチャンクして 1 ユーザークエリ（呼び出し側は並列制御）
- テスト: PSG 和集合 / 権限行マッピング / 行補完（欠落 = false）/ SOQL ビルダー（IN リスト）/ チャンク分割 / レコード解析（attributes.type と列名）

### Phase B: UI（タブ / ViewModel / i18n）

- `ViewModels/ObjectAccessViewModel` / `FieldAccessViewModel` / `RecordAccessViewModel`（`Attach(DataIoViewModel)` パターン、`DescribeChanged` 連動、LoadAsync 遅延）
- `Views/ObjectAccessView`（固定列 DataGrid）/ `FieldAccessView`（動的列 + フィルタ）/ `RecordAccessView`（動的列 + ページング + ユーザー選択）
- `DataIoWindow.xaml` に 3 TabItem 追加、`DataIoViewModel` に子 VM を追加
- UiText（En/Ja 各 ~40 キー）、LocalizationUsageTests / UiTextTests 対応

### Phase C: 実機検証（acc 組織）

- ビルド + 全テスト green
- UIA チェック: タブ表示 / 行数突合（`sf data query` の ObjectPermissions 行数と比較）/ 列数 / 種類フィルタ / ユーザー検索 / 3 行スクロール / SOQL 実行 → ページング → アクセス列表示 / リンク URL 形式
- スクリーンショット取得（任意）

### Phase D: 仕上げ

- PLAN.md（Phase 14）/ README（機能追記）/ バージョン **0.7.0**（csproj + AppxManifest）/ MSIX 再ビルド / コミット

---

## 6. リスク・制約

| # | 内容 | 対応 |
|---|---|---|
| 1 | UserRecordAccess は 200 行 / クエリ上限 | ページ = 200 件 × ユーザー毎 1 クエリ（上限内）。ユーザー多数時の並列は 4 まで |
| 2 | UserId は単一指定のみ（IN 不可） | ユーザー毎にクエリ（キャッシュで再クエリ抑制） |
| 3 | PSG のミューティング未考慮 | 構成 PS の和集合で近似（設計上の割り切り・凡例に注記） |
| 4 | 項目アクセスの大量列（PS 160 + PSG 18） | 種類フィルタ + 列絞り込み + DataGrid 仮想化 |
| 5 | FieldPermissions の行なし = 明示付与なし | 凡例に注記（標準項目の既定アクセスは含まない） |
| 6 | 対象 SOQL が大量レコードを返す | 10,000 件で打ち切り + 注記表示 |
