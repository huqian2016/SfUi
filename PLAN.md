# SfUi — Salesforce CLI 統合デスクトップツール 実装計画

最終更新: 2026-10-04 / ステータス: Phase 0-15 完了 + AI 接続先の汎用化 + 組織比較 + データ入出力 + アクセス権限タブ + レコードのバックアップ・復元（自動ラベル・比較タブ付き）+ Microsoft Store（MSIX）提出準備完了（v0.8.0 / テスト 376 件 / 実 API スモーク + UIA E2E 検証済み。GitHub Release は v0.7.3 公開済み、Microsoft Store は審査準備中）

## 1. 概要

Salesforce CLI (`sf` コマンド) の操作を Windows デスクトップ UI から行うためのツール。
裏で `sf` を実行して結果を表示し、実行した内容（組織・フォルダ・SOQL・匿名Apex・結果など）を
蓄積して再実行を容易にする。認証済み組織のアクセストークンを取得し REST API を直接呼び出すことで、
高速な SOQL 実行や汎用 API 呼び出しも可能にする。

- 対象: Windows 10/11、sf CLI v2（確認済み: v2.94.6 stable）
- 技術: C# / WPF (.NET 9) / MVVM / ポータブル単一 EXE

## 2. 要件マッピング

| # | 要件 | 実装方針 |
|---|------|----------|
| 1 | sf CLI を利用する | `SfCliRunner` が `C:\Program Files\sf\bin\sf.cmd` を `--json` 付きで実行 |
| 2 | C# UI から sf 機能を操作 | WPF シェル + 各機能ビュー（SOQL / 匿名Apex / ログ / デプロイ / コマンド） |
| 3 | 操作内容を記憶 | JSON 履歴（種別・組織・フォルダ・パラメータ全文・結果）。ダブルクリックで再実行 |
| 4 | トークン取得 → API 呼び出し | `sf org auth show-access-token` → `SalesforceRestClient`（REST / Tooling） |
| 5 | クリック数を抑えた UI | 上部バーで組織/フォルダ切替、ショートカット、お気に入り Ctrl+1..9 |
| 6 | 組織/フォルダの簡単切替 | 上部コンボ + 履歴 + ピン留め。起動時に前回状態を復元 |
| 7 | よく使うツール起動 | ターミナル(wt) / エクスプローラー / VS Code / ブラウザ ボタン + 引数選択 |

## 3. 確定事項（2026-10-02 ユーザー確定）

- UI: **WPF (.NET 9)**
- 配布: **ポータブル単一 EXE**（self-contained / single-file）
- 保存: **JSON ファイル**（種別ごとに分割）
- 実行方式: **REST API 優先 + CLI フォールバック**
- 追加機能（すべて採用）: REST 汎用コンソール / sf 自由コマンド実行 / SOQL 結果 CSV 出力 / Apex デバッグログ / デプロイ・メタデータ操作
- 実行前確認: **危険操作（deploy / delete / logout 等）のみ確認**
- 履歴上限: **2000 件/種別**、結果保存閾値 **64KB**（設定画面で変更可）
- 初期スコープ外: apex tail log、トレイ常駐 / グローバルホットキー（後続で追加）

## 4. 環境前提（確認済み）

- .NET SDK 9.0.301
- sf CLI v2.94.6 stable（`C:\Program Files\sf\bin\sf.cmd`、設定は `~/.sf`、認証は `~/.sfdx`）
- 認証済み組織（alias）: trailhead, acc, EduCloud, agent1, Edu4, hks3, hks3_sand3, NISSAY598, Hks2, hks4, hks4sand1, hks4sand2 ほか
- Windows Terminal (`wt.exe`)、VS Code CLI (`code.cmd`)、Firefox
- SF プロジェクトフォルダ例: `c:\huqian\vscode\{hks3, Hks3Sand3, EduCloud, NISSAY, ...}`

## 5. アーキテクチャ

### プロジェクト構成

```
SfUi.sln
├─ src/SfUi.Core    … WPF 非依存のロジック（サービス / モデル / ストア）※単体テスト対象
├─ src/SfUi.App     … WPF アプリ（Views / ViewModels / DI 構成）
└─ tests/SfUi.Tests … xUnit
```

### 主要サービス（Phase 対応）

| サービス | 役割 | Phase |
|---|---|---|
| `SfCliRunner` | sf 実行・JSON 解析・キャンセル・タイムアウト | 1 |
| `SfCommandCatalog` | コマンド/引数の定義 | 1 |
| `OrgService` | 組織一覧・トークン取得・キャッシュ | 1 |
| `SalesforceRestClient` | REST / Tooling API | 3 |
| `HistoryStore` / `FavoritesStore` / `RecentStore` / `AppSettingsStore` | JSON 永続化 | 2 |
| `ToolLauncherService` | wt / explorer / code / browser 起動 | 4 |
| `AppPaths` / `AppLog` | データフォルダ解決・ログ | 0 ✅ |

### データフォルダ解決の優先順位（`AppPaths.Resolve`）

1. `--data-dir` 引数 / `SFUI_DATA_DIR` 環境変数
2. 開発時: ソリューションルート（上方向に `SfUi.sln` を探索）の `data/`
3. ポータブル: 実行ファイル隣の `data/`（書込可の場合）
4. `%APPDATA%\SfUi`

## 6. UI 設計方針（クリック数・入力回数の最小化）

- 常時表示の上部バー: [組織コンボ] [SFフォルダコンボ] [ターミナル] [エクスプローラー] [VS Code] [ブラウザ]
- 履歴・お気に入り: ダブルクリックで再実行、`Ctrl+1..9` でお気に入り即実行
- 実行: `Ctrl+Enter`、再実行: `F5`
- 組織・フォルダは起動時に前回状態を自動復元
- 引数（フォルダ / URL）は履歴・ピン留めから選択（手入力不要）

## 7. sf コマンド対応表

| 機能 | コマンド（v2.94.6 時点。フラグは実装時に `sf <cmd> --help` で最終確定） |
|---|---|
| 組織一覧 | `sf org list --json` |
| アクセストークン | `sf org display --target-org <alias> --json`（v2.94.6 では `org auth show-access-token` 未搭載。将来搭載時は切替） |
| ログイン / ログアウト | `sf org login web --alias <alias>` / `sf org logout` |
| ブラウザ起動 | `sf org open --target-org <alias> [--path <path>]` / URL 直接起動 |
| SOQL | REST `/services/data/vXX.X/query`（自動ページング）／代替 `sf data query --query <soql> --json` |
| 匿名 Apex | `sf apex run --target-org <alias> --file <tmp.apex> --json` |
| ログ一覧 / 取得 | `sf apex list log --json` / `sf apex get log --log-id <id> --json` |
| デプロイ | `sf project deploy start / validate / quick / report --json` |
| 取得 | `sf project retrieve start --json` |
| 自由コマンド | ユーザー入力引数をそのまま実行（`sf` は自動付与） |

## 8. データ保存設計（JSON）

```
data/
├─ settings.json          … アプリ設定
├─ favorites.json         … お気に入り（SOQL / 匿名Apex / コマンド / URL / フォルダ）
├─ recent-folders.json    … 最近の SF 実行フォルダ
├─ recent-urls.json       … 最近の URL
├─ history/
│  ├─ soql.json / apex.json / command.json / api.json / deploy.json / org.json
│  └─ 上限 2000 件/種別。超過分は古い順に削除
├─ results/<id>.json      … 大きい実行結果（64KB 超）の本文
├─ logs/app-YYYYMMDD.log  … アプリログ（日次）
└─ tmp/                   … 匿名Apex一時ファイル等
```

- 原子書き込み（temp → File.Replace）、破損時は `.bak` から復旧
- 結果本文 64KB 以下は履歴 JSON 内に保存、超過分は `results/` へ分離

## 9. フェーズ計画

### Phase 0: プロジェクト基盤（本フェーズ）✅

- ソリューション / 3 プロジェクト作成、`.gitignore`、git init
- `AppPaths`（データフォルダ解決）、`AppLog`（日次ログ）、DI 登録（`AddSfUiCore`）
- MainWindow シェル（上部バー仮置き + タブ + ステータスバー）
- `--smoke` 起動モード（3 秒で自動終了、起動確認用）
- ビルド / 単体テスト / スモーク起動で動作確認

### Phase 1: sf CLI 実行基盤＋組織管理 ✅

- `SfCliRunner`（cmd.exe /d /s /c 経由、UTF-8、タイムアウト・キャンセル、テレメトリ/自動更新抑制の環境変数）
- `SfCommandResult`（--json 解析。エラー名/メッセージ抽出、result の Clone 保持）
- `OrgService`（組織一覧は username で重複排除、トークンは `sf org display`、30 分メモリキャッシュ）
- 上部バー: 組織コンボ（既定★先頭）+ 更新ボタン、SFフォルダ（編集可 + 参照ダイアログ）
- 注: インストール済み sf v2.94.6 には `org auth show-access-token` が無いため `org display` を使用（将来搭載時は切替候補）

### Phase 2: JSON 永続化 ✅

- `AtomicJsonFile`（一時ファイル → File.Replace の原子的書き込み、.bak 復旧、破損時 .bad-* 退避）
- `AppSettingsStore`（前回組織 / 前回フォルダ / 履歴上限 2000 件・結果閾値 64KB / 危険操作確認）
- `RecentItemsStore<T>`（フォルダ / URL。ピン留め対応、上限 30 件で古い順に削除）
- `FavoritesStore`（SOQL / 匿名Apex / コマンド / API / URL / フォルダ）
- `HistoryStore`（種別ごとの JSON、上限ローリング、64KB 超の結果は results/ へ分離保存）
- 履歴ビュー（種別フィルタ / 全文検索 / パラメータコピー / 削除、右クリックメニュー）
- 上部バー: 最近フォルダのコンボ選択・前回組織 / 前回フォルダの起動時復元
- ダブルクリック再実行は Phase 3 以降の機能ビューと同時に有効化予定

### Phase 3: SOQL・匿名Apex・ログ（中核）✅

- `SalesforceRestClient`（Bearer トークン、401 時トークン再取得、自動ページング、Tooling、limits、describe、汎用送信）
- `SoqlService`（REST 優先 → 失敗時 `sf data query` フォールバック。構文エラーの場合は再実行しない）
- `SoqlResultFactory`（records を DataTable へ平坦化: ネストは `Account.Name`、配列は JSON 文字列）
- SOQL ビュー（AvalonEdit + SQL ハイライト、Ctrl+Enter、結果 DataGrid、CSV/TSV 出力、履歴・お気に入り連携）
- 匿名 Apex ビュー（一時ファイル [BOM なし UTF-8] → `sf apex run --json`、成功/コンパイルエラー/例外/ログ表示、ファイル読込・保存）
- ログビュー（`apex list log` → `apex get log`。`--log-id` / `--number` 対応）
- 履歴ダブルクリック: SOQL は再実行、匿名Apex は読み込み（安全のため自動実行なし）

### Phase 4: ツールランチャー ✅

- `ToolLauncherService`: ターミナル（Windows Terminal → cmd フォールバック、PowerShell / cmd / WSL 選択可・WSL パス変換付き）、エクスプローラー（/select 対応）、VS Code（新規 / 現在ウィンドウ、code.cmd 自動検出）、ブラウザ（既定ブラウザ）
- 上部バー: 各ツールをワンクリック起動（クリック = 既定 / 右クリック = バリアント選択）
- ブラウザメニュー: 組織ホーム / 組織の設定 / Salesforce ログイン / URL 入力ダイアログ / 最近の URL（自動記録）
- 引数の既定: 現在の SF 実行フォルダ（未選択時はユーザープロファイル）

### Phase 5: 自由コマンド・REST コンソール・デプロイ ✅

- 自由コマンドビュー（`sf` は自動付与、Enter 実行、危険操作は実行前確認、stdout/stderr 表示、履歴 / お気に入り）✅
- REST 汎用コンソール（GET/POST/PATCH/DELETE、パス / JSON ボディ、整形表示、履歴保存。401 時はトークン再取得で 1 回リトライ）✅
- デプロイ（start / validate / quick / report / retrieve。ソースディレクトリ / manifest / テストレベル / 待機時間、確認ダイアログ、60 分タイムアウト、進捗 / キャンセル、結果サマリーから job-id 自動補完）✅

### Phase 7: 多言語対応（英語・日本語） ✅

- `UiText`（Core）: en / ja の文字列辞書 + `SetLanguage` + `LanguageChanged`。未定義キーはキーを返す
- XAML: `{loc:Tr Key}` マークアップ拡張（バインド経由で言語切替時に一斉更新、再起動不要）
- 言語切替: 上部バーの言語コンボ（English / 日本語）+ settings.json の `language`（既定 en）で永続化し、起動時に適用
- 言語追従: 履歴の種別フィルタ / デプロイ操作コンボ / 確認ポリシーのラベルは切替時に自動再構築
- 対象範囲: 全 View XAML・全 ViewModel のメッセージ / ダイアログ・Core のユーザー向けメッセージ / ラベル・サンプル履歴の要約（アプリログと開発コメントは対象外）

### Phase 8: AI チャット（DeepSeek） ✅

- `DeepSeekClient`（Core）: OpenAI 互換 `POST https://api.deepseek.com/chat/completions`。モデル既定 `deepseek-chat`（設定で `deepseek-reasoner` も選択可）。タイムアウト 3 分・キャンセル対応
- API キーの解決順: settings.json（`deepSeekApiKey`）→ 環境変数 `DEEPSEEK_API_KEY` → 内蔵（評価用・XOR+Base64 難読化で `DefaultAiKey.cs` に埋め込み）。README / MD ファイルにはキー値を記載しない
- AI パネル（右サイド）: 会話 UI（ユーザー / AI メッセージ・自動スクロール・新しい会話）、`Ctrl+Enter` 送信、キャンセル。上部バーの「AI」トグルで表示/非表示（状態は settings.json に保存）。タブは従来どおり 8 タブ
- 生成支援: システムプロンプトでコードブロック出力を指示（`soql` / `apex` / `bash`）。応答内のコードを「SOQL タブへ / 匿名Apex タブへ / コマンドタブへ」ボタンで適用
- 結果分析: 結果付き履歴をコンテキストとして添付（12,000 文字で切詰め）して送信。「直前の結果を分析」をワンクリックで準備
- 設定画面: API キー / モデル / 接続テスト
- スモーク: `--smoke-ai`（実 API に ping し tokens / 秒数をログ）

### Phase 6: 仕上げ・配布 ✅

- クイックパネル（左サイド、お気に入り一覧 + `Ctrl+1..9` 即実行、ダブルクリック / Enter 実行、右クリック削除、上部バーの「クイック」で表示切替）✅
- 設定画面（sf パス / Windows Terminal パス / VS Code パス / 履歴上限 / 結果閾値 / 確認ポリシー。保存で即時反映）✅
- ショートカット整備（`Ctrl+1..9` クイック実行、`F5` 直前の操作を再実行、`Ctrl+Enter` 実行は各ビュー）✅
- ステータスバーに組織 / フォルダ表示 ✅
- 単一 EXE publish（`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` → `dist/SfUi.exe`）✅

### Phase 9: Microsoft Store 配布（MSIX） ✅（提出準備完了）

- `packaging/` 一式を追加:
  - `AppxManifest.xml` — Desktop Bridge（Windows.Desktop / `runFullTrust` / x64）。Partner Center 予約値（Name=`HKS.SfUi`、Publisher=`CN=F23B2630-…`、PublisherDisplayName=HKSテック株式会社）を反映
  - `Make-Msix.ps1` — publish → layout → resources.pri → makeappx → 一時キー署名（`-SkipPublish` / `-Register` / `-Unregister` / `-Version`）
  - `New-Icon.ps1` — PNG → 16〜256px マルチサイズ ICO 生成
  - `Assets/`（仮ロゴ 5 種）と README
- EXE / タスクバー / ウィンドウ アイコン（`src/SfUi.App/SfUi.ico`、csproj `<ApplicationIcon>` + WPF `Resource` + MainWindow `Icon=`）
- VS Code タスク: `msix (build)` / `msix (register test)` / `msix (unregister)`
- 検証: `dist/SfUi_0.2.0.0_x64.msix`（MinVersion 10.0.17763.0 / Identity 0.2.0.0 / 自己署名一時キー）を unpack・アイコン色検証・スモークで確認
- Partner Center: 登録情報（日英説明）、`runFullTrust` の用途申請、プライバシー ポリシー（テキスト提供）を進行中。認定後は Microsoft が再署名（SmartScreen 警告なし・証明書不要）

### Phase 10: 組織情報ウィンドウ ✅

管理者向けの閲覧ツール。メインウィンドウの「組織情報」ボタンから、選択中組織の設定・メタデータ・アクセス制御を
**非モーダルの独立ウィンドウ**（複数同時可・同じ組織は毎回新規）で一覧・検索・再取得する。

- 設計ドキュメント: `docs/org-info-window-plan.md`（Step 1〜5）
- タブ: 概要 / 設定 / ユーザ / プロファイル / 権限セット / ロール / オブジェクト / OWD / Apex クラス / Apex トリガ / フロー / スケジュール済みジョブ / 接続アプリ / インストール済みパッケージ / ログイン履歴 / 設定変更履歴 / レコードタイプ / 通貨 / オブジェクト項目（遅延取得）/ マイ設定（カスタムタブ）/ マイ設定管理
- キャッシュ: `data/orginfo/<orgKey>.json`（**初回のみ自動取得**・以降は手動再取得。再オープンでは取得しない）。マイ設定は `data/orginfo/preferences.json`（組織別・複数ウィンドウで同期）
- 検索: タブ内（列名 + 値、200ms デバウンス）+ 全タブ横断（上限 1,000 件、結果から該当タブ・行へジャンプ）
- マイ設定: カタログ 55 項目（概要 / 設定 / OWD / 統計）から選択。複数タブ・名前変更・並べ替え・組織別保存。再取得は参照元セクションのみ
- AI: ウィンドウ単位の独立会話。右サイドパネル（AI トグル）で「表示中タブのデータを添付」（TSV・12,000 文字）とクイックプロンプト 4 種。システムプロンプトに組織コンテキストを自動付与
- リンク: セクションの Setup ボタン / 行の ↗（ユーザー・プロファイル・オブジェクト詳細）/ 設定の「リンクのみ」行
- スモーク: `--smoke --smoke-orginfo <org>`（初回取得 → 2 回目は API を呼ばない → `--smoke-orginfo-refresh` で fetchedAt 更新）
- テスト: 242 件（+97: Core クエリ/パース/キャッシュ/検索/URL/カタログ/設定ストア/添付 + UI 表示）
- E2E（実アプリ + UI Automation、`C:\huqian\sfui-orginfo-*.ps1`）: 初回 8 セクション自動取得 → 再オープンで再取得なし / 項目 63 取得 / 全タブ検索 + ジャンプ / マイ設定の作成〜保存〜復元 / AI タブデータ添付 / 多通貨無効の通貨タブ専用メッセージ / 日本語表示確認

### Phase 11: AI 接続先の汎用化（OpenAI 互換 / Anthropic / ローカル LLM） ✅

環境によって DeepSeek に到達できないケースへの対応として、AI チャットの接続先を任意の **OpenAI 互換 API** に切り替え可能にした。

- `DeepSeekClient` → **`AiChatClient`** に改名・汎用化: `POST {endpoint}` + `Authorization: Bearer`。既定は従来どおり DeepSeek（`https://api.deepseek.com/chat/completions` / `deepseek-chat`）。タイムアウト 3 分・キャンセル対応は不変
- 設定（settings.json）: `aiEndpoint` / `aiApiKey` / `aiModel`（null = 既定）。旧 `deepSeekApiKey` / `deepSeekModel` は読み込み時に自動移行して空に戻す
- API キーの解決順: settings.json → 環境変数 `SFUI_AI_API_KEY` → 旧 `DEEPSEEK_API_KEY` / 内蔵キー（**後者 2 つは DeepSeek 既定接続先のときのみ**。カスタム接続先へ DeepSeek のキーを送らない）。カスタム接続先では未設定でもキーなしで送信（ローカル LLM 対応）
- 設定画面: AI グループに **プリセット 4 種**（DeepSeek（既定）/ OpenAI 互換 / Anthropic（Claude）/ ローカル LLM）+ エンドポイント欄 + モデル欄（自由入力 TextBox）+ 接続テスト。Anthropic は公式の OpenAI SDK 互換レイヤー（`https://api.anthropic.com/v1/chat/completions` / `claude-sonnet-4-6`）を使用
- 検証: ローカルスタブ（TcpListener）で OpenAI 互換リクエスト / レスポンスと Authorization ヘッダー有無（キーあり = 送信・キーなし = 無送信）を確認。実 OpenAI API は認証成功（アカウント残高なしの 429。`GET /v1/models` でキー有効を確認）。DeepSeek 既定（内蔵キー）も `--smoke-ai` で OK
- テスト: 251 件（+9: AiChatClient の解決順 / 抽出 + 設定移行 round-trip）
- バージョン: **0.4.0**（機能追加のためマイナーアップ。`SfUi.App.csproj` = 0.4.0 / `AppxManifest.xml` = 0.4.0.0 / MSIX = `dist\SfUi_0.4.0.0_x64.msix`）

### Phase 12: 組織比較ウィンドウ（複数組織の一括比較） ✅

2〜4 組織を一括で横並び比較する独立ウィンドウ（非モーダル・複数同時可）。メイン上部バーの「組織比較」から開く。設計書: `docs/org-compare-window-plan.md`。

- `OrgCompareService`（Core）: 組織情報キャッシュ優先 + 未取得セクションは比較時に自動取得（組織情報ウィンドウとキャッシュ共有）。13 カテゴリ（概要 / 設定 / OWD / 統計 / ユーザー / プロファイル / 権限セット / ロール / オブジェクト / Apex クラス / Apex トリガー / フロー / レコードタイプ）を **API 名で突合**（ユーザーのみ Username・レコードタイプは Sobject.DeveloperName）
- 差分 = ①片方にのみ存在 ②比較列の値不一致（大小文字無視・null = 空文字）。差分行をハイライト + 「差分のみ表示」フィルタ。セル状態 = 値 / —（存在しない）/ 未取得 / 取得失敗（失敗はログして継続、既存キャッシュがあれば表示継続）
- UI: 組織チェックボックス（最大 4・選択とカテゴリは `data/orginfo/compare.json` に永続化）、カテゴリタブ（動的列グリッド）、CSV 出力（フィルタ適用後・組織列 + 差分列）
- スモーク: `--smoke --smoke-compare <org1,org2[,...]>`（全カテゴリの行数/差分件数をログ）。E2E: 実組織 acc / hks4sand1 で権限セット 290 行・差分 250 行・CSV 290 行を確認
- テスト: 277 件（+25: 突合キー / 差分判定 / 統計 / DataTable / 状態ストア）
- バージョン: **0.5.1**（組織比較の追加と UI 改修。`SfUi.App.csproj` = 0.5.1 / `AppxManifest.xml` = 0.5.1.0 / MSIX = `dist\SfUi_0.5.1.0_x64.msix`）

### Phase 13: データ入出力ウィンドウ（Data Export / Data Import 相当） ✅

選択中組織に対する **データエクスポート / インポート** を行う独立ウィンドウ（非モーダル）。設計書: `docs/data-io-window-plan.md`。メイン上部バーの「データ入出力」と、組織情報ウィンドウのオブジェクトタブの「データ入出力」（選択中オブジェクトを引き継ぐ）から開く。

- Core: `CsvParser`（RFC4180・エンコーディング自動判定 = BOM UTF-8/UTF-16 → UTF-8 → Shift-JIS → Latin-1）/ `CsvExporter.ToTsv` / `ImportValueCoercion`（describe に基づく型変換）/ `ImportFieldMatcher`（自動マッピング・検証）/ `ImportBatchPlanner`（200 件バッチ・composite ボディ・Bulk CSV）/ `ImportResultMapper` / `DataIoQueryBuilder`（オブジェクト・項目からの SOQL 生成）/ `SObjectDescribeService`（組織単位キャッシュ）/ `DataExportService` / `DataImportService`
- エクスポート: SOQL 直接入力または項目選択ビルダー × REST / Bulk API。結果グリッド + CSV / JSON / TSV 保存
- インポート: CSV 読込（エンコーディング・行数表示）→ 自動マッピング（変更・使用可否）→ 挿入 / 更新 / アップサート / 削除 × REST（composite/sobjects・200 件/バッチ・Upsert は逐次 PATCH）/ Bulk API。確認ダイアログ（危険操作ポリシー連動）→ 進捗 → 行別結果グリッド（失敗行 CSV 出力付き）
- 履歴: type = `data` を追加しフィルタ / ダブルクリック再実行（SOQL はエクスポートタブで復元）
- テスト: 341 件（+64: CSV パーサ / 型変換 / マッピング / バッチ計画 / 結果マッピング / SOQL ビルダ）
- スモーク: `--smoke --smoke-dataio <org>`（一覧・Account describe・REST エクスポート・インポート計画ドライラン）
- E2E（`C:\huqian\sfui-dataio-e2e.ps1`・UIA）: オブジェクトタブからの事前選択 → REST 挿入 2 行 → エクスポート（グリッド / CSV / JSON）→ 更新 → アップサート検証 → 削除 → Bulk エクスポート → Bulk 挿入 → Bulk 削除まで全 25 項目 PASS（グラウンドトゥルースは `sf data query`）
- バージョン: **0.6.0**（`SfUi.App.csproj` = 0.6.0 / `AppxManifest.xml` = 0.6.0.0 / MSIX = `dist\SfUi_0.6.0.0_x64.msix`）
- 知見: Composite API は各レコードの**先頭**に `attributes: {type: ...}` が必須（欠けると JSON_PARSER_ERROR）/ WPF の確認 MessageBox は UIA のボタン列挙が不安定なため Enter 送信で確定 / PowerShell の 1 要素配列スカラー展開（`@()` で回避）に注意

### Phase 14: データ入出力ウィンドウ アクセス権限タブ（オブジェクト / 項目 / レコード） ✅

データ入出力ウィンドウに、権限（アクセス権）を一覧化する 3 タブを追加。設計書と実 API 調査結果: `docs/access-tabs-plan.md`。

- オブジェクトアクセス: 選択中オブジェクトに対する Permission Sets / Permission Set Groups / Profiles の権限を一覧（種類 / ラベル / API 名 / カスタム / Read / Create / Edit / Delete / View All Records / Modify All Records / View All Fields。検索付き）。PSG 行は構成 PS の和集合
- 項目アクセス: 項目（行）× 権限主体（列）のマトリクス（セル = R / E）。種類フィルタ（プロファイル / 権限セット / PSG）・行の検索・列絞り込み付き。データ元は `FieldPermissions` の明示行のみ
- レコードアクセス: 任意 SOQL で対象レコードを抽出（上限 10,000 件・queryMore）→ 200 件 / ページでページング + 検索。有効ユーザーをチェックボックス選択（名前検索・3 行でスクロール・全選択 / 全解除）し、UserRecordAccess でユーザーごとの読取 / 編集 / 削除 / 転送を表示（リンク列でレコードをブラウザーで開ける）
- 実測した API 制約: UserRecordAccess は `UserId = '<単一 ID>'` のみ（IN 不可）・SELECT 可は RecordId / Has*Access / MaxAccessLevel のみ・**1 クエリ 200 行上限**（ページサイズ 200 はこの上限に適合）・アクセスなしでも全 false 行が返る
- Core: `PermissionSubject` / `ObjectAccessRow` / `FieldAccessSnapshot` / `RecordAccessUser` / `RecordQueryResult` / `UserRecordAccessFlags` + `PermissionAccessService` / `RecordAccessService` / `RestQueryPager`（query + queryMore 共通化）
- テスト: 356 件（+15: カタログ解析 / PSG 和集合 / 権限行マッピング / アクセス行 / チャンク分割 / レコード解析）
- スモーク: `--smoke --smoke-access <org>`（カタログ / オブジェクト権限 / 項目権限 / 有効ユーザー / 対象レコード / UserRecordAccess）
- UI チェック（`C:\huqian\sfui-access-tabs-check.ps1`・読み取りのみ）: 全 20 項目 PASS（178 行 / 71 項目 × 178 主体 / 種類フィルタ 178 → 160 列 / ユーザー 13 人のスクロールリスト / 15 件ページング / ユーザー権限列 / 検索絞り込み）
- バージョン: **0.7.0**（`SfUi.App.csproj` = 0.7.0 / `AppxManifest.xml` = 0.7.0.0 / MSIX = `dist\SfUi_0.7.0.0_x64.msix`）
- 知見: PSG には IsCustom が無い（常にカスタム扱い）/ PermissionSetGroupComponent にミューティング識別が無いため和集合で近似 / プロファイルは `IsOwnedByProfile = true` の PermissionSet（ラベル = Profile.Name）/ UIA の ValuePattern.SetValue では WPF ComboBox のテキストサーチが働かない → オブジェクト名の完全一致で選択するよう DataIoViewModel を改善
- 2026-10-04 改修（v0.7.1）: オブジェクト / 項目アクセスに検索（行の内容で絞り込み・AND・スペース区切り・「表示: n 件 / 全 m 件」）を追加。UI チェック 24 項目 PASS

### Phase 15: レコードのバックアップ・復元 ✅

選択中組織のレコードをローカルに**バックアップ**し、あとから**復元**する独立ウィンドウ（非モーダル）。設計書: `docs/backup-window-plan.md`。メイン上部バーの「バックアップと復元」から開く。

- 保存形式: `data/backups/<yyyyMMdd-HHmmss>/`（`metadata.json` + オブジェクトごとの `<Object>.json`（REST）/ `<Object>.csv`（Bulk）+ `backups/counts.json` キャッシュ + `backup-state.json` 選択状態）
- バックアップタブ: オブジェクト一覧（検索・件数付き・背景取得 + キャッシュ + 再取得ボタン・選択は組織ごとに記憶 / 「選択をクリア」あり）→ ラベル / 説明（開いた時点で自動入力: `{エイリアス}_{yyyy-MM-dd_HH-mm}` / エイリアス・インスタンス URL・組織 ID・種類（本番 / サンドボックス）。クリアして実行した場合は再生成）→ 実行（進捗 / キャンセル）。件数 ≤ `backupRestMaxRecords`（既定 2000）は REST（JSON）、超える場合は Bulk API（CSV）
- バックアップ比較タブ: 基準 (A) と比較対象 (B) の 2 つのバックアップをオブジェクト単位にレコード突合（キー = Id。値は REST の型付き値と Bulk CSV の文字列・null ↔ 空・数値/真偽値/日時の表記差を吸収）。結果グリッドは追加 / 削除 / 変更と状態を表示し、**差分のある行を色でハイライト**（差分 = 黄 / A のみ = 青 / B のみ = 紫 / エラー = 赤）+ 「差分のみ」フィルタ。「↗」で**レコード単位の比較結果を別ウィンドウ**（状態 / Id / 表示名 / `項目: A → B` の変更内容、検索 + 200 件/ページ、上限 20,000 件）
- 復元タブ: バックアップ選択（検索・削除）→ オブジェクト一覧（件数・エンジン・キー項目 ComboBox・「レコード」ボタンで別ウィンドウのレコード詳細）→ 照合方式（自動 / Id / キー）× 既存一致時（スキップ / 上書き）→ 実行。結果グリッド（作成 / 上書き / 復元 / スキップ / 失敗 + エラー）
- 復元の意味論（実測に基づく）: **同じ組織** = Id で照合。既存 → スキップ / 上書き（PATCH）、**削除済み → SOAP undelete で Id を維持して復元**（親の削除でカスケード削除された子は undelete 不可のため新規挿入にフォールバック）、完全に無い → 新規挿入（Id は新規 + 旧 Id → 新 Id の**参照張り替え**）。**別の組織** = キー項目（既定 Name）で照合
- レコード詳細ウィンドウ / 比較のレコード単位ウィンドウ: 先頭列に「レコードページを開く」列（↗）を追加。バックアップ元組織のインスタンス URL を解決して `{instanceUrl}/lightning/r/{Object}/{Id}/view` をブラウザーで開く（現在の組織と同一なら sf CLI を呼ばず即時、別組織は認証済み組織一覧から検索）
- レコード詳細ウィンドウ: 列はバックアップ内容から動的生成・200 件/ページ・AND 検索・上限 20,000 件（打ち切り表示）
- Core: `BackupModels` / `BackupStateStore` / `SalesforceSoapClient`（partner SOAP の undelete のみ）/ `BackupService`（並列件数取得・REST/Bulk バックアップ・Id/キー照合復元・参照張り替え・結果集計）/ `BackupCompareService`（バックアップ間のレコード突合・差分計算。ローカル ファイルのみで組織 API は呼ばない）
- テスト: 380 件（+22: SOQL 生成 / JSON 解析 / フィールド構築 / CSV 変換 / 表示変換 / パストラバーサル防止 / メタデータ round-trip / ストア / SOAP エンベロープ・解析 / 値正規化 / 差分計算 / REST×Bulk 比較 / レコードページ URL 解決）
- スモーク: `--smoke --smoke-backup <org>`（マーカーレコード作成 → バックアップ → 削除 → Id 復元（undelete で Id 維持）→ 上書き → キー復元（新 Id + 参照張り替え）→ Bulk 確認 → 後片付け。全項目成功）
- UI チェック（`C:\huqian\sfui-backup-ui-check.ps1`・読み取りのみ）: 全 20 項目 PASS（タブ 3 つ / オブジェクト一覧 33 行 / 自動ラベル `acc_yyyy-MM-dd_HH-mm` / 自動説明（URL・組織 ID・種類）/ フィルタ 21 行 / 選択の記憶 / 選択クリア / バックアップ一覧 / 復元グリッド + キー項目 / レコード詳細 70 列 + レコードページ列 16 件 / 比較グリッド 9 列 × 2 行 / 比較のレコード単位ウィンドウ 29 行 + レコードページ列 21 件）
- バージョン: **0.8.0**（`SfUi.App.csproj` = 0.8.0 / `AppxManifest.xml` = 0.8.0.0）
- 知見: Id を指定した挿入は REST / Bulk / SOAP のいずれも不可（`INVALID_FIELD_FOR_INSERT_UPDATE cannot specify Id in an insert call`）→ Id 維持は undelete のみ / partner SOAP には `SOAPAction: ""` ヘッダーが必要（無いと応答を解析できない）/ 親の削除時にカスケード削除された子レコードは個別 undelete 不可（"Entity is not in the recycle bin"）

## 10. 検証計画

1. `dotnet build` / `dotnet test`（引数クォート・JSON 解析・ストア round-trip・CSV）
2. `sf org list --json` と UI 組織コンボの一致、1 クリック切替
3. 同一 SOQL の REST 実行と `sf data query` の結果一致、CSV 出力
4. 匿名 Apex 成功 / コンパイルエラー / 例外 + 直後ログ
5. 再起動時の履歴・お気に入り・前回組織 / フォルダ復元
6. フォルダ切替 → ターミナル cwd・VS Code・エクスプローラー・ブラウザ
7. 単一 EXE を別パスにコピー → 起動 → `data/` 生成 → 組織一覧

## 11. スコープ外（初期）

- sfdx v1、Apex テスト実行 / トレース、Bulk API、SOSL、ソース追跡系、複数ウィンドウ、tail log、トレイ常駐 / グローバルホットキー

## 12. 開発メモ

- VS Code タスク: build / test / run / run (smoke) / run (smoke org) / publish (single exe)
- UI 文字列: コードは `UiText.T("Key")`、XAML は `{loc:Tr Key}`。辞書は `src/SfUi.Core/Localization/UiText.En.cs` / `UiText.Ja.cs`（キーは両ファイルで同一 — テストで検証）
- スモーク起動: `dotnet run --project src/SfUi.App -- --smoke`（sf CLI で組織一覧取得を検証して自動終了、`data/logs` に記録）
- スモーク起動（組織情報）: `dotnet run --project src/SfUi.App -- --smoke --smoke-orginfo <alias>`（初回は概要・ユーザーを取得、2 回目以降は API を呼ばずキャッシュを使用。`--smoke-orginfo-refresh` を付けると手動再取得で fetchedAt の更新を検証）
- sf のフラグは実装時に `sf <command> --help` で確定する（バージョン差吸収）

## 13. 実装進捗

- ✅ **Phase 0（2026-10-02 完了）**: プロジェクト基盤
  - `dotnet build`: 3 プロジェクト成功（SfUi.Core / SfUi.App / SfUi.Tests）
  - `dotnet test`: 5 件成功（AppPaths 解決 3 件 / AppLog 1 件 / DI 登録 1 件）
  - スモーク起動: `--smoke` で起動 → MainWindow 表示 → 3 秒後に正常終了（ExitCode=0、`data/logs/app-20261002.log` に記録）
  - データフォルダ自動解決: 開発時 `c:\huqian\vscode\SfUi\data\`（logs / history / results / tmp 生成確認）
  - VS Code タスク: build / test / run / run (smoke)

- ✅ **Phase 1（2026-10-02 完了）**: sf CLI 実行基盤＋組織管理
  - `SfCliRunner`: cmd.exe /d /s /c 経由実行、UTF-8、タイムアウト（既定 120 秒）・キャンセル、引用符処理を単体テストで検証
  - `OrgService`: `sf org list --json` のカテゴリ重複（other / sandboxes / nonScratchOrgs）を username で排除して 12 組織を取得。トークンは `sf org display` + 30 分キャッシュ
  - 上部バー: 組織コンボ + 更新ボタン、SFフォルダ（編集 + `OpenFolderDialog`）
  - `dotnet test`: 25 件成功（引用符・コマンドライン 10 / JSON 解析 6 / 組織一覧 4 / 基盤 5）
  - スモーク起動: 実機で sf CLI を呼び出し「組織一覧取得成功: 12 件」を確認（ExitCode=0）

- ✅ **Phase 2（2026-10-02 完了）**: JSON 永続化＋履歴ビュー
  - `AtomicJsonFile`: 原子的書き込み（File.Replace + .bak）、破損時フォールバックをテストで検証
  - `AppSettingsStore` / `RecentItemsStore<T>`（フォルダ・URL）/ `FavoritesStore` / `HistoryStore`（結果の閾値分離保存）
  - 履歴ビュー: 種別フィルタ・検索・パラメータコピー・削除・右クリックメニュー
  - 上部バー: 最近フォルダ選択（ピン留め対応）、前回組織 / 前回フォルダの復元
  - `dotnet test`: 47 件成功（+22 件: 設定 4 / 最近項目 7 / お気に入り 4 / 履歴 7）
  - スモーク起動: `--smoke: ストアOK (履歴 0 件 / 最近フォルダ 0 件 / お気に入り 0 件, 履歴上限 2000 件 / 結果閾値 65536 B)` + `settings.json` 生成を実機確認

- ✅ **Phase 3（2026-10-02 完了）**: SOQL・匿名Apex・デバッグログ＋履歴連携
  - 実機検証: `sf data query` / `sf apex run` / `sf apex list log` / `sf apex get log` の出力形状を確認（get log は -i/-n 必須、run の logs に実行ログ全文）
  - `dotnet test`: 69 件成功（+22 件: CSV 8 / DataTable 平坦化 4 / ApexService パース 10）
  - スモーク E2E（`--smoke --smoke-org hks4sand1`）: `SOQL(REST) OK: 1 件 / 3170 ms`、`匿名Apex 実行: success=True, compiled=True, logs=1209 文字`、`ログ一覧: 0 件`
  - 知見: 匿名Apex 一時ファイルは **BOM なし UTF-8** で保存（BOM 付きだと compiled=false で失敗）

- ✅ **Phase 4（2026-10-02 完了）**: ツールランチャー
  - `ToolLauncherService`（wt / PowerShell / cmd / WSL、explorer、VS Code、ブラウザ）+ WSL パス変換の単体テスト
  - 上部バーの 4 ボタンを有効化（クリック = 既定起動、右クリック = バリアント）
  - ブラウザメニュー（組織ホーム / Setup / ログイン / URL 入力 / 最近の URL）
  - `dotnet test`: 76 件成功（+7 件: WSL パス変換 6 / ツール検出 1）
  - スモーク: `--smoke: ツール検出: wt=...\WindowsApps\wt.exe, code=...\Microsoft VS Code\bin\code.cmd` を確認

- ✅ **Phase 5（2026-10-02 完了）**: 自由コマンド・REST コンソール・デプロイ
  - `DeployService`（操作 5 種の引数構築を単体テストで検証。deploy/validate は -d/-x、quick/report は --job-id / --use-most-recent、retrieve は -d/-x）
  - 自由コマンドビュー（Enter 実行・危険操作確認・stdout/stderr・履歴 / お気に入り）
  - REST コンソール（`SalesforceRestClient.SendConsoleAsync` が HTTP ステータス + 本文を例外なしで返却）
  - `dotnet test`: 103 件成功（+27 件: DeployService 10 / CommandLineParser 10 / SfCommandSafety 4 / JsonPretty 3）
  - スモーク E2E（`--smoke --smoke-org hks4sand1`）: `RESTコンソール GET /limits: HTTP 200 (4,617 文字)`。SOQL / 匿名Apex / ログも再確認
  - 知見: TextBox の Text は既定 TwoWay のため、読み取り専用プロパティ（実行コマンド プレビュー等）へのバインドには `Mode=OneWay` が必須（レイアウト時に InvalidOperationException）
  - 補足: 実デプロイは副作用があるためスモークには含めず、引数構築の単体テスト + 手動確認とする

- ✅ **Phase 6（2026-10-02 完了）**: 仕上げ・配布（全フェーズ完了）
  - 設定モデル拡張: `SfExecutablePath` / `WindowsTerminalPath` / `VsCodePath` / `ConfirmPolicy`（dangerous / always / never、旧 `ConfirmDangerousCommands` から移行）
  - 設定画面: sf / ツールパスの参照・クリア・自動検出表示、履歴上限・閾値のバリデーション、データフォルダを開く
  - クイックパネル: お気に入りを数字スロット付きで表示、`Ctrl+1..9` / ダブルクリック / Enter で実行（SOQL は即実行、他は安全に読み込み）、右クリックで削除
  - ショートカット: `F5` = 直前の操作を再実行（SOQL のみ自動実行）
  - スモーク強化: 全 8 タブを順に選択してレイアウト検証（バインディング例外をカウント、0 以外は失敗扱い）
  - `dotnet test`: 120 件成功（+17 件: 確認ポリシー 13 / sf パス解決 4）
  - スモーク E2E: `全タブのレイアウトOK (例外 0 件)` + 従来の SOQL / 匿名Apex / ログ / REST コンソール
  - 単一 EXE 検証: `dist/SfUi.exe`（約 164 MB、自己完結）を `C:\huqian\sfui-publish-test` へコピー → 起動で `data/` 自動生成 → 組織 12 件 → 全タブ検証 → ExitCode=0

- ✅ **追加（2026-10-02）**: サンプル履歴の投入
  - `SampleHistorySeeder`（SOQL 9 / 匿名Apex 6 / コマンド 8 / REST 7 = 合計 30 件。種別 + パラメータで重複スキップ）
  - 起動オプション `--seed-samples`（UI を出さず投入して終了）、設定画面の「サンプル履歴を投入」ボタン、VS Code タスク `seed samples`
  - `dotnet test`: 123 件成功（+3: 追加 / 重複スキップ / 既存保持）
  - 実データ投入確認: 追加 29 件 / スキップ 1 件（既存と同内容が 1 件）→ 再実行で追加 0 件 / スキップ 30 件

- ✅ **Phase 7（2026-10-02 完了）**: 多言語対応（英語 / 日本語）
  - 言語切替: 上部バーのコンボで**即時切替**（再起動不要）、settings.json に保存（既定は英語）
  - `dotnet test`: 133 件成功（+10: UiText の辞書整合性 / 切替動作 / XAML・C# の使用キー存在検証）
  - スモーク E2E: `全タブのレイアウトOK (例外 0 件)` + **`言語切替OK (ja→en, 例外 0 件)`** + 従来の E2E すべて成功
  - 備考: アプリログ（data/logs）と XML ドキュメントコメントは開発者向けのため日本語のまま

- ✅ **Phase 8（2026-10-02 完了）**: AI チャット（DeepSeek）
  - AI は**右サイドパネル**（上部バーの「AI」トグルで表示/非表示、状態は settings.json に保存）
  - `dotnet test`: 142 件成功（+9: DeepSeek 応答 / エラー / usage 解析、キー解決）
  - 実 API 検証（`--smoke-ai`）: `OK（OK / 1.2 秒 / tokens 11+1）` + 全 8 タブ・両パネル・言語切替例外 0 件
  - セキュリティ: API キーは settings.json（gitignore 対象）→ 環境変数 → 内蔵（評価用）の順で解決。評価用キーは XOR+Base64 難読化で同梱するためダウンロード直後でも AI を試せる（難読化のみで暗号学的保護ではない。README / MD にはキー値を記載しない）

- ✅ **Microsoft Store 配布準備（2026-10-03）**: `packaging/` 一式（AppxManifest / Make-Msix.ps1 / New-Icon.ps1 / Assets / README）と EXE・ウィンドウ アイコンを追加。`dist/SfUi_0.2.0.0_x64.msix`（MinVersion 10.0.17763.0）を生成・一時キー署名・検証済み。Partner Center 提出のための登録情報・`runFullTrust` 用途申請・プライバシー ポリシーを準備（審査準備中）。知見: Identity の Version 更新は XML で行う（文字列 regex だと `MinVersion` の末尾に誤マッチして破壊する）

- ✅ **Phase 10（2026-10-03 完了）**: 組織情報ウィンドウ（設計: `docs/org-info-window-plan.md`）
  - Step 1（Core 基盤: モデル/キャッシュ/クエリ/URL/概要・ユーザ・プロファイル・権限セット・ロール・オブジェクト・OWD）/ Step 2（ウィンドウ UI: 複数ウィンドウ・タブ・初回のみ自動取得・再取得・タブ内検索）/ Step 3（オブジェクト項目の遅延取得・全タブ検索・Setup リンク・権限エラー）/ Step 4（主な設定・追加候補 10 タブ・マイ設定のカスタムタブ・AI 連携）を実装
  - `dotnet test`: 242 件成功（+97 件: Step 1 = 42 / Step 3 = 16 / Step 4 = 39）
  - E2E（実アプリ + UI Automation）: 初回 8 セクション取得 → 再オープンで再取得なし（キャッシュ）/ オブジェクト項目 63 件 / 全タブ検索 3 件 + タブジャンプ / カスタムタブ作成→保存→再オープン復元 / AI のタブデータ添付 (197 文字) + クイックプロンプト / 多通貨無効組織の通貨タブ専用メッセージ / 日本語表示（新規キー約 200 件）を実機確認
  - スモーク E2E（`--smoke --smoke-orginfo acc`）: 初回「overview / users を取得」→ 2 回目「API 呼び出し = 0 セクション（キャッシュのみ）」→ `--smoke-orginfo-refresh` で「fetchedAt 更新 (updated=True)」をログで確認
  - バージョン: **0.3.0**（AppxManifest は 0.3.0.0。MinVersion 10.0.17763.0 は据え置き）
  - 知見: 設定値は `Organization` の `Preferences*` 等（describe で存在確認）/ `CurrencyType` は多通貨無効組織では sObject 自体が未サポート / Tooling `SubscriberPackageVersion` は `Id = '...'` の単一形式のみ / `SetupAuditTrail.CreatedBy` は null になり得る / UIA の ListBox 項目名はデータ型の ToString になるため record に ToString を実装
- ✅ **Phase 11（2026-10-03 完了）**: AI 接続先の汎用化（OpenAI 互換 / Anthropic / ローカル LLM）
  - `AiChatClient`（Core）に改名・汎用化: `aiEndpoint` / `aiApiKey` / `aiModel`（旧 `deepSeek*` は自動移行）/ プリセット 4 種 / エンドポイント・モデル欄 / キーなしカスタム接続先は Authorization を送らない
  - `dotnet test`: 251 件成功（+9: 解決順・抽出・設定移行）
  - スモーク E2E: ローカルスタブ（キーあり → Authorization あり / カスタム + キーなし → ヘッダーなし、どちらも OK）/ 実 OpenAI（認証成功・残高なし 429）/ DeepSeek 既定 builtin キー OK
  - 設定画面の AI グループ表示を UI Automation + スクリーンショットで確認
  - バージョン: **0.4.0** 化（`SfUi.App.csproj` / `AppxManifest.xml` / `dist\SfUi_0.4.0.0_x64.msix` 再ビルド）

- ✅ **Phase 12（2026-10-03 完了）**: 組織比較ウィンドウ（複数組織の一括比較）
  - Core: `OrgCompareModels` / `OrgCompareService` / `OrgCompareStateStore` + DI。UI: `CompareOrgsWindow` / `CompareCategoryView` / `CompareOrgsViewModel` / `CompareOrgsWindowFactory` + メイン「組織比較」ボタン + UiText 21 キー
  - `dotnet test`: 277 件成功（+25）
  - スモーク: `--smoke --smoke-compare hks4sand1,acc` で全 13 カテゴリを構築（settings 20/0・owds 787/652・users 31/31・permissionSets 290/250・objects 779/650・apexClasses 558/558・flows 356/288 行〔行数/差分〕）。VS Code タスク `run (smoke compare)` 追加
  - E2E（`C:\huqian\sfui-compare-verify.ps1`）: 12 組織 / 13 タブ / 差分のみフィルタ / CSV 290 行 OK。知見: Windows 11 最新式 SaveFileDialog は UIA ValuePattern 不可（Pane）→ 自動化はキーボード操作で保存
  - バージョン: **0.5.1** 化（`SfUi.App.csproj` / `AppxManifest.xml` / `dist\SfUi_0.5.1.0_x64.msix` 再ビルド）
  - 2026-10-03 改修（v0.5.1）: 組織チェックを最大 3 列 + スクロール / 各タブ検索（AND・「表示: n 件 / 全 m 件」）/ 差分行のみ黄色ハイライト（交互色を廃止）/ MainWindow 幅 1440 → 1520（言語プルダウンの見切れ対応）

- ✅ **Phase 13（2026-10-04 完了）**: データ入出力ウィンドウ（設計: `docs/data-io-window-plan.md`）
  - Core 9 ファイル（CSV パーサ / 型変換 / マッピング / バッチ計画 / 結果マッピング / SOQL ビルダ / describe キャッシュ / エクスポート / インポートサービス）+ テスト 64 件（合計 341 件成功）/ UI = DataIoWindow（エクスポート・インポートの 2 タブ + 共通の組織・オブジェクト選択と項目数表示）/ メイン「データ入出力」+ 組織情報オブジェクトタブ「データ入出力」（選択中オブジェクトを事前選択）
  - E2E（`C:\huqian\sfui-dataio-e2e.ps1`）: REST 挿入 → エクスポート（CSV/JSON）→ 更新 → 削除 → Bulk エクスポート/挿入/削除まで 25 項目 PASS / `sf data query` と件数一致（挿入 2 → 更新 2 → 削除 0 → Bulk 挿入 1 → Bulk 削除 0）。知見: composite/sobjects は `attributes` を先頭に含める必要あり / 確認 MessageBox は UIA ボタン列挙が不安定で Enter 送信で確定 / PS の 1 要素配列スカラー展開に `@()` で対処
  - バージョン: **0.6.0** 化（`SfUi.App.csproj` / `AppxManifest.xml` / `dist\SfUi_0.6.0.0_x64.msix` 再ビルド）

- ✅ **Phase 14（2026-10-04 完了）**: データ入出力ウィンドウ アクセス権限タブ（設計: `docs/access-tabs-plan.md`）
  - オブジェクトアクセス（PS / PSG / プロファイル × 11 列・PSG は構成 PS の和集合）/ 項目アクセス（項目 × 主体のマトリクス・種類フィルタ・列絞り込み）/ レコードアクセス（UserRecordAccess・ユーザーチェックボックス + 名前検索・3 行スクロール・SOQL 抽出・200 件ページング・検索・レコードリンク）
  - テスト 356 件成功（+15）/ スモーク `--smoke --smoke-access acc`（カタログ 178 件・オブジェクト権限 178 行・項目権限 178 主体・有効ユーザー 13 件・UserRecordAccess 動作）/ UI チェック 20 項目 PASS
  - 実測制約: UserRecordAccess は単一 UserId のみ・200 行 / クエリ上限（ページ 200 件に一致）・アクセスなしでも全 false 行あり
  - 改善: 対象オブジェクト欄への API 名完全一致入力で describe を読み込むように（UIA / 手入力どちらでも安定）
  - バージョン: **0.7.0** 化（`SfUi.App.csproj` / `AppxManifest.xml` / `dist\SfUi_0.7.0.0_x64.msix` 再ビルド）
  - 2026-10-04 改修（v0.7.1）: オブジェクト / 項目アクセスに検索（行の内容・AND・スペース区切り・表示件数付き）を追加 / UI チェック 24 項目 PASS
  - 2026-10-04: GitHub Release **v0.7.1** 公開（アセット = `SfUi.exe` + `SfUi-v0.7.1-portable.zip`。v0.5.1 → v0.7.1 の累積リリース）

- ✅ **2026-10-04 改修（v0.7.2）**: ブラウザボタンをセッション付き URL（ログイン不要）で開く
  - `OrgService.GetFrontDoorUrlAsync` を追加し、`sf org open --url-only --target-org <org> --path <path> --json` の `result.url`（`/secur/frontdoor.jsp?sid=...&retURL=...`）で組織ホーム / セットアップを開く。ブラウザーでの再ログイン不要
  - 取得失敗（未認証・CLI エラー・タイムアウト）時は従来の通常 URL にフォールバック。セッション URL は認証情報を含むため「最近の URL」には保存しない
  - テスト 358 件（+2: frontdoor URL 解析）/ 実機 UI 検証（ブラウザボタン左クリック → ステータスに `frontdoor.jsp?sid=` 表示・PASS）
  - 2026-10-04: GitHub Release **v0.7.2** 公開（アセット = `SfUi.exe` + `SfUi-v0.7.2-portable.zip`）

- ✅ **2026-10-04 改修（v0.7.3）**: Main ウィンドウ上部バーのボタンを Fluent アイコン化
  - Microsoft Fluent UI System Icons（MIT）からアイコンを取得し、`src/SfUi.App/Resources/Icons.xaml`（StreamGeometry）として同梱
  - 上部バーの全ボタン（組織更新 / 組織情報 / 組織比較 / データ入出力 / 参照 / ターミナル / エクスプローラー / VS Code / ブラウザ / クイックパネル / AI）をアイコンのみに変更
  - マウスオーバーで「ラベル + 説明」のツールチップを表示。AI トグルはオン/オフで Sparkle の regular / filled を切替
  - アクセシビリティ / E2E 互換のため `AutomationProperties.Name` にラベルを設定（既存 UI チェック・E2E のボタン名検索が継続動作）
  - 実機検証: トップバー スクリーンショット確認 / ツールチップ probe（UIA でラベル + 説明を確認・PASS）/ 既存 UI チェック OK（テスト 358 件）
  - 2026-10-04: README のスクリーンショット（EN/JA 各 5 枚）を新 UI で再撮影。GitHub Release **v0.7.3** 公開（アセット = `SfUi.exe` + `SfUi-v0.7.3-portable.zip`）

- ✅ **Phase 15（2026-10-04 完了）**: レコードのバックアップ・復元（設計: `docs/backup-window-plan.md`）
  - メイン上部バー「バックアップと復元」（Fluent の Cloud Arrow Down アイコン）+ BackupWindow（バックアップ / 復元 / バックアップ比較の 3 タブ）+ レコード詳細・比較レコードの別ウィンドウ
  - バックアップ タブ: ラベル / 説明の自動入力（`{エイリアス}_{yyyy-MM-dd_HH-mm}` / URL・組織 ID・種類）+ 選択をクリア。復元は Id（undelete で Id 維持）/ キー照合 + 参照張り替え
  - バックアップ比較タブ: 2 バックアップのレコード突合（REST×Bulk の型差を吸収）→ 差分行を色でハイライト + レコード単位の差分を別ウィンドウで表示（変更は `項目: A → B`）
  - Core: `BackupModels` / `BackupStateStore` / `SalesforceSoapClient`（undelete）/ `BackupService`（背景件数取得・REST/Bulk 自動切替・Id/キー照合・参照張り替え）/ `BackupCompareService`（差分計算）
  - テスト 376 件成功（+18）/ スモーク `--smoke --smoke-backup acc`（undelete で Id 維持・上書き・キー復元の参照張り替え・Bulk 保存まで全項目成功）/ UI チェック 17 項目 PASS
  - バージョン **0.8.0**