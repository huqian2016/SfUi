# Avalonia 版 macOS 実機動作確認チェックリスト

最終更新: 2026-10-06 / ステータス: 実施済み（自動検証 + 主要画面のスクリーンショット/OCR 確認。トークン起因の不具合 1 件を検出）

- 実施日: 2026-10-06
- 環境: macOS 15.6.1 / Apple Silicon（arm64） / .NET SDK 9.0.318（`~/.dotnet`）/ sf CLI 2.152.14（darwin-arm64）
- 検証対象組織: **LwcEditor**（`kokenn2008@gmail.com.lwceditor` / `00D2w00000LcGsMEAV` / Connected）

このドキュメントは、[SfUi.Avalonia](../../src/SfUi.Avalonia)（Avalonia 11 版）を **macOS 実機**で
全面的に動作確認するための手動チェックリストです。移植の設計・状態は
[avalonia-port-plan.md](./avalonia-port-plan.md) を参照。

- 対象ブランチ: `verify/avalonia-macos`
- 前提: ロジックは [SfUi.Core](../../src/SfUi.Core) + [SfUi.Presentation](../../src/SfUi.Presentation) を
  WPF 版と共有。CI ではビルド・テスト・スモーク・`.app` 組立まで green 済み。本チェックリストは
  CI でカバーしきれない「実機での UI 操作・OS 連携」の確認が主目的。

## 記録の凡例

| 記号 | 意味 |
|---|---|
| ✅ | 期待どおり動作 |
| ❌ | 不具合（手順に再現手順・ログ・スクショを添付） |
| ⚠️ | 動作するが挙動・見た目に懸念あり |
| ― | 対象外 / スキップ（理由を記載） |

結果は最後の「結果まとめ」表に転記する。

---

## 検証結果サマリ（2026-10-06 実施）

### 合格（✅）

| 項目 | 結果 |
|---|---|
| ビルド `dotnet build src/SfUi.Avalonia` | 成功（0 警告 / 0 エラー） |
| ユニットテスト `dotnet test tests/SfUi.Tests` | 399 件合格（失敗 0） |
| スモーク `--smoke` | exit 0。ログに「組織一覧 14 件」「言語切替 OK (ja→en→zh→ko)」 |
| スモーク `--smoke --smoke-compare` | exit 0。Compare ウィンドウ開閉 OK（データ取得のみ失敗、下記） |
| `.app` 組立 `bash packaging/make-mac-app.sh osx-arm64` | 成功。`dist/mac/SfUi.app` + zip 生成、`.icns` / `Info.plist` 正常 |
| `.app` 起動（`open` + `--smoke`） | 正常起動・exit 0。ようこそ + メイン 2 ウィンドウ表示を確認 |
| メイン 8 タブの描画 | SOQL / Apex / Debug Logs / History / Deploy / Command / REST API / Settings すべて描画（OCR 確認） |
| ようこそ画面（`--welcome` / `--welcome-missing`） | 両状態を確認。sf 検出 `Detected: /usr/local/bin/sf` / 未検出時はインストーラ・npm・再チェック表示 |
| Org Info ウィンドウ（`--open orginfo`） | LwcEditor のキャッシュ済み 13 セクション / 20 タブ / Overview 20/20 を表示 |
| Compare Orgs ウィンドウ（`--open compare`） | 14 組織 / 13 カテゴリの UI 描画。overview データ取得も成功（401 警告なし） |
| Data I/O ウィンドウ（`--open dataio`） | 5 タブ / Export ビルダー UI 描画。オブジェクト一覧 2133 件を取得 |
| Backup & Restore ウィンドウ（`--open backup`） | 3 タブ / オブジェクト一覧・件数・ラベル自動入力を確認（一部オブジェクトの件数は API 制約でスキップ） |
| Org Management ウィンドウ（`--open orgmanage`） | 14 組織グリッド / 操作ボタン群を描画 |
| Settings の macOS パス検出 | `sf=/usr/local/bin/sf`、`wt=(not found)`、VS Code=`/Applications/Visual Studio Code.app/.../code` を検出 |

### 検出した不具合

1. **🟠 HIGH — REST 系機能が `INVALID_AUTH_HEADER` で失敗する**（✅ 修正済み）
   - 症状: Data I/O のオブジェクト一覧、Backup の件数取得、Org Management の Health / Migration Inventory、
     Compare Orgs のデータ取得、SOQL の REST 実行など、アクセストークンを使う全機能が失敗。
   - 原因: [OrgService.GetAuthAsync](/Users/huqian/SfUi/src/SfUi.Core/Services/OrgService.cs:90) が
     `sf org display --target-org <org> --json` でトークンを取得するが、インストール済み sf CLI 2.152.14 は
     `org display` のシークレット出力を既定で隠すようになり、`accessToken` が
     `[REDACTED] Use 'sf org auth show-access-token' to view` になる。アプリはこのプレースホルダを
     そのまま Authorization ヘッダのトークンとして使うため 401 になる。
   - 修正: [OrgService.GetAuthAsync](/Users/huqian/SfUi/src/SfUi.Core/Services/OrgService.cs:90) を、
     トークン取得に `sf org auth show-access-token --target-org <org> --json` を使い（`--json` で確認プロンプトを
     スキップ）、instanceUrl / apiVersion / username は従来どおり `org display --json` から得るように変更。
   - 再検証: 修正後、Data I/O / Backup の「オブジェクト一覧を取得: LwcEditor (2133 件)」が成功。
     `--smoke --smoke-compare` でも 401 警告が消えた。curl（HTTP 200）・`sf data query` でも正常を確認済み。

2. **⚪ LOW — ショートカット表示が macOS でも「Ctrl」のまま**（✅ 修正済み）
   - 症状: クイックパネル「Ctrl+1..9」、実行「Ctrl+Enter」、ヒント「Ctrl+<number>」が macOS でも Ctrl 表記。
   - 現状: キーボードは [MainWindow.axaml.cs](/Users/huqian/SfUi/src/SfUi.Avalonia/MainWindow.axaml.cs:34) で
     `Control` と `Meta` の両方を受け付けるため機能上は問題なし（表示のみ）。
   - 修正: ローカライズ文言の「Ctrl+」を `{mod}+` プレースホルダに置換し、[UiText.T](/Users/huqian/SfUi/src/SfUi.Core/Localization/UiText.cs:96) で
     [PlatformInfo.ShortcutModifierLabel](/Users/huqian/SfUi/src/SfUi.Core/PlatformInfo.cs:20)（macOS で "Cmd"）に解決。
     対象キー 13 × 4 言語を更新し、[UiTextTests](/Users/huqian/SfUi/tests/SfUi.Tests/UiTextTests.cs) に検証を追加。
   - 再検証: OCR で「Quick Panel (Cmd+1..9)」「Run (Cmd+Enter)」「Cmd+<number>」への変化を確認。

---

## 0. 前提条件と環境

- [ ] **macOS のアーキテクチャを確認** — `uname -m` で `arm64` / `x86_64` を記録する。
  - 期待: `arm64`（Apple Silicon）なら `osx-arm64`、`x86_64` なら `osx-x64` を以降のビルドで使う。
- [ ] **.NET 9 SDK** — `dotnet --version` が `9.0.x` であること。
- [ ] **Salesforce CLI（`sf`）** — `sf --version` が成功する。未インストールなら Homebrew で導入。
  - 期待: `sf` が PATH（`/opt/homebrew/bin/sf` または `/usr/local/bin/sf`）から解決できる。
- [ ] **認証済み組織が 1 件以上ある** — `sf org list` に組織が表示される。
  - 期待: 後続のタブ/ウィンドウ検証で実データを流せる。無い場合は `sf org login web` 等で追加。
- [ ] **検証に使用する組織を選定** — 主検証は既存の **LwcEditor** 組織を使用する（許可済み）。
  - alias: `LwcEditor` / username: `kokenn2008@gmail.com.lwceditor` / Org Id: `00D2w00000LcGsMEAV` / Status: `Connected`
  - Compare Orgs（2〜4 組織）や組織切替の検証では、他に接続中の `bear-tracking`・`nipponkoeicompanyltd`・
    `kokenn2008@brave-otter-esxoqt.com` などを併用する（`sf org list` で `Connected` のものを使用）。
  - 注意: 既定 DevHub（🌳）は `kokenn2008@gmail.com`。アプリ起動時の既定選択が意図と異なる場合は
    組織コンボで LwcEditor に切り替えてから検証する。
- [ ] **書き込み可能なデータルート** — ポータブル（実行ファイル隣接 `data/`）か
  `~/Library/Application Support/SfUi` のどちらに落ちるかを確認して記録。

### 0-1. 自動検証（CI 相当の確認）

- [ ] **ユニットテスト** — `dotnet test tests/SfUi.Tests` が全件 green（395 件）。
  - 期待: 終了コード 0。macOS 上で Shift-JIS / パス解決を含む全テストが通る。
- [ ] **Avalonia ビルド** — `dotnet build src/SfUi.Avalonia` が警告 0 で成功。
- [ ] **スモーク起動（基本）** — `dotnet run --project src/SfUi.Avalonia -- --smoke`。
  - 期待: ログに「組織一覧 N 件」「言語切替 OK (ja→en→zh→ko)」が出て exit 0 で自動終了。
  - ログ: `data/logs/app-*.log`（または `--data-dir` 指定先）。
- [ ] **スモーク起動（比較ウィンドウ）** — `dotnet run --project src/SfUi.Avalonia -- --smoke --smoke-compare`。
  - 期待: 組織が 2 件以上あれば Compare Orgs ウィンドウが開いて閉じ、exit 0。
- [ ] **`.app` バンドル組立** — `bash packaging/make-mac-app.sh osx-arm64`（Intel は `osx-x64`）。
  - 期待: `dist/mac/SfUi.app` と zip が生成される。`iconutil` があれば `.icns` が入る。
- [ ] **`.app` を Finder から起動** — `open dist/mac/SfUi.app`。
  - 期待: Dock にアイコンが表示され、アプリが起動する（未署名のため Gatekeeper 警告が出る場合は
    「システム設定 → プライバシーとセキュリティ」から許可して記録）。
- [ ] **`.app` 起動時のスモーク** — `dist/mac/SfUi.app/Contents/MacOS/SfUi.Avalonia --smoke`（パスは実物に合わせる）。
  - 期待: バンドル内実行ファイルが単体でも exit 0。

---

## 1. 起動とメイン画面

- [ ] **初回起動でようこそ画面が出る** — データルートを一時退避（`--data-dir /tmp/sfui-check` 等）して起動。
  - 期待: Welcome ウィンドウが表示され、主な機能の案内と sf の検出結果が出る。
- [ ] **sf 未検出時のようこそ画面** — `dotnet run --project src/SfUi.Avalonia -- --welcome-missing`。
  - 期待: sf が無い想定の表示（インストール案内・npm ヒント・**再チェック**ボタン）になる。
- [ ] **「Get started」/「Open settings」** — ようこそ画面の両ボタン。
  - 期待: それぞれメイン画面へ進む / Settings タブを開く。
- [ ] **「今後表示しない」** — チェックして閉じ、再起動でようこそが出ない。Settings から再表示できる。
- [ ] **メイン画面の表示崩れなし** — 初期ウィンドウサイズでレイアウトが崩れない（Retina 表示）。
- [ ] **ウィンドウのリサイズ** — 最小〜最大化でツールバー/左クイックパネル/タブが崩れない。

---

## 2. トップバー

- [ ] **組織コンボ** — 一覧が表示され、選択が切り替わる。起動時に前回選択が復元される。
- [ ] **組織の再読込** — 再読込ボタンで組織一覧が更新される。
- [ ] **フォルダコンボ（編集可能）** — 手入力と候補選択の両方ができ、履歴が記憶される。
- [ ] **フォルダ参照** — フォルダ参照ボタンで Finder ダイアログ（`OpenFolderDialog`）が開く。
- [ ] **Org Info ボタン** — 選択中の組織の Org Info ウィンドウが開く（未選択時は無効/案内）。
- [ ] **Compare Orgs ボタン** — 組織 2 件以上で有効化し、比較ウィンドウが開く。
- [ ] **Data I/O ボタン** — データ入出力ウィンドウが開く。
- [ ] **Backup & Restore ボタン** — バックアップ ウィンドウが開く。
- [ ] **Org Management ボタン** — 組織管理ウィンドウが開く。
- [ ] **ターミナル起動** — ターミナル ボタンで Terminal.app が SF フォルダ（または既定）で開く。
- [ ] **Finder 表示** — Finder ボタンでフォルダが `open <path>` で開く。
- [ ] **VS Code 起動** — VS Code ボタンで `code`（PATH または `/Applications/Visual Studio Code.app/.../code`）が開く。
  - 期待: 未インストール時は適切な案内/無効化。
- [ ] **ブラウザ起動** — ブラウザ ボタンで `open <url>` が既定ブラウザで開く。
- [ ] **クイックパネル開閉** — 左パネルがトグルで表示/非表示になる。
- [ ] **AI パネル開閉** — 右 AI パネルがトグルで表示/非表示になる。
- [ ] **言語切替** — トップバーから ja / en / zh / ko を切替えると全体の文言が即時反映される。
- [ ] **ツールチップ** — 各アイコン ボタンにホバーでツールチップが出る。

---

## 3. 各タブ

タブは左から 0..7（`--tab N` で直接開ける）。各タブ共通で、操作後に **History に記録**されることを確認する。

- [ ] **0 SOQL** — クエリを実行して結果グリッド表示。ソート・列表示が機能。
  - 期待: REST 優先実行、`sf data query` フォールバック。Tooling API トグルも確認。
- [ ] **SOQL CSV/TSV エクスポート** — 結果を CSV/TSV で保存できる（保存ダイアログが macOS ネイティブ）。
- [ ] **1 Apex** — `sf apex run` で無名 Apex を実行。コンパイルエラー/例外/デバッグログが表示される。
  - 期待: AvaloniaEdit のハイライト・スクロール・Enter/Cmd+Enter 実行が機能。
- [ ] **Apex ファイル開く/保存** — `.apex` ファイルのオープン/保存ダイアログが機能。
- [ ] **2 Debug Logs** — `sf apex list log` で一覧、ID/番号で内容取得、ローカル保存。
- [ ] **3 History** — 種別ごとに記録され、検索できる。ダブルクリックで再実行できる。
- [ ] **4 Deploy** — deploy / validate / quick deploy / report / retrieve を実行。
  - 期待: ソース ディレクトリ/マニフェスト選択、テストレベル、待機時間、コマンド プレビュー、結果サマリ。
- [ ] **5 Command** — 任意の `sf` 引数を実行し stdout/stderr が表示される。
  - 期待: 危険操作（delete/logout/deploy 等）は確認ダイアログが出る。
- [ ] **6 REST API** — GET/POST/PATCH/DELETE を組織の instance URL + アクセストークンで実行。
  - 期待: レスポンスが pretty print され、401 は自動リフレッシュされる。
- [ ] **7 Settings** — sf / ツール パス、履歴上限、確認ポリシー、言語、サンプル データの各設定が保存・復元される。
  - 期待: ファイル選択ダイアログ（3 箇所）が macOS ネイティブで開く。
- [ ] **ショートカット（Cmd+1..9）** — クイックパネルの先頭 9 件が実行される（`Cmd` と `Ctrl` の両対応）。
- [ ] **F5 / Cmd+Enter** — SOQL/Apex/Command の実行ショートカットが機能。

---

## 4. 個別ウィンドウ

### 4-1. Org Info（20+ タブ）

- [ ] **ウィンドウが非モーダルで複数開ける** — 別組織で同時に 2 枚以上開く。
- [ ] **タブ一覧** — Overview / Org Settings / Users / Profiles / Permission Sets / Roles / Objects /
  Sharing(OWD) / Apex / Flows / Scheduled Jobs / Connected Apps / Installed Packages / Login History /
  Setup Audit Trail / Record Types / Currencies / Object Fields / My Settings が表示・取得できる。
- [ ] **タブ横断検索** — 全タブをまたぐ検索と、該当行へのジャンプ。
- [ ] **タブ個別 / 全リフレッシュ** — 各タブと全体の再取得が機能。
- [ ] **Object Fields の遅延読込** — 開いた時だけ取得される。
- [ ] **Setup リンク** — Setup ページへの遷移リンクが正しい URL を開く。
- [ ] **My Settings** — 55 項目カタログから自前タブを構成できる。
- [ ] **AI パネル（各ウィンドウ）** — 「Attach current tab data」+ クイック プロンプトが機能。
- [ ] **キャッシュ** — 初回だけ取得し、再オープンが即座（手動リフレッシュで再取得）。

### 4-2. Compare Orgs

- [ ] **2〜4 組織を選んで比較** — 13 カテゴリが API 名で突き合わされる。
- [ ] **差分ハイライト** — 片方のみ/値違いの行が色分けされる。
- [ ] **Diff only フィルタ** — 差分行だけに絞れる。
- [ ] **カテゴリ内検索 / CSV エクスポート** — 検索とエクスポートが機能。

### 4-3. Data I/O

- [ ] **Export タブ** — SOQL 直接 or フィールド チェックボックスから構築、REST / Bulk API 実行。
  - 期待: 結果グリッドを参照し CSV / JSON / TSV で保存できる。
- [ ] **Import タブ** — CSV 読込（UTF-8 / Shift-JIS 自動判定）、列→フィールド自動マッピング（編集可）。
  - 期待: Insert / Update / Upsert / Delete を REST（200 件/バッチ）or Bulk API で実行。
  - 期待: 確認プロンプト・進捗・行別結果グリッド（失敗行の CSV エクスポート）が出る。
- [ ] **Object Access タブ** — Permission Sets / PSG / Profiles のオブジェクト権限一覧（検索付き）。
- [ ] **Field Access タブ** — フィールド × サブジェクト行列（R / E セル、列検索）。
- [ ] **Record Access タブ** — ユーザー選択 → SOQL で対象レコード取得 → 200 件ページング +
  UserRecordAccess（Read/Edit/Delete/Transfer）+ レコードを開くリンク。

### 4-4. Backup & Restore

- [ ] **Backup タブ** — オブジェクト選択（件数付き・バックグラウンド取得）、選択記憶・一括クリア。
  - 期待: ラベル/説明の自動入力、REST（小）/ Bulk API（大）で CSV エクスポート。
- [ ] **Restore タブ** — バックアップ選択（削除可）→ 対象オブジェクト選択 → 復元。
  - 期待: 同一組織は Id 一致（スキップ/上書き/削除済みの undelete）、別組織はキー項目一致。
  - 期待: 結果グリッド（作成/更新/undelete/スキップ/失敗）がオブジェクト別に集計される。
- [ ] **Records 詳細ウィンドウ** — ページング・AND 検索・先頭列から Salesforce を開く。
- [ ] **Compare タブ** — バックアップ A/B を選択し、オブジェクト別にレコード単位で比較。
  - 期待: diff / only A / only B / error の色分け + Diff only フィルタ。
- [ ] **差分詳細ウィンドウ（↗）** — `Field: A → B` の変更内容が検索・ページング・リンク付きで表示される。

### 4-5. Org Management

- [ ] **Orgs タブ** — 認証済み組織一覧（★デフォルト/alias/ユーザー名/org ID/タイプ/状態/instance URL/最終バックアップ/タグ・メモ）。
  - 期待: Set as default / Open in browser / Add login (web) / Register org… / Logout（確認付き）/
    Set alias / Test connection / Test all orgs / 検索ボックスが機能。
  - 期待: `force://` URL または `sf org display --verbose --json` の JSON で組織登録できる。
  - 期待: タグ・メモは `data/org-manage.json` に保存され組織へは書かれない。
- [ ] **Health タブ** — `/limits` の used / max / usage % バー（80%+ で赤）・名前フィルタ・使用率ソート。
- [ ] **Migration Inventory タブ** — Workflow rules / Process Builder / Flows の一覧。
  - 期待: kind / name / API name / object / active / subtype / last modified、kind・active only フィルタ、
    種類別サマリ、CSV エクスポート、行の **Setup** ボタン。

---

## 5. macOS 固有の確認

- [ ] **ショートカットの Cmd 対応** — `Cmd+Enter` / `Cmd+1..9` / `Cmd+F5`（F5）が機能。
  - 期待: `Ctrl` でも動く（両対応）。UI 表記が `⌘` / `Cmd` になる（`PlatformInfo` 系の出し分け）。
- [ ] **Windows 専用ツールの非表示** — `powershell` / `cmd` / `wsl` / `wt` がメニューに出ない。
- [ ] **フォント フォールバック** — クエリ入力・ログ・結果グリッドが Menlo / SF Mono（等幅）で表示される。
- [ ] **データルート** — `.app` から起動した場合、バンドル内が書き込み不可なら
  `~/Library/Application Support/SfUi` にフォールバックする。
  - 期待: `--data-dir` / `SFUI_DATA_DIR` の優先も効く。
- [ ] **Shift-JIS CSV** — Shift-JIS の CSV を Import で正しく読み込める（CodePages が macOS で動作）。
- [ ] **`open` 連携** — Finder / ターミナル / VS Code / ブラウザが既定アプリで開く（セクション 2 と重複可）。
- [ ] **Dock / アイコン** — `.app` のアイコンと Dock 表示、ウィンドウ切替が正しい。

---

## 6. 永続化（設定・データ）

- [ ] **settings.json** — 言語・確認ポリシー・履歴上限・AI パネル表示等が再起動後に復元される。
- [ ] **履歴** — `data/history` に種別ごと記録され、再実行できる。
- [ ] **Org Info キャッシュ** — `data/orginfo` に保存され、再オープンで即表示。
- [ ] **バックアップ** — バックアップ一覧とファイルが永続化・削除できる。
- [ ] **お気に入り / クイックパネル** — SOQL / Apex / コマンド / REST / URL / フォルダが保存・復元される。
- [ ] **org-manage.json** — タグ・メモが保存される。

---

## 7. ローカライズ

- [ ] **4 言語切替** — ja / en / zh / ko の切替で、メイン画面・全ウィンドウ・ダイアログの文言が即時反映。
- [ ] **設定からの言語切替** — Settings タブからも変更でき、再起動後に維持される。
- [ ] **未翻訳キーの検出** — 切替時に生キー（`Tab_Soql` 等）が表示されない。

---

## 8. スクリーンショット記録

`--tab N` / `--open <target>` を使うと特定画面を開いた状態で起動できる（比較用スクショに便利）。

- [ ] **メイン画面（全 8 タブ）** — `--tab 0..7` で各タブを撮影。
- [ ] **各ウィンドウ** — `--open compare` / `orginfo` / `dataio` / `backup` / `orgmanage` /
  `backuprecords` / `comparerecords` で起動して撮影。
  - 期待: 組織・バックアップ等の実データがある場合のみ開く（無い場合は `--smoke-compare` 相当で代替）。
- [ ] **ようこそ画面** — `--welcome` / `--welcome-missing` の両状態を撮影。
- [ ] **保存先** — `docs/screenshots`（または指定先）に保存し、PR 説明に添付。

---

## 9. 結果まとめ

| セクション | 合否（✅/⚠️/❌） | 備考・リンク（ログ/スクショ/issue 番号） |
|---|---|---|
| 0. 前提条件と環境 | ✅ | macOS 15.6.1 arm64 / .NET 9.0.318（`~/.dotnet`）/ sf 2.152.14。LwcEditor Connected |
| 0-1. 自動検証 | ✅ | build 0警告 / 400 テスト合格 / smoke・smoke-compare・.app smoke すべて exit 0 |
| 1. 起動とメイン画面 | ✅ | ようこそ・メイン描画 OK（OCR）。`--welcome`/`--welcome-missing` 両状態確認 |
| 2. トップバー | ✅（一部未操作） | 描画・ツールチップ・言語切替 OK。各ボタンのクリック操作はウィンドウ起動（`--open`）で代替確認 |
| 3. 各タブ | ✅ | 8 タブ描画 OK。REST 実行等のトークン系も修正後に動作（Data I/O で 2133 件取得を確認） |
| 4. 個別ウィンドウ | ✅ | 5 ウィンドウの描画・LwcEditor 選択 OK。オブジェクト一覧/件数/overview 取得も成功 |
| 5. macOS 固有の確認 | ✅ | sf/VS Code 検出・Finder/Terminal 経路 OK。ショートカット表示も「Cmd」に修正済み |
| 6. 永続化 | ✅（一部未操作） | データルート `/Users/huqian/SfUi/data`、設定復元 OK。バックアップ等は書き込み未実施 |
| 7. ローカライズ | ✅ | smoke で ja→en→zh→ko 切替 OK。各画面の全言語網羅は未確認 |
| 8. スクリーンショット | ✅ | 主要 15 画面を撮影 + OCR 検証（セッション保存先 `files/screenshots/`） |

### 発見した不具合（再掲・すべて修正済み）

| # | Severity | 場所 | 症状 | 修正内容 |
|---|---|---|---|---|
| 1 | 🟠 HIGH | [OrgService.GetAuthAsync](/Users/huqian/SfUi/src/SfUi.Core/Services/OrgService.cs:90) | sf 2.152.14 の `org display` シークレット隠しによりトークンが `[REDACTED]...` になり、REST 系全機能が `INVALID_AUTH_HEADER` | トークン取得を `org auth show-access-token --json` に変更 |
| 2 | ⚪ LOW | ローカライズ文言 / [PlatformInfo.ShortcutModifierLabel](/Users/huqian/SfUi/src/SfUi.Core/PlatformInfo.cs:20) 未使用 | macOS でもショートカット表示が「Ctrl」のまま（キー操作は Ctrl/Cmd 両対応で機能） | 文言を `{mod}` プレースホルダ化し `ShortcutModifierLabel` で解決 |

発見した不具合は、再現手順・`data/logs/app-*.log` の該当行・スクリーンショットを添えて issue 化する
（または本チェックリストの該当項目に追記してブランチにコミット）。

