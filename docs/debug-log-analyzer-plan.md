# デバッグログ・アナライザー 設計書（C1）

最終更新: 2026-10-08 / ステータス: **Phase 1〜3 完了（単体テスト + 実 org スモーク + UIA 10/10 PASS）**
対象: ログタブ（WPF / Avalonia 両方）+ `SfUi.Core` パーサー

---

## 1. 目的・背景

- 現状のログタブは **一覧・取得・保存・生テキスト表示**のみで、ログを「読み解く」機能がない。
- デバッグでは「なぜ失敗したか」「どこが遅いか」をログから掴む必要があるが、生テキストは数万行に及び読解コストが高い。
- 外部では VS Code の **Certinia「Apex Log Analyzer」** 等の可視化ツールが支持されている
  （フレームチャート / コールツリー / 遅い SOQL・DML / ガバナ制限 / ヒープ）。
  SfUi にもログ取得基盤（`ApexService`）があるため、**解析層を追加**して同等の「読み解き」支援を提供する。

## 2. スコープ（確定）

| 項目 | 決定 |
|---|---|
| 入口 1 | ログタブに **「解析」ボタン** — 現在表示中のログ本文を解析してウィンドウ表示 |
| 入口 2 | ログタブに **「.log ファイルを開く」ボタン** — ローカルのログファイルを読み込み、本文表示 + 解析（組織不要・オフライン可） |
| 表示 | 新規**非モーダル**ウィンドウ `LogAnalyzerWindow`（複数同時表示可、Owner を設定しない既存方針に従う） |
| タブ | **概要 / ツリー / イベント** の 3 タブ |
| 対象外（今後） | フレームチャート、ヒープ詳細ビュー、AI 連携（概要の AI 添付導線は将来拡張） |

## 3. ログフォーマット解析仕様（Salesforce Debug Log）

### 3.1 行形式

```
HH:mm:ss.ffffff (経過ナノ秒)|EVENT_TYPE|details...
```

- 括弧内の **経過ナノ秒** を時間軸として使用（`ElapsedMs = nanos / 1e6`）。
- 複数行イベント: `LIMIT_USAGE_FOR_NS` の直後に続く `  Number of X: y out of z` 形式の継続行を収集。
- 先頭行（`67.0 APEX_CODE,FINEST;...`）はヘッダとして保持（解析対象外）。
- **壊れた行・未知の行は捨てずに「その他」イベントとして保持**（行番号付き）。パーサーは決して throw しない（ベストエフォート）。

### 3.2 カテゴリ分類（イベントフィルタ用）

| カテゴリ | プレフィックス |
|---|---|
| 実行 | `EXECUTION_*`, `CODE_UNIT_*`, `CUMULATIVE_LIMIT_USAGE`, `LIMIT_USAGE_FOR_NS`, `USER_INFO` |
| メソッド | `METHOD_*`, `SYSTEM_METHOD_*`, `CONSTRUCTOR_*`, `STATIC_VARIABLE_LIST`, `VARIABLE_SCOPE_BEGIN/END` |
| SOQL | `SOQL_EXECUTE_*`, `SOSL_EXECUTE_*` |
| DML | `DML_*` |
| コールアウト | `CALLOUT_*` |
| フロー | `FLOW_*`, `WAVE_*`, `WF_*` |
| 例外 | `EXCEPTION_THROWN`, `FATAL_ERROR`, `VALIDATION_FAIL`, `VALIDATION_ERROR` |
| その他 | 上記以外（`USER_DEBUG`, `DEBUG`, `SYSTEM_*`, `HEAP_ALLOCATE`, `STATEMENT_EXECUTE` 等） |

### 3.3 ツリー構築（対応ペア）

スタックベースで以下をネスト。構造ノードのみツリーに載せ、それ以外はイベント一覧にのみ表示。

| 開始 | 終了 | ラベル |
|---|---|---|
| `EXECUTION_STARTED` | `EXECUTION_FINISHED` | 実行 |
| `CODE_UNIT_STARTED` | `CODE_UNIT_FINISHED` | 詳細の最終セグメント（メソッド/トリガ名） |
| `METHOD_ENTRY` | `METHOD_EXIT` | `[行] クラス.メソッド()` |
| `SYSTEM_METHOD_ENTRY` | `SYSTEM_METHOD_EXIT` | 同上 |
| `CONSTRUCTOR_ENTRY` | `CONSTRUCTOR_EXIT` | 同上 |
| `SOQL_EXECUTE_BEGIN` | `SOQL_EXECUTE_END` | クエリ本文（200 文字で切詰）+ 行数（END の `Rows:n`） |
| `SOSL_EXECUTE_BEGIN` | `SOSL_EXECUTE_END` | 同上 |
| `DML_BEGIN` | `DML_END` | `Op:Insert Type:Account Rows:1` を整形 |
| `CALLOUT_REQUEST` | `CALLOUT_RESPONSE` | エンドポイント + ステータス |
| `FLOW_START_INTERVIEWS_BEGIN` | `FLOW_START_INTERVIEWS_END` | フロー |
| `FLOW_ELEMENT_BEGIN` | `FLOW_ELEMENT_END` | 要素名 |

- 所要時間 = 終了イベント経過ns − 開始イベント経過ns（ms 表示）。
- 不完全ペア（終了なし）は閉じずに最後まで保持し、所要時間は「−」表示。

### 3.4 集計（サマリ）

| 項目 | 算出方法 |
|---|---|
| 合計時間 | `EXECUTION_STARTED`→`EXECUTION_FINISHED`（無ければ最終イベントの経過ns） |
| イベント数 | 全イベント行数 |
| SOQL | 件数 = `SOQL_EXECUTE_BEGIN` / 行数 = `SOQL_EXECUTE_END` の `Rows:` 合計 |
| DML | 件数 = `DML_BEGIN` / 行数 = `DML_BEGIN` の `Rows:` 合計 |
| コールアウト | `CALLOUT_REQUEST` 件数 |
| 例外 | `EXCEPTION_THROWN` + `FATAL_ERROR`（種別・メッセージ・行番号） |
| ガバナ制限 | **最後の** `CUMULATIVE_LIMIT_USAGE` ブロック（無ければ最後の `LIMIT_USAGE_FOR_NS`）。名前空間×名前で最新値を採用 |
| 遅い処理 | 構造ノード（メソッド/SOQL/DML/コールアウト/フロー）を所要時間降順 Top 15 |

## 4. Core モデル / パーサー

新規ファイル:

- `src/SfUi.Core/Services/DebugLogModels.cs`
  - `DebugLogEventCategory`（enum: Execution/Method/Soql/Dml/Callout/Flow/Exception/Other）
  - `DebugLogEvent`（LineNumber / ElapsedNanos / EventType / Category / Summary / Details）
  - `DebugLogNode`（EventType / Label / LineNumber / EntryNanos / ExitNanos? / DurationMs? / Rows? / Children / ChildEventCount）
  - `DebugLogLimitEntry`（Namespace / Name / Used / Max / Percent?）
  - `DebugLogError`（Type / Message / LineNumber? / ElapsedNanos?）
  - `DebugLogSummary`（TotalDurationMs / EventCount / SoqlCount / SoqlRows / DmlCount / DmlRows / CalloutCount / Errors / Limits / Root）
  - `DebugLogAnalysis`（HeaderLine? / Events（List, cap なし全件） / Summary）
- `src/SfUi.Core/Services/DebugLogParser.cs`（static・純関数）
  - `Parse(string logText)` → `DebugLogAnalysis`
  - `ClassifyEventType(string eventType)` → `DebugLogEventCategory`（単体テスト対象）
  - 実装方針: 正規表現は 1 行 1 回のみ（プリコンパイル）。数 MB ログでも 1 パスで処理。

テスト: `tests/SfUi.Tests/DebugLogParserTests.cs` — サンプルログ（ネストメソッド / SOQL / DML / 例外 / 複数行リミット / 壊れ行 / 不完全ペア）を fixture 文字列で用意。

## 5. ViewModel / UI

### 5.1 LogAnalyzerViewModel（`src/SfUi.Presentation/ViewModels/`）

- `Initialize(DebugLogAnalysis analysis, string orgLabel, string sourceLabel)`
- 表示用: `Title` / `HeaderText` / `SummaryCards`（6 枚: 合計時間 / イベント数 / SOQL / DML / コールアウト / 例外）
- `Limits`（使用率降順、ProgressBar 用 `Percent`）
- `SlowRows`（Top 15: 種別 / ラベル / ms / 行数）
- `Errors`（例外一覧）
- `TreeRoots`（`DebugLogNode` をそのまま TreeView に）
- イベント一覧: `SearchText` + `CategoryFilter`（すべて + 8 カテゴリ）でフィルタ。
  表示は **先頭 20,000 件で打ち切り**（超過時はステータスに「全 N 件中 20,000 件表示」）
- `ExportEventsCsvCommand`（既存 `CsvExporter` 利用）/ `StatusText`
- すべての文言キーは UiText（§6）

### 5.2 WPF

- `src/SfUi.App/Views/LogAnalyzerWindow.xaml(.cs)` — 非モーダル、約 1100×750、`WindowPlacement.CenterOn`
- `src/SfUi.App/Services/LogAnalyzerWindowFactory.cs`（既存 Factory パターン）
- `WpfAppWindowService` に `OpenLogAnalyzer(analysis, orgLabel, sourceLabel)` を追加（interface + 実装）
- `LogView.xaml`: ツールバーに「解析」「.log ファイルを開く…」を追加
- `LogViewModel`: `OpenLogFileCommand`（`IFilePickerService.OpenFileAsync` → 本文表示 + 解析）/ `AnalyzeCommand`（`Task.Run` で解析 → `IAppWindowService`）

### 5.3 Avalonia

- `src/SfUi.Avalonia/Views/LogAnalyzerWindow.axaml(.cs)`（3 タブ、DataGrid / TreeView）
- `AvaloniaAppWindowService.OpenLogAnalyzer` 実装
- `LogView.axaml` のボタン追加 + `ILogAnalyzerWindowService` 相当の DI（既存パターン踏襲）

## 6. UiText 新キー（4 言語: En / Ja / Zh / Ko）

`LogAnalyze_*`（ボタン・状態）、`LogAnalyzer_*`（ウィンドウ内のラベル・カテゴリ名・列名・ステータス）。
動的キー生成はしない（`LocalizationUsageTests` の regex 対策。必要ならローカル変数で組み立て）。
主要キー: Title / TabSummary / TabTree / TabEvents / Duration / EventCount / Soql / Dml / Callouts / Exceptions /
Limits / NoLimits / Slowest / NoErrors / SearchPlaceholder / FilterAll / Cat*×8 / Line / Elapsed / Type / Details /
ExportCsv / ExportedFmt / TruncatedFmt / NoEvents / NoContent / ParsingFmt / ParseFailedFmt / OpenFile / OpenTitle / FileFilter。

## 7. フェーズ計画

| Phase | 内容 | 完了条件 |
|---|---|---|
| 1 | モデル + パーサー + 単体テスト | `dotnet test` 緑（既存 520 + 新規） |
| 2 | `--smoke --smoke-loganalyzer <org\|file>`（org なら直近ログ取得→解析。file なら読み込み→解析。サマリをログ出力） | smoke exit 0 / 実 org で解析成功 |
| 3 | WPF UI（VM / ウィンドウ / Factory / ログタブ導線 / キー） | 実機で解析ウィンドウ表示 |
| 4 | Avalonia UI（ウィンドウ / 導線） | Avalonia 実行で表示 |
| 5 | UIA 検証・ドキュメント更新（README / PLAN / 本設計書） | `sfui-log-analyzer-check.ps1` 全 PASS |

## 8. 検証方法

- 単体: サンプルログでの解析結果（件数・時間・リミット・ツリー構造）
- スモーク: `--smoke --smoke-loganalyzer acc`（実ログ取得→解析）/ ローカル `.log` ファイル
- UIA: `C:\SfUiDemo\sfui-log-analyzer-check.ps1` — ログタブ → 「.log ファイルを開く」→
  `C:\SfUiDemo\sample-debug.log` を入力 → 解析ウィンドウの概要タブのテキスト（SOQL 件数など）を検証
- 目視: ウィンドウ / タブ切替 / フィルタ / CSV 出力

## 9. リスクと対策

| リスク | 対策 |
|---|---|
| 数 MB・数十万行のログで UI が重い | パーサー 1 パス・正規表現 1 回/行、イベント表示は 20,000 件打ち切り、DataGrid 仮想化 |
| フォーマットのバリアント | 未知イベントは「その他」で保持。解析はベストエフォート（throw しない） |
| 不完全ペア（truncated ログ） | 閉じないノードは所要時間 null（「−」表示） |
| タイムゾーン・時刻表示 | 経過 ns を基準にし、絶対時刻は使わない |

## 10. 実装メモ（2026-10-08 完了時点）

- Phase 1（パーサー + 22 単体テスト）/ Phase 2（`--smoke-loganalyzer`）/ Phase 3（WPF + Avalonia UI）完了。
- スモーク: ローカル `.log` ファイル解析 OK / org 直近ログ取得 OK。
  **組織にデバッグログが保存されていない場合**（acc 等）は匿名 Apex を実行し、実行結果のインライン ログを解析するフォールバックを追加。
- UIA: `C:\SfUiDemo\sfui-log-analyzer-check.ps1` **10/10 PASS**（.log を開く → 解析 → 概要 / ツリー / イベント / 検索フィルタ検証）。
- イベント一覧は表示 **20,000 件で打ち切り**（CSV 出力は全件）。パーサーは 1 パス・正規表現 1 回/行。
- 今後: Apex タブ実行結果からの直接解析、フレームチャート、AI 連携は将来拡張。
