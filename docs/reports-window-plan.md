# レポート（ローカル Salesforce レポート）設計書

最終更新: 2026-10-28 / ステータス: **Phase 1〜3 + Phase 4 のスケジューラ連携まで完了（Core + WPF + Avalonia）** — 残りは帳票レイアウト（§7.4 / §8）

対象リポジトリ: `c:\huqian\vscode\SfUi`（.NET 9 / SfUi.Core + SfUi.Presentation + WPF + Avalonia）

---

## 1. 目的とスコープ

SfUi 内に **Salesforce の「レポート」に相当する集計・一覧機能** を追加する。レポート定義（オブジェクト・項目・条件・グループ・集計）をローカルに保存し、SOQL で実行して結果を表示する。**リアルタイム更新**（数秒間隔の自動再実行）と、**できるだけ多い形式でのダウンロード**に対応し、将来の **SVF 風帳票（レイアウト + 差し込み + PDF）** のデータ供給元としても使える構造にする。

### 1.1 ユーザー要件（原文）

1. ローカルで Salesforce のレポート機能を実現したい
2. リアルタイム更新モードもほしい（数秒間隔でデータ更新とか）
3. ダウンロードする形式をなるべく多くサポートしたい
4. 今後作成予定の SVF みたいな帳票機能にも使うと考慮（現段階では実装不要だが、設計上考慮する）

### 1.2 Salesforce 機能との対応

| Salesforce | 本機能 | Phase |
| --- | --- | --- |
| Reports タブ / 保存済みレポート | レポート一覧（`data/reports/reports.json`） | 1 |
| Report Builder（項目 / 条件 / グループ / 集計 / 表示形式） | レポート エディタ | 1–2 |
| 実行（Tabular / Summary / Matrix） | SOQL 生成 + クライアント集計 | 1–2 |
| レポートのエクスポート | CSV / TSV / Excel / JSON / JSONL / XML / YAML / Markdown / HTML / PDF / 画像 | 1–2 |
| リアルタイム更新（ダッシュボード相当） | タイマー再実行 + 差分ハイライト | 2 |
| ダッシュボード / グラフ | **対象外**（Phase 4 以降の候補） | — |
| 組織に保存された Lightning レポートの実行（Reports API） | **対象外**（ローカル定義のみを実行） | — |

> 「ローカルで Salesforce のレポート機能を実現」= 組織のレポートを呼び出すのではなく、**同等の定義・集計・表示を SfUi 内で完結**させる方針。

---

## 2. アーキテクチャ

### 2.1 層構成（既存の SfUi 規約どおり）

| 層 | 追加するもの |
| --- | --- |
| `SfUi.Core` | `ReportDefinition` / `ReportColumn` / `ReportFilter` / `ReportGrouping` / `ReportAggregate`、`ReportDefinitionStore`、`ReportQueryBuilder`（SOQL 生成）、`ReportAggregator`（グループ + 集計）、`ReportRunner`（実行 + 差分検出）、出力 writer（JSON/JSONL/XML/YAML/TSV/画像）、`ReportLayoutDefinition`（SVF 用の器） |
| `SfUi.Presentation` | `ReportsViewModel`（一覧 / 実行 / リアルタイム更新 / 出力）、`ReportEditorViewModel`（定義編集）、`IReportSaveDialog` 等の抽象 |
| `SfUi.App`（WPF） | `Views/ReportsWindow.xaml`、`Views/ReportEditorDialog.xaml`、`Services/Wpf*` |
| `SfUi.Avalonia` | 同上（`ReportsWindow.axaml` / `ReportEditorDialog.axaml`） |

既存の **レポート出力基盤（`ReportDocument` / `ReportExportService` / `ReportExportRunner` / 出力ダイアログ）を再利用**する。レポート結果は `ReportDocument` に変換してから書き出すため、SfUi の他画面と同じ出力 UX（形式の複数選択・分析セクション・保存先記憶）をそのまま使える。

### 2.2 Core モデル（案）

```csharp
public sealed class ReportDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string ObjectName { get; set; } = "Account";     // sObject API 名
    public string? TargetOrg { get; set; }                   // null = 現在の組織
    public List<ReportColumn> Columns { get; set; } = new();  // 表示順 = 列順
    public List<ReportFilter> Filters { get; set; } = new();
    public List<ReportSort> Sorts { get; set; } = new();
    public List<ReportGrouping> Groupings { get; set; } = new(); // 最大 3 階層
    public List<ReportAggregate> Aggregates { get; set; } = new();
    public int RowLimit { get; set; } = 2000;
    public bool UseBulk { get; set; }                     // 大量行は Bulk クエリ
    public int RefreshSeconds { get; set; }               // 0 = リアルタイム更新オフ
    public string DisplayMode { get; set; } = "tabular";  // tabular / summary / matrix
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ReportColumn
{
    public string Field { get; set; } = "Id";   // "Name" / "Account.Name" / "Owner.Alias"
    public string? Label { get; set; }          // 空 = メタデータのラベル
    public bool Hidden { get; set; }
    public string Format { get; set; } = "auto"; // auto/text/number/currency/percent/date/datetime/bool/url
    public int? Width { get; set; }
}

public sealed class ReportFilter
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "eq"; // eq/ne/lt/lte/gt/gte/contains/startsWith/in/isNull/notNull/dateLiteral
    public string? Value { get; set; }
    public string? Value2 { get; set; }          // 範囲条件用
    public bool IncludeNulls { get; set; }
    public string Logic { get; set; } = "and";   // and / or（グループ単位）
}

public sealed class ReportGrouping { public string Field { get; set; } = ""; public string SortOrder { get; set; } = "asc"; public string? DateGranularity { get; set; } }
public sealed class ReportAggregate { public string Field { get; set; } = ""; public string Function { get; set; } = "sum"; public string? Label { get; set; } }
```

- **保存**: `data/reports/reports.json`（`AtomicJsonFile` + `SchemaVersion`。既存ストアと同じ `Upsert/Delete/Find/Changed` パターン）
- **インポート / エクスポート**: 定義 1 件を JSON で書き出し・読み込み（複数 PC 間の共有、AI への受け渡し）

### 2.3 実行パイプライン

```
ReportDefinition
  → ReportQueryBuilder.Build()        … SELECT 列 + グループ項目 + 集計対象 / WHERE / ORDER BY / LIMIT (RowLimit + 1)
  → SoqlService.ExecuteSoqlAsync()    … 既存の REST 優先 + sf フォールバック
  → ReportAggregator.Aggregate()      … グループ化・小計/合計・件数、表示用の整形（DataTable → ReportResult）
  → ReportResult                      … Table / Groups / Totals / RowCount / Truncated / Duration / Engine
  → ReportDocumentFactory.FromReport() … 既存 ReportDocument へ変換
  → ReportExportService / 新しい writer … 各形式で出力
```

- **集計は原則クライアント側**（SOQL の `GROUP BY` は使わない）。理由: Salesforce レポートの「グループ + 小計 + 明細」表示を再現しやすく、項目メタデータ（ラベル・型）と組み合わせた書式設定ができるため。大量行時のみ `UseBulk`（Bulk API 2.0 クエリ）にフォールバック。
- `LIMIT` は `RowLimit + 1` で取得し、超過時は **「行数上限で切り捨て」** を明示（サイレント切り捨てを避ける）。
- リレーション項目（`Account.Name` など）は SOQL にそのまま渡し、表示ラベルは `SObjectDescribeService` のメタデータから解決（キャッシュ済み）。

### 2.4 リアルタイム更新

| 項目 | 仕様（案） |
| --- | --- |
| 間隔 | レポートごとに `RefreshSeconds`（既定 **10 秒**、最小 **2 秒**、最大 3600 秒）。「オフ」も選択可 |
| 実行条件 | レポート ウィンドウが**表示中かつ結果タブがアクティブ**なときのみ。最小化・非表示・別ウィンドウ操作中は停止 |
| 多重実行 | 前回の実行が終わるまで次を開始しない（`SemaphoreSlim(1)`）。キャンセルは新しい実行開始時に前を中断 |
| 失敗時 | 指数バックオフ（2 → 4 → 8 … 最大 60 秒）。連続失敗回数をステータスに表示し、3 回で自動停止（手動再開可） |
| 差分表示 | 主キー（Id 列。無ければ全列ハッシュ）で比較し、**追加 = 緑 / 変更 = 黄 / 削除 = 赤（薄く）**。差分件数をステータスに表示 |
| 表示 | 「最終更新 / 次回更新まで / 更新回数 / 実行時間」を常時表示。「今すぐ更新」「一時停止」ボタン |
| 負荷対策 | 選択項目の最小化、`LIMIT` 固定、SOQL 結果キャッシュとは別扱い（キャッシュを使わず常に最新） |

実装は `SchedulerService` と同じ **`TimeProvider` + `PeriodicTimer`** パターンにして、テストはフェイク タイマーで行う。

### 2.5 出力形式

| 形式 | 状態 | 備考 |
| --- | --- | --- |
| CSV | 既存 | `ReportExportService`（表ごとに 1 ファイル） |
| TSV | **追加** | Excel に貼り付けやすい |
| Markdown | 既存 | AI 用（YAML フロント マター付き） |
| HTML | 既存 | 絞り込み・ソート付きの単一ファイル |
| Excel (.xlsx) | 既存 | 概要 + 表ごとのシート |
| PDF | 既存 | ヘッドレス ブラウザーで HTML を印刷 |
| JSON | **追加** | 行オブジェクトの配列（メタデータ + 行 + 集計） |
| JSONL (NDJSON) | **追加** | 1 行 1 レコード。大量データ / ストリーミング向け |
| XML | **追加** | 業務システム連携向け（要素名 = 項目 API 名） |
| YAML | **追加** | 設定ファイル / AI 向け |
| 画像 (PNG / JPEG) | **追加** | レンダリングした帳票/表を画像化（Chromium 依存。失敗時は HTML/PDF にフォールバック） |
| レポート定義 JSON | **追加** | 定義の共有・バックアップ（出力ダイアログとは別ボタン） |

- `ReportFormats`（`[Flags]`）に `Tsv / Json / JsonLines / Xml / Yaml / Png / Jpeg` を追加し、`ReportExportService` に writer を追加（`ReportJsonWriter` / `ReportXmlWriter` / `ReportYamlWriter` / `ReportImageWriter`）。既存の出力ダイアログは形式チェックボックスが増えるだけで、他画面にも同時に効く。
- 出力先は既定で **`data/exports/`**（スケジューラと同じ解決規則）。

### 2.6 SVF 風帳票を見据えた設計（今回は実装しない）

将来の帳票機能が「レポート結果をデータ源として、レイアウトに差し込んで PDF / 印刷する」構図になるため、**データ供給とレンダリングを分離**しておく。

```csharp
/// 帳票・出力の共通入り口（Phase 1 では ReportExportService が唯一の実装）
public interface IReportRenderer
{
    string Id { get; }                        // "document" / "svf" / "html" ...
    Task<ReportExportResult> RenderAsync(ReportDocument document, ReportRenderOptions options, CancellationToken ct);
}

/// 帳票レイアウト定義（Phase 4 で UI を持つ。今はモデル + ストアのみ）
public sealed class ReportLayoutDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PaperSize { get; set; } = "A4";      // A4 / A3 / Letter
    public string Orientation { get; set; } = "portrait";
    public double MarginMm { get; set; } = 12;
    public ReportLayoutRegion? Header { get; set; }    // 差し込みフィールド + 固定文字
    public ReportLayoutRegion? Footer { get; set; }
    public ReportLayoutRegion? Detail { get; set; }    // 1 レコード = 1 明細（繰り返し）
    public ReportLayoutRegion? Summary { get; set; }   // 集計行
    public bool RepeatHeaderOnPageBreak { get; set; } = true;
    public string? FontFamily { get; set; }
}
```

- レイアウトは **JSON 定義**（`data/reports/layouts.json`）として保存し、将来 UI で編集できる形にする。
- `ReportResult` は「表形式（一覧）」と「レコード スナップショット（辞書のリスト）」の両方を返せるようにし、**明細型帳票**と**一覧表帳票**のどちらにも供給できる。
- SVF 連携は将来的に **(a) 独自レイアウト → PDF/HTML/画像** と **(b) SVF の帳票定義 CSV 互換のデータ出力** の 2 方式を選択できるよう、`IReportRenderer` を差し替え可能にする。
- Phase 1–2 では `IReportRenderer` / `ReportLayoutDefinition` の**型とストアだけ**用意し、UI と SVF レンダラは Phase 4。

### 2.7 UI（WPF / Avalonia 共通 ViewModel）

```
┌──────────────────────────────────────────────────────────────────────┐
│ [新規] [複製] [削除] [編集] [出力▼]        最終更新 12:34:56 / 10 秒毎 │
├───────────────┬──────────────────────────────────────────────────────┤
│ レポート一覧   │ 結果（タブ区切り / グループ表示）                       │
│ ・営業案件 一覧│  グループ: 取引先  (小計 12 件 / ¥1,200,000)           │
│ ・未完了タスク  │   行 行 行 …                                        │
│ ・月別 売上    │ 合計 (123 件 / ¥45,600,000)                          │
├───────────────┴──────────────────────────────────────────────────────┤
│ 行数 123 / 実行 0.8s / REST / [リアルタイム更新 ●] [間隔 10s] [今すぐ] │
└──────────────────────────────────────────────────────────────────────┘
```

- 上部バーに **「レポート」** ボタン（`OpenReports`、カレンダー アイコンの隣）を追加。
- 一覧（検索 / 新規 / 複製 / 削除 / 定義の入出力）、結果グリッド（グループの折りたたみ・合計行）、エディタ（オブジェクト・項目・条件・グループ・集計・上限・更新間隔）。
- 出力は既存の出力ダイアログを再利用（形式の複数選択・分析セクション・保存先記憶）。
- `AutomationProperties.AutomationId` を付与（`ReportsList` / `ReportsRun` / `ReportsLive` / `ReportsGrid` など）、ヘルプ ボタン（マニュアル第 19 章）、4 言語の UiText。

---

## 3. 段階計画

| Phase | 内容 | 規模 |
| --- | --- | --- |
| **1. Core 基盤** | モデル / ストア / SOQL 生成 / 実行 / グループ + 集計 / `ReportDocument` 変換 / 出力形式の追加（JSON・JSONL・XML・YAML・TSV）/ 単体テスト | 中 |
| **2. WPF UI** | レポート ウィンドウ（一覧 + 結果 + エディタ）/ リアルタイム更新 / 出力ダイアログ連携 / 履歴・お気に入り / E2E | 大 |
| **3. Avalonia 移植** | 同一 ViewModel で `ReportsWindow.axaml` / エディタ / UIA E2E | 中 |
| **4. 帳票 / 連携** | `ReportLayoutDefinition` の UI、`IReportRenderer` の SVF 風レンダラ（PDF/HTML/画像）、スケジューラの `Report` ジョブ種別、マニュアル | 大 |

---

## 4. テスト方針

- **クエリ生成**: フィルタ演算子 × 型（数値 / 文字列 / 日付 / bool / null）の組み合わせ、リレーション項目、`ORDER BY`、`LIMIT + 1` をテーブル駆動テスト（実組織不要）。
- **集計**: 単一 / 複数グループ、小計・合計、`count / countDistinct / sum / avg / min / max`、空グループ、null の扱い。
- **出力**: 各形式のヘッダー・行数・エスケープ（XML/CSV の引用）・JSON スキーマをスナップショット検証。
- **リアルタイム更新**: フェイク `TimeProvider` で間隔・バックオフ・多重実行防止・差分検出を検証。
- **UI**: `--smoke`（WPF / Avalonia）+ UIA E2E（レポート作成 → 実行 → リアルタイム更新 → 出力）。

## 5. リスクと対策

| リスク | 対策 |
| --- | --- |
| 大量行で UI が固まる | `RowLimit`（既定 2,000）+ 切り捨て警告、`UseBulk` フォールバック、表示は仮想化グリッド |
| リアルタイム更新で API 制限に達する | 最小 2 秒 + 表示中のみ + 失敗時バックオフ + 連続失敗で自動停止 |
| 項目メタデータの取得が遅い | 既存の `MetadataCacheStore`（組織ごとキャッシュ）を使用 |
| 画像 / PDF 出力が環境依存 | Chromium が見つからない場合は HTML へフォールバックし、理由を表示 |
| 通貨 / 日付の書式が組織依存 | 組織のロケール設定は取得できないため、UI 設定（通貨記号・日付書式）として保持 |
| 帳票要件が固まっていない | Phase 1 で `IReportRenderer` / `ReportLayoutDefinition` の境界だけ確保し、後から UI / SVF レンダラを追加 |

---

## 6. 確認したいこと（決定事項）

| # | 論点 | 推奨 |
| --- | --- | --- |
| 1 | レポート定義の作り方 | **ビルダー UI**（オブジェクト選択 + 項目追加 + 条件 + グループ + 集計）。SOQL 直書きモードも将来追加 |
| 2 | リアルタイム更新の既定 / 最小間隔 | 既定 **10 秒** / 最小 **2 秒**、ウィンドウ非表示時は停止 |
| 3 | 追加する出力形式 | JSON / JSONL / XML / YAML / TSV / 画像（PNG・JPEG）＋既存の CSV / Markdown / HTML / Excel / PDF |
| 4 | 帳票（SVF 風）の方向性 | 独自 JSON レイアウト → PDF/HTML/画像 を主とし、SVF 定義 CSV 互換のデータ出力も可能な構造にする |
| 5 | スケジューラ連携 | Phase 4 でジョブ種別「レポート実行」を追加（結果を CSV/Excel/PDF で保存） |

---

## 7. Phase 1〜2 実装状況（2026-10-28）

スケジューラと同じ構成で **Core（レポート エンジン）＋ WPF UI** を実装した。Avalonia は `OpenReports` が未移植メッセージを出すのみ（Phase 3）。

### 7.1 追加したファイル

| 層 | ファイル | 役割 |
| --- | --- | --- |
| Core | `Reports/LocalReportModels.cs` | `ReportDefinition` / 列 / 条件 / 並べ替え / グループ / 集計 / `LocalReportResult` / `ReportDocument` 変換 / 演算子・集計関数・表示モード・書式の定義 |
| Core | `Reports/ReportQueryBuilder.cs` | 定義 → SOQL（`SELECT` 項目の自動補完、`WHERE`（AND/OR）、`ORDER BY`、`LIMIT+1`（切り捨て検出））と検証 |
| Core | `Reports/ReportAggregator.cs` | グループ化・小計・合計・件数・`countDistinct`、セル整形 |
| Core | `Reports/ReportValueFormatter.cs` | 数値 / 通貨 / パーセント / 日付 / 真偽値 / 自動判定、日付粒度（日・週・月・四半期・年） |
| Core | `Reports/LocalReportRunner.cs` | 検証 → SOQL 実行 → 集計、`ReportDiff`（Id か全列で追加 / 変更 / 削除を判定） |
| Core | `Reports/ReportLivePolicy.cs` | リアルタイム更新（既定 10 秒 / 最小 2 秒 / 最大 1 時間、2→4→8… 最大 60 秒のバックオフ、3 回連続失敗で自動停止） |
| Core | `Reports/ReportDataWriter.cs` | TSV / JSON / JSONL / XML / YAML |
| Core | `Reports/ReportImageWriter.cs` | PNG / JPEG（SkiaSharp でローカル描画。ブラウザー不要） |
| Core | `Reports/ReportLayoutDefinition.cs` | SVF 風帳票用の `ReportLayoutDefinition` / `IReportRenderer`（型とストアの器のみ） |
| Core | `Storage/ReportDefinitionStore.cs` | `data/reports/reports.json`（原子書き込み） |
| Presentation | `ViewModels/ReportEditorViewModel.cs` | レポート ビルダー（オブジェクト / 列 / 条件 / グループ / 集計 / 並べ替え / 上限 / 更新間隔 / 表示モード） |
| Presentation | `ViewModels/ReportsViewModel.cs` | 一覧 / 実行 / リアルタイム更新 / 差分 / 出力 / 複製 / 削除 |
| WPF | `Views/ReportsWindow.xaml` / `Views/ReportEditorDialog.xaml` / `Services/{WpfReportEditor,ReportsWindowFactory}.cs` | UI（`AutomationId`: `OpenReports` / `ReportsNew` / `ReportsRun` / `ReportsLive` / `ReportsGrid` / `ReportsExport` 等） |

### 7.2 利用方法

1. 上部バーの **レポート** アイコン → レポート ウィンドウ。
2. **新規レポート** → オブジェクト（既定 Account）→ フィールドを選んで「追加」→ 条件 / グループ / 集計 / 並べ替え / 行数上限 / リアルタイム更新間隔（秒、0 = オフ）を設定 → 保存。
3. **今すぐ実行**（保存時に自動実行）→ 結果グリッド・グループ集計・合計・行数 / 実行時間 / エンジン表示。
4. **リアルタイム更新** をオンにすると指定間隔（最小 2 秒）で再実行し、次回時刻を表示。連続失敗時は自動停止。
5. **出力** で CSV / TSV / Markdown / HTML / Excel / PDF / JSON / JSONL / XML / YAML / PNG / JPEG を選択して `data/exports/` へ書き出し。

### 7.3 検証

| 項目 | 結果 |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test` | **1,323 件すべて成功**（+28: クエリ生成・集計・出力形式・ストア・更新ポリシー・差分） |
| WPF / Avalonia `--smoke` | 両方 exit 0 |
| UI E2E | `C:\huqian\sfui-reports-e2e.ps1` → **16/16 PASS**（レポート作成 → 実行 136 行 → グループ表示 → 出力ダイアログの新形式 → リアルタイム更新 7 回） |

### 7.4 残り（Phase 4）

- 帳票レイアウトの UI と `IReportRenderer` の SVF 風レンダラ（PDF/HTML/画像）
- チャート / ダッシュボード

スケジューラのジョブ種別「レポート実行」は **§8 で完了**（2026-10-28）。

### 7.5 Phase 3（Avalonia 移植・2026-10-28）

| 項目 | 内容 |
| --- | --- |
| ウィンドウ | `src/SfUi.Avalonia/Views/ReportsWindow.axaml`（結果グリッドの列は `GridColumns` に合わせてコードビハインドで動的生成。行は `ReportGridRow` のインデクサーにバインド） |
| エディタ | `src/SfUi.Avalonia/Dialogs/ReportEditorDialog.axaml`(+`.cs`) / `Services/AvaloniaReportEditor.cs`（`ShowDialog<ReportDefinition?>`） |
| 起動 | `AvaloniaAppWindowService.OpenReports` → `ReportsWindow`、`MainWindow.axaml` に `OpenReports` ボタン追加、`IconReport` 追加 |
| リアルタイム更新 | チェックボックスは WPF = `Command` 駆動 / Avalonia = `IsChecked` 双方向（UIA の Toggle が Command を発火しないため）。VM は `OnIsLiveChanged` で開始 / 停止するのでどちらでも動く |
| 検証 | `C:\huqian\sfui-reports-e2e-avalonia.ps1` → **15/15 PASS**（レポート作成 → 136 行 → 出力形式 → リアルタイム更新 3→9 回）。WPF 版も 15/15 |

---

## 8. Phase 4-2: スケジューラのジョブ種別「レポート実行」（2026-10-28）

保存済みのローカル レポートをスケジュール実行し、結果をファイルへ書き出す。

| 層 | ファイル | 役割 |
| --- | --- | --- |
| Core | `Scheduling/SchedulerModels.cs` | `ScheduledJobTypes.Report = "report"`（`All` の データ エクスポート の次）、`ReportJobPayload { ReportId, Formats, OutputPath, MaxRows }` |
| Core | `Reports/LocalReportRunner.cs` | `ILocalReportRunner` を抽出（`LocalReportRunner` が実装。DI で両方登録） |
| Core | `Scheduling/Executors/ReportJobExecutor.cs` | 定義を `ReportDefinitionStore.Find` で解決 → 実行 → `ReportDocument` → `ReportExportService` で出力。相対パスは `data/exports/` 基準、空なら `レポート名-yyyyMMdd-HHmmss`。`ParseFormats("csv,excel,pdf")` を公開 |
| Presentation | `ViewModels/SchedulerJobEditorViewModel.cs` | レポート選択（`ReportChoices`）/ 出力形式（7 種）/ 出力ファイル（`ReportPathHint` で解決後パスを表示）、`IsReport`、検証 `Sched_ErrorNoReport` |
| WPF / Avalonia | `Views/SchedulerJobEditorDialog.xaml` / `Dialogs/SchedulerJobEditorDialog.axaml` | `JobEditorReport` / `JobEditorReportFormat` / `JobEditorReportOutput` / `JobEditorReportOutputHint` |
| UiText | `UiText.{En,Ja,Zh,Ko}.cs` | `Sched_TypeReport` / `Sched_EditorReport` / `Sched_EditorReportFormat` / `Sched_EditorReportOutput` / `Sched_ErrorNoReport` / `Sched_ErrorReportNotFound` / `Sched_SummaryReportFmt` |

検証:

| 項目 | 結果 |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test` | **1,334 件すべて成功**（+11: 形式パース / 実行 / 出力先 / 未選択・未存在エラー / エディタのペイロード往復） |
| WPF / Avalonia `--smoke` | 両方 exit 0 |
| UI E2E (WPF) | `C:\huqian\sfui-report-job-e2e.ps1` → **21/21 PASS**（種別に Report → レポート一覧 → 形式 7 種 → 出力ヒント → 保存された `payloadJson`） |
| UI E2E (Avalonia) | `C:\huqian\sfui-report-job-e2e-avalonia.ps1` → **13/13 PASS** |

---

## 9. Phase 4-1: 帳票（SVF 風）レンダラ + レイアウト エディタ（完了・2026-10-28）

| 層 | ファイル | 役割 |
| --- | --- | --- |
| Core | `Reports/ReportFormWriter.cs` | `ReportPapers`（A4 / A3 / Letter + 縦横）、`ReportFormLayout`（`RowsPerPage` / `PageCount` / `Substitute`）、`ReportFormWriter.Write`（ページ分割した帳票 HTML。`{Title}` `{Org}` `{Date}` `{Page}` `{Pages}` `{Rows}` `{Table}` を差し込み、明細は `document.Tables[0]` をページごとに繰り返す） |
| Core | `Reports/ReportLayoutDefinition.cs` | `SvfReportRenderer : IReportRenderer`（`Id = "svf"`、HTML + PDF）、`ReportExportOptions.Layout`、`ReportLayoutRegion.RowHeightMm`、`ReportLayoutDocument.DefaultLayoutId` |
| Core | `Storage/ReportLayoutStore.cs` | `data/reports/layouts.json`（`Upsert` / `Find` / `FindDefault` / `SetDefault` / `Delete`、既定レイアウトの自動作成 `CreateDefault`） |
| UiText | `UiText.{En,Ja,Zh,Ko}.cs` | `Report_RendererSvf` / `Report_FormNoLayout` / `Report_FormNoRows` / `Report_FormSummaryFmt` |
| Presentation | `UI/IReportFormDialog.cs` / `ViewModels/ReportFormViewModel.cs` | レイアウト選択・新規・削除・保存、用紙 / 向き / 余白 / ヘッダー / 明細行高 / 集計 / フッターの編集、HTML / PDF 出力（`data/exports`） |
| Presentation | `ViewModels/ReportsViewModel.cs` | `ExportFormCommand`（ツールバーの「帳票出力…」）。実行結果が無いときは `Reports_NoSelection` |
| WPF | `Views/ReportFormDialog.xaml` / `Services/WpfReportFormDialog.cs` | `AutomationId`: `ReportsExportForm` / `ReportForm{Layouts,New,Delete,Name,Paper,Orientation,Margin,Header,RowHeight,Summary,Footer,RepeatHeader,OutputName,OutputHint,Html,Pdf,Save,Render,Cancel,Status}` |
| Avalonia | `Dialogs/ReportFormDialog.axaml` / `Services/AvaloniaReportFormDialog.cs` | 同じ AutomationId（`ShowDialog<ReportExportResult?>`） |
| UiText | `UiText.{En,Ja,Zh,Ko}.cs` | `Reports_ExportForm` / `Report_Form*`（19 キー） / `Report_RendererSvf` / `Report_FormNoLayout` / `Report_FormNoRows` / `Report_FormSummaryFmt` |

検証:

| 項目 | 結果 |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test` | **1,348 件すべて成功**（+14: ページ計算・用紙・差し込み・HTML 生成・エスケープ・ストア・レンダラ + VM の保存 / 出力） |
| WPF / Avalonia `--smoke` | 両方 exit 0 |
| UI E2E (WPF) | `C:\huqian\sfui-report-form-e2e.ps1` → **18/18 PASS**（帳票出力 → レイアウト編集 → 保存 → HTML 出力 2 ページ） |
| UI E2E (Avalonia) | `C:\huqian\sfui-report-form-e2e-avalonia.ps1` → **18/18 PASS** |
