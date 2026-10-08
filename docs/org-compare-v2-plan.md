# 複数組織比較ウィンドウ拡張 設計書（v2）

最終更新: 2026-10-08 / ステータス: **Phase 5 完了（全フェーズ完了・v0.13.0 リリース済み）**
対象: 既存「組織比較」ウィンドウ（`CompareOrgsWindow`）の拡張 — ユーザー確定（2026-10-08）により**新規ウィンドウは作らない**

---

## 1. 目的

組織情報ウィンドウと同等の内容（全 18 セクション + オブジェクト項目）を**最大 8 組織**で並べて比較し、
差異をハイライトする。修正作業をしやすくするための URL リンク、再取得、レコード比較を追加する。

## 2. 確定事項（ユーザー回答 2026-10-08）

| 項目 | 決定 |
|---|---|
| 既存ウィンドウとの関係 | **既存の「組織比較」ウィンドウを拡張**（新規ウィンドウは作らない） |
| レコード比較 | **行 = レコード**（セルに各組織の値を表示・差異ハイライト）+ **差分詳細ウィンドウ**（項目単位） |
| タブ構成 | **全セクション + オブジェクト項目 + レコード比較 + 統計**（時系列系も含める） |

## 3. 要件と対応

| # | 要件 | 対応 |
|---|---|---|
| 1 | 組織情報ウィンドウと同じくらいの内容を並べて比較 + 差異ハイライト | カテゴリ 13 → 21（+6 静的セクション、+オブジェクト項目、+レコード比較）。行単位の黄色ハイライト（既存）+ セル状態表示（既存）を維持 |
| 2 | 最大 8 組織 | `OrgCompareStateStore.MaxOrgs` 4 → **8**（超過時はチェックが戻りメッセージ表示・既存ガード） |
| 3 | URL リンク（変更しやすいように） | 各セルに **↗ ボタン**: 行の `OrgInfoRow.Link`（ユーザー / プロファイル / オブジェクト等）を優先し、無ければ**その組織のセクション Setup URL**（`OrgInfoUrlBuilder.ForSection`）にフォールバック。通貨は新規に Setup URL を追加 |
| 4 | 再取得 | 既存「このタブを再取得」+ **「すべて再取得」** を追加（全タブを順に強制再取得） |
| 5 | レコードの比較 | ✅ レコード比較タブ（Phase 3）+ 差分詳細ウィンドウ（Phase 4）完了 |
| 6 | ほかに有用な機能 | タブの差分件数バッジ（≠N）・組織列ヘッダー · 既存の差分のみフィルタ / タブ内検索 / CSV / 状態保存を全カテゴリで利用可 |

## 4. フェーズ計画

- **Phase 1（本コミット）**: 8 組織 / 追加セクション 6 種（スケジュール済みジョブ・接続アプリ・インストール済みパッケージ・通貨・ログイン履歴・設定変更履歴）/ セル URL リンク + Currencies Setup URL / すべて再取得 / 差分バッジ / テスト更新
- ✅ **Phase 2（完了）**: オブジェクト項目タブ — 動的カテゴリ `fields:<Object>` + オブジェクト選択（objects セクションから候補取得・選択を状態保存・選択で再比較。カテゴリは不変のためタブを差し替える方式）
- ✅ **Phase 3（完了）**: レコード比較タブ — 対象オブジェクト / 照合キー（Id・Name・任意項目）/ 比較項目（複数選択）/ 件数上限（既定 200・最大 2000）。各組織で REST SOQL 実行 → キーで突合（大文字小文字無視）。セル = 項目値（`ラベル: 値` ・ 連結）、↗ = レコードページ（`{instanceUrl}/lightning/r/{Object}/{Id}/view`）。差分カウントはバッジに表示。`--smoke-compare-records` で実組織検証（hks4sand1 2 件 / acc 15 件 / 17 行・17 差分）
- ✅ **Phase 4（完了）**: レコード差分詳細ウィンドウ（WPF / Avalonia）— 行 = 項目、列 = 組織、差異セルをハイライト（1 レコード分）。行のダブルクリックまたは「詳細を表示」ボタンで開く。空値は「（空）」表示（レコードなしの — と区別）
- ✅ **Phase 5（完了）**: 実組織 E2E（UIA スクリプト 2 本で描画・操作検証）+ リリース v0.13.0（タグ `v0.13.0` / コミット `1705d6c` / GitHub Release id=406468953・アセット 5 点〔exe・portable・MSIX・cer・公証済み osx-arm64〕。CI green）。README EN/JA に新スクリーンショット（compare-records / compare-records-detail）と機能説明を反映

## 5. 実装メモ（Phase 1）

- `OrgCompareOrgColumn` に `InstanceUrl` を追加（セルリンクの Setup URL フォールバックに使用）
- `OrgCompareCategories` へ 6 カテゴリ追加。突合キー / 表示列:
  - スケジュール済みジョブ: key = name / display = jobType, state, nextFireTime
  - 接続アプリ: key = name / display = lastModified
  - インストール済みパッケージ: key = name / display = namespace, version
  - 通貨: key = isoCode / display = name, active, conversionRate
  - ログイン履歴・設定変更履歴: 時系列データ（差分が多くなりがち）— 取得は既存 200 件上限
- セル ↗: `CompareCellViewModel.Link` / `HasLink` を追加し、WPF / Avalonia の動的列テンプレートにボタンを追加（クリックは `CompareCategoryViewModel.OpenLinkRequested` → 親 VM が `ToolLauncherService.LaunchBrowser`）
- すべて再取得: `CompareOrgsViewModel.RefreshAllAsync`（現在の `LoadCategoryAsync` を全カテゴリへ順次適用）
- 差分バッジ: `CompareCategoryViewModel.BadgeText`（"≠N"）/ `HasBadge`。タブ ヘッダーに黄色バッジ

## 6. 実装メモ（Phase 2）

- 動的カテゴリ: `OrgCompareCategories.IsDynamic()`（`fields:` プレフィックス + オブジェクト名が非空）と `CreateFields(objectApiName)`（Keyed・key = API 名・表示列 = ラベル/型/カスタム/参照先/必須/Nillable/参照項目数/インデックス/計算/履歴/説明）。オブジェクト候補は `objects` セクションから取得（`OrgCompareService.EnsureSectionAsync` = キャッシュ優先）
- タブは不変のため**選択時にタブを差し替える**（`CompareOrgsViewModel.CreateFieldsTab`）。選択オブジェクトは `OrgCompareState.FieldsObject` として状態保存。カテゴリ ID `fields:<Object>` は `OrgCompareStateStore.Sanitize` が保持（`IsDynamic` 判定。復元時に Overview へ落ちる不具合を修正）
- ビュー: WPF `CompareFieldsCategoryView`（ツールバー = ComboBox、`TextSearch.TextPath="Display"`）+ Avalonia `CompareFieldsCategoryView`（AutoCompleteBox、`MinimumPrefixLength="0"` / `FilterMode="Contains"`）。本体は既存 `CompareCategoryView` を内包
- UiText 追加: `Compare_FieldsTabFmt`（`項目: {0}`）/ `Compare_FieldsObjectLabel` / `Compare_FieldsSelectObject` / `Compare_FieldsNoObjects` ×4 言語
- テスト +4（CreateFields 構築・API 名突合・FieldsObject ラウンドトリップ・動的カテゴリ ID 保持）= **510 件グリーン**。UIA 検証: 「Fields: Account」タブ選択・97 行 / 49 差分

## 7. 実装メモ（Phase 3）

- Core `OrgRecordCompareService`（新規）: `QueryAllAsync`（組織順・失敗は Failed で継続）/ `QueryOrgAsync` / `BuildSoql` / `BuildTable`（純関数）。SOQL 識別子は「英字で始まる英数字と _」のみ許可（不正は ArgumentException）・重複列は除外・上限は 1〜2000 にクランプ（既定 200）
- 突合: キーは大文字小文字を無視・キー空値は Id にフォールバック。セル = `ラベル: 値` を ・ 連結（空値項目は省略）、↗ = レコードページ（`OrgInfoUrlBuilder.RecordOrNull`）。1 組織の失敗セルは差分判定から除外（既存 IsRowDiff の規約）
- 結果はキャッシュしない（実行のたびに最新を取得・1 ページ目のみ。queryMore / Bulk 対応は将来課題）。
- 動的カテゴリ `records:<Object>`: `OrgCompareCategories.CreateRecords` + `IsDynamic` が `records:` も動的扱い（状態復元で維持）。オブジェクト変更でタブ差し替え（照合キー・比較項目はリセット・上限は維持）
- VM `CompareRecordsCategoryViewModel`（新規）: 照合キー候補 = Id / Name / 全項目、比較項目 = 全項目から複数選択（既定 = Name + 先頭数件・保存値があればそれを復元）。項目メタデータは `fields:<Object>` セクションを再利用（`EnsureSectionAsync` = キャッシュ優先）
- 親 VM: `LoadRecordsTabAsync`（候補確保 → メタデータ → 各組織 REST SOQL → BuildTable → Apply）。言語切替では再クエリしない（セルは組織データのみ）。「比較実行」で再クエリし、選択パネルは自動で畳む
- 状態: `RecordObject` / `RecordKeyField` / `RecordFields` / `RecordLimit`（Sanitize で正規化・上限クランプ）
- UiText 追加 12 キー ×4 言語（OrgInfo_Tab_Records / Compare_RecordsTabFmt / Compare_KeyFieldLabel / Compare_LimitLabel / Compare_CompareFieldsLabel / Compare_RunButton / Compare_RecordsSelectObject / Compare_RecordsNoFields / Compare_RecordsNoMetadataFmt / Compare_RecordsFetchingFmt / Compare_RecordKeyHeader(Fmt)）
- テスト +8 = **518 件グリーン**。UIA 検証（`C:\SfUiDemo\sfui-compare-records-check.ps1` → `compare-records.png`）: タブ選択・17 行/17 差分・チェックボックス 62 件・Type を外して「Compare」再実行でセルから `Type:` が消えることを確認

## 8. 実装メモ（Phase 4）

- Core `OrgRecordCompareService.BuildDetail`（純関数）: 行 = 比較項目（ラベル・API 名）、列 = 組織。空値は「（空）」表示（レコードなしの — と区別）。レコードなし / 取得失敗のセルはそのまま引き継ぎ、`IsRowDiff` で差分行を判定。対象キーが表に無ければ null。`CompareRecordDetailModel` を返す
- Presentation `CompareRecordDetailViewModel`: タイトル「レコード詳細: {キー}」/ 要約「{object} / {n} 項目 / 差分 {m} 件」。行は `CompareRowViewModel` を再利用
- 起動経路: ①行のダブルクリック（`CompareCategoryViewModel.OnRowActivated` 仮想メソッド → レコード比較タブが `DetailRequested` → 親 VM が `IAppWindowService.OpenCompareRecordDetail`）②「詳細を表示」ボタン（`SelectedRow` 選択時のみ有効）。既存の比較表から組み立てるため再クエリなし
- ウィンドウ: WPF `CompareRecordDetailWindow`（+ `CompareRecordDetailWindowFactory`・DI 登録）/ Avalonia `CompareRecordDetailWindow`（`AvaloniaAppWindowService` から開く）。列は組織数に応じて動的生成・差分行は黄色ハイライト・セルツールチップに全文
- UiText 5 キー ×4（Compare_DetailTitleFmt / Compare_DetailHeaderFmt / Compare_DetailButton / Compare_DetailTip / Compare_EmptyValue）
- テスト +2 = **520 件グリーン**。UIA 検証（`sfui-compare-records-check.ps1` → `compare-records-detail.png`）: 行選択 →「Record detail: 456」/「Account / 3 fields / 3 diffs」/(empty) と — の区別を確認

## 9. リスク・注意

- 8 組織 × 全タブの比較はキャッシュ（組織情報と共有）の分だけディスクが増えるが、セクション単位の JSON のため許容
- ログイン履歴 / 設定変更履歴は「同じでないのが正常」なデータのため、差分だらけになる（必要なら差分のみフィルタやタブ内検索で絞る）
- レコード比較（Phase 3）は 1 組織あたり上限 200 件を既定とする（最大 2000・1 ページ目のみ）。Bulk / queryMore 対応は将来課題
