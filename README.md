# SfUi — Salesforce CLI Launcher

A Windows desktop tool that wraps the Salesforce CLI (`sf`) with a fast, click-light UI.
Run SOQL, anonymous Apex, debug logs, deploys, free-form `sf` commands and raw REST API calls —
with history/favorites, quick switching between orgs and project folders, and one-click tool launcher.

> UI languages: **English (default) / 日本語** — switch instantly from the top bar.

## Features

- **Org & folder switching in one click** — the org combo and SF folder combo are always in the top bar; the last selection is restored on startup.
- **SOQL** — AvalonEdit editor with SQL highlighting, REST-first execution (falls back to `sf data query`, with a Tooling API toggle), results in a sortable grid, CSV/TSV export.
- **Anonymous Apex** — run scripts via `sf apex run`, show compile errors / exceptions / debug logs, open & save `.apex` files, fetch the latest debug log from the org.
- **Debug logs** — list logs (`sf apex list log`), fetch content by id or number, save locally.
- **Deploy** — deploy / validate / quick deploy / report / retrieve with source dir or manifest, test level, wait time, live command preview, and result summaries (auto-fills the job id).
- **Free-form commands** — run any `sf` arguments; dangerous operations (delete/logout/deploy …) ask for confirmation.
- **REST console** — GET/POST/PATCH/DELETE against the org's instance URL using the access token (`sf org display`), with pretty-printed responses and 401 auto-refresh.
- **History** — every operation is recorded per type (org, folder, params, result), searchable, re-runnable by double-click.
- **Favorites & quick panel** — pin SOQL / Apex / commands / REST requests / URLs / folders; run the first nine with `Ctrl+1..9`.
- **AI chat (DeepSeek)** — generate SOQL / Apex / sf commands from natural language and analyze execution results; apply generated code to the matching tab with one click. Requires a DeepSeek API key (Settings → AI, or the `DEEPSEEK_API_KEY` environment variable).
- **Tool launcher** — open Terminal (wt / PowerShell / cmd / WSL), Explorer, VS Code, or the org in a browser (home / setup / login / recent URLs).
- **Keyboard shortcuts** — `Ctrl+Enter` run (SOQL/Apex), `Enter` run (command), `Ctrl+1..9` favorites, `F5` re-run the last operation.
- **Portable** — a `data/` folder (settings, history, logs, results) is created next to the exe; falls back to `%APPDATA%\SfUi` when not writable.

## Requirements

- Windows 10 / 11 (x64)
- **Salesforce CLI (`sf`)** installed and at least one org authenticated (`sf org login web`)
  - The app runs `sf` behind the scenes; default path is `C:\Program Files\sf\bin\sf.cmd` (configurable in Settings, or via the `SFUI_SF_PATH` environment variable)
- Running the released EXE requires **no .NET runtime** (self-contained)

## Getting started

1. Download from the [Releases](../../releases) page:
   - **`SfUi.exe`** — the portable single executable, or
   - **`SfUi-v0.1.0-portable.zip`** — the executable **plus a ready-to-use `data/` folder with 30 sample SOQL / Apex / commands / REST requests already in the history** (just extract and run).
2. Put it in any folder and double-click (no installer).
   - Windows SmartScreen may warn because the binary is unsigned — choose *More info* → *Run anyway*.
3. On first run a `data/` folder is created next to the exe (portable mode).
4. Pick your org in the top bar and start with a tab, or seed the history with sample SOQL / Apex / commands / REST requests via **Settings → Seed sample history**.

## Screens & tabs

| Tab | What it does |
|---|---|
| SOQL | Editor + results grid, CSV export, favorites/history |
| Apex | Anonymous Apex runner with debug log output |
| Debug Logs | List / fetch / save logs from the org |
| History | All operations, searchable and re-runnable |
| Deploy | Deploy / validate / quick / report / retrieve |
| Command | Free-form `sf` arguments with stdout/stderr |
| REST API | Raw REST console (limits, describes, queries, …) |
| Settings | sf & tool paths, history limits, confirm policy, language, sample data |

The left **Quick Panel** shows favorites with number slots; the top bar hosts org/folder selection, the four tool launcher buttons and the language switch.

## Build from source

```powershell
# Requirements: .NET SDK 9.0+
dotnet build SfUi.sln -c Debug
dotnet test  SfUi.sln

# Produce a self-contained single-file EXE into dist/
dotnet publish src/SfUi.App/SfUi.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

### Project layout

```
SfUi.sln
├─ src/SfUi.Core    … WPF-free logic (services, stores, localization dictionaries, tests target)
│  ├─ Services      … SfCliRunner, OrgService, SalesforceRestClient, DeployService, ToolLauncherService, …
│  ├─ Storage       … atomic JSON stores (settings / history / favorites / recent)
│  └─ Localization  … UiText dictionaries (en / ja)
├─ src/SfUi.App     … WPF app (MVVM, views, localization markup extension)
└─ tests/SfUi.Tests … xUnit (142 tests: quoting, JSON parsing, stores, services, localization, …)
```

Built with C# / .NET 9 / WPF, CommunityToolkit.Mvvm and AvalonEdit.

---

# SfUi — Salesforce CLI ランチャー（日本語）

Salesforce CLI（`sf`）の操作を Windows デスクトップ UI から行えるツールです。
SOQL・匿名Apex・デバッグログ・デプロイ・自由コマンド・REST API 呼び出しを、履歴とお気に入り付きで
少ないクリックで実行できます。組織とフォルダの切替、外部ツールの起動もワンクリックです。

- UI は **英語（既定）/ 日本語** に対応（上部バーのコンボで即時切替）
- **AI チャット（DeepSeek）**: 自然言語から SOQL / 匿名Apex / sf コマンドを生成、実行結果の分析も可能（設定 → AI で API キーを登録）
- 2026-10-02 時点で Phase 0〜8 完了（テスト 142 件 / スモーク E2E 検証済み）

## 使い方

1. [Releases](../../releases) からダウンロード
   - **`SfUi.exe`** … 実行ファイル単体（サンプル履歴は設定画面から後で追加可）
   - **`SfUi-v0.1.0-portable.zip`** … exe + サンプル履歴 30 件入りの `data/` フォルダ（解凍してそのまま実行）
2. 任意のフォルダに置いてダブルクリック（インストーラー不要）
   - 署名なしのため SmartScreen の警告が出たら「詳細情報」→「実行」
3. 初回起動時に exe 隣に `data/` フォルダ（設定・履歴・ログ）が作成されます（ポータブル動作）
4. 上部バーで組織を選び、各タブから操作を開始。サンプルの SOQL / Apex / コマンド / REST は
   「設定 → サンプル履歴を投入」で追加できます

## 前提条件

- Windows 10 / 11（x64）
- **Salesforce CLI（`sf`）** がインストール済みで、いずれかの組織にログイン済みであること
  - 既定のパスは `C:\Program Files\sf\bin\sf.cmd`（設定画面または環境変数 `SFUI_SF_PATH` で変更可）
- 単一 EXE 版の実行に .NET ランタイムは不要（自己完結）

## 主なショートカット

- `Ctrl+Enter` … SOQL / 匿名Apex を実行
- `Enter` … コマンド実行
- `Ctrl+1..9` … クイックパネルのお気に入りを実行
- `F5` … 直前の操作を再実行

## ビルド

```powershell
dotnet build SfUi.sln -c Debug
dotnet test  SfUi.sln
```
