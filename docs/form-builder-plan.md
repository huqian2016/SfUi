# 帳票ビルダー（SVF 風フォーム）設計書

最終更新: 2026-10-28 / ステータス: **Phase A（Core）+ Phase B（UI）完了**

対象リポジトリ: `c:\huqian\vscode\SfUi`（.NET 9 / SfUi.Core + SfUi.Presentation + WPF + Avalonia）

---

## 1. 目的

Salesforce の SVF 帳票のように、**ローカルで帳票（見積書・注文書・納品書・請求書・領収書・給与明細・税務書類など）を作成・出力・印刷**できるようにする。

要件:

1. SVF 帳票相当（ヘッダー / 明細の繰り返し / 集計 / フッター / 用紙設定 / ページ番号）
2. **業界別テンプレートを多数**用意する（見積書・注文書・領収書・経理書類など）
3. **出力形式を多数**（HTML / PDF / SVG / PNG / JPEG）
4. **印刷**できる（PDF / HTML をブラウザーで印刷、WPF はアプリから直接印刷）

## 2. モデル（Core）

| 型 | 役割 |
| --- | --- |
| `FormDefinition` | 帳票定義。`Category` / 用紙（A4・A3・Letter）/ 向き / 余白 / 既定フォント（pt）/ `Header`（要素）/ `Table`（明細）/ `Footer`（要素） |
| `FormElement` | 絶対配置（mm）の要素。`text`（差し込み可）/ `line` / `box` / `page`。フォント（pt）・太字・揃え・枠線 |
| `FormTableSpec` / `FormTableColumn` | 明細表。列幅（mm）・揃え・見出し・行高・1 ページの行数固定（0 = 自動） |
| `FormData` | 差し込みデータ。`Title` / `Org` / `GeneratedAt` / `Table` / 自由入力 `Values` |
| `FormLayout` | 用紙サイズ・`RowsPerPage`・`PageCount`・`SplitRows`・差し込み置換 |
| `FormDrawList` | 帳票 → 描画命令（文字 / 矩形 / 線）。SVG と画像で共用 |

### 差し込みフィールド

| 記法 | 内容 |
| --- | --- |
| `{Title}` / `{ReportName}` | レポート名（帳票タイトル） |
| `{Org}` / `{Date}` / `{DateTime}` / `{Source}` | 組織・日付・アプリ名 |
| `{Rows}` / `{Page}` / `{Pages}` | 件数・ページ番号 |
| `{Total:列名}` / `{Sum:列名}` / `{Count:列名}` | 数値列の合計・件数 |
| `{Values:キー}` / `{キー}` | 自由入力欄（顧客名・見積番号など） |

## 3. 出力

| 形式 | 実装 | 備考 |
| --- | --- | --- |
| HTML | `FormHtmlWriter` | `@page` 付き。mm 絕對配置なのでブラウザー印刷でも崩れない |
| PDF | `FormOutputWriter` → `ReportPdfWriter` | インストール済みの Chromium 系ブラウザーで印刷 |
| SVG | `FormSvgWriter` | ベクター。1 ページ 1 ファイル（2 ページ以上は `-p1.svg`） |
| PNG / JPEG | `FormImageWriter`（SkiaSharp） | 150dpi 既定。印刷・貼り付け用 |

## 4. テンプレート（17 種）

| カテゴリ | テンプレート |
| --- | --- |
| 見積書 | 製造業 / 建設・工事 / IT・システム / 小売・店舗 |
| 注文書・発注書 | 注文書（製造業）/ 発注書（購買） |
| 納品書 | 納品書 |
| 請求書 | サービス / 建設・出来高 |
| 領収書 | 領収書 / 領収書（インボイス対応） |
| 給与・賞与 | 給与明細 / 賞与明細 |
| 税務書類 | 源泉徴収税額表（A3 横）/ 支払調書 |
| 集計表・台帳 | 売掛金台帳（A4 横） |
| 汎用 | 汎用帳票（一覧） |

- テンプレートは `FormTemplateLibrary.All`（組み込み・読み取り専用）。
- ユーザーは `FormStore.CopyFromTemplate(id)` で複製して保存（`data/forms/forms.json`）。

## 5. Phase A の検証（2026-10-28）

| 項目 | 結果 |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test` | **1,383 件成功**（+12: テンプレート 17 種・レイアウト・差し込み・HTML/SVG/画像・ストア） |
| 目視確認 | 見積書（製造業）を 150dpi PNG で確認 — ヘッダー・明細・小計・フッター・ページ番号が用紙内に収まる |

## 6. Phase B（UI・2026-10-28 完了）

1. `FormOutputViewModel`（テンプレート選択 / 差し込み値の入力 / 形式選択 / プレビュー PNG）— 完了
2. WPF / Avalonia の帳票出力ダイアログ — `Views/FormOutputDialog.xaml` / `Dialogs/FormOutputDialog.axaml`（`AutomationId`: `ReportsTemplates` / `FormTemplate` / `FormValues` / `FormValueBox` / `FormPreviewImage` / `FormHtml`〜`FormJpeg` / `FormOutputName` / `FormRender` / `FormPrint` / `FormCancel` / `FormStatus`）
3. **印刷**: WPF は `PrintDialog` + `FixedDocument` でページ画像を印刷、Avalonia は生成した PDF / HTML を既定ブラウザーで開いて印刷
4. レポート ウィンドウのツールバーに「帳票テンプレート…」ボタン（`ReportsTemplates`）
5. E2E: `C:\huqian\sfui-form-template-e2e.ps1` / `-avalonia.ps1` → **14/14 PASS**（見積書テンプレート → 差し込み 12 項目 → HTML 出力、`@page` / 明細行を確認）。テストは **1,394 件**、マニュアル §19.7 と README を 4 言語で更新
