# レポート出力（CSV / Markdown / HTML / Excel / PDF）設計メモ

各タブのデータを **1 つのレポート モデル**にまとめ、共通の設定ダイアログから
複数形式へ同時に書き出せるようにする機能です。

- 状態: **Phase 1 実装済み（2026-10-24）** — 基盤 + メイン ウィンドウの 6 タブ
- 関連コード:
  - `src/SfUi.Core/Reports/` — `ReportDocument`（モデル / 出力設定）、`ReportAnalysis`、
    `ReportMarkdownWriter`、`ReportHtmlWriter`、`ReportPdfWriter`、`ReportExportService`
  - `src/SfUi.Presentation/UI/IReportExportDialog.cs` — ダイアログ抽象 + `ReportExportRunner`
  - `src/SfUi.Presentation/UI/ReportDocumentFactory.cs` — DataTable / テキストから文書を作る共通処理
  - `src/SfUi.Presentation/ViewModels/ReportExportDialogViewModel.cs`
  - WPF `Views/ReportExportDialogWindow.xaml` + `Services/WpfReportExportDialog.cs`
  - Avalonia `Dialogs/ReportExportDialog.axaml` + `Services/AvaloniaReportExportDialog.cs`

## 出力形式

| 形式 | 用途 | 実装 |
|---|---|---|
| CSV | 既存互換。表ごとに 1 ファイル（UTF-8 BOM） | `ExportSheet.ToCsv()` を再利用 |
| Markdown | **AI に渡す**（YAML フロント マター + 見出し + 表。数値列は `---:` 右寄せ） | `ReportMarkdownWriter` |
| HTML | **人が確認**（全列の部分一致フィルター / ヘッダー クリックでソート / 行数表示 / 印刷ボタン） | `ReportHtmlWriter`（CSS + JS 埋め込みの単一ファイル） |
| Excel | **加工ベース**（概要シート + 表ごとのシート。列幅自動・オートフィルター付き） | `ExcelExporter`（OpenXML） |
| PDF | 配布・共有 | `ReportPdfWriter`（HTML を Microsoft Edge / Chrome のヘッドレス印刷で PDF 化） |

- PDF は追加ライブラリを持たず、インストール済みの Chromium 系ブラウザーを利用（日本語も正しく描画）。
  見つからない場合は「Edge または Chrome が必要」というメッセージを返す（`Report_NoPdfBrowser`）。

## 分析（レポートらしさ）

`ReportAnalysis.Analyze(table)` が表ごとに次を箇条書きで追加します（ダイアログの「分析を付ける」がオン）。

- 行数 / 列数、空欄の件数
- 数値列: 件数・合計・平均・最小・最大
- その他の列: 種類数、多い順トップ 5、すべて同じ値の検出
- 完全重複行のグループ数

## ダイアログ（各タブの「出力」ボタン）

- 含まれる内容（タイトル・組織・作成日時・表ごとの行数×列数・分析の有無）
- 出力形式（CSV / Markdown / HTML / Excel / PDF）
- 分析セクションの有無、表ごとの最大行数（0 = すべて）
- ファイル名・出力先フォルダー（参照ボタン）・保存後にフォルダーを開く
- 形式未選択などの入力エラーはダイアログ内に表示して閉じない

## 配線済みの機能（Phase 1）

| タブ | 出力内容 |
|---|---|
| SOQL | クエリ + 結果グリッド（数値列は自動判定） |
| 履歴 | 表示中の履歴一覧（種別 / 概要 / 組織 / 状態 / 所要時間 / 日時） |
| デバッグログ | ログ一覧（ID / 開始 / 操作 / 状態 / 所要時間 / サイズ）+ 表示中のログ本文 |
| 匿名 Apex | スクリプト + 実行結果ログ |
| コマンド | 実行した sf 引数 + 標準出力 / 標準エラー |
| REST API | メソッド・パス・状態 + リクエスト本文 + 応答 JSON |

## 今後の展開（Phase 2 以降）

- 組織情報ウィンドウ（`OrgInfoSectionViewModel` の表をそのままレポート化）
- 組織比較 / データ入出力の結果 / バックアップ・レコード一覧 / 組織管理（棚卸し・ヘルス）
- ソース エディタ（ファイル内容 + 反映結果）、ETL（ジョブ定義・実行結果）
- レポート テンプレート（既定の形式・行数を設定に保存）、複数レポートの一括出力

## 検証

- 単体テスト: `tests/SfUi.Tests/ReportExportTests.cs`（8 件 — Markdown / HTML / エスケープ / 分析 /
  行数制限 / 一括出力 / 表ごとの CSV / 形式なし）
- UI 自動操作 E2E: `C:\huqian\report-export-e2e.ps1` — SOQL 実行 → 出力ボタン → ダイアログで
  形式と出力先を設定 → CSV / MD / HTML / XLSX / PDF の 5 ファイル生成と中身を確認（15/15）
