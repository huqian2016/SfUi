# SfUi ソース エディタ機能 設計書

最終更新: 2026-10-10 / ステータス: **Phase 2 完了（編集 + 未反映ハイライト + 組織反映）** — Phase 1（閲覧）+ 編集・baseline/working ドラフト自動保存・未反映行の背景色（緑=追加/黄=変更）・組織へ反映（Ctrl+S）・検証のみ（dry-run）・削除・反映エラーの行ジャンプ、を両アプリで実装済み。`--smoke-source` は反映の往復（作成→検証→構文エラー→更新→削除）まで検証
対象: WPF (`SfUi.App`) + Avalonia (`SfUi.Avalonia`) の両方

---

## 1. 目的・背景

SfUi に「Salesforce ソース エディタ」ウィンドウを新規追加する。組織のメタデータ ソースを VS Code ライクなエディタで閲覧・編集し、組織へ簡単に反映できるようにする。

### ユーザー要望（確定 5 項目）

1. **Apex クラス / トリガー / LWC / Visualforce ページのソースを表示**し、簡易的に**新規作成・編集**できる。**組織にシンプルに反映**できる。エディタは VS Code のような表示（**色付き**=シンタックス ハイライト、**未反映箇所のハイライト**、**行番号**）
2. **Flow はグラフで表示**する
3. 可能であれば**編集時の自動補完**機能
4. **AI 機能**
5. ソース エディタとしてあると良い機能は追加検討

### 本設計の進め方

先に本設計書でスパイク結果（実組織検証）とフェーズ計画を確定し、**段階的に実装 → 動作確認**（単体テスト + スモーク + 両アプリ UI E2E + 実組織）を繰り返す。

## 2. 技術スパイク結果（2026-10-10 実測・hks4sand1）

| 検証項目 | 結果 | 判定 |
|---|---|---|
| Apex クラス一覧（Tooling SOQL、NamespacePrefix=null） | 185 件取得 | ✅ 一覧は Tooling REST |
| Apex トリガー一覧 | 15 件取得 | ✅ |
| Visualforce ページ一覧 | 24 件取得 | ✅ |
| LWC 一覧（`LightningComponentBundle` Tooling クエリ） | 取得 OK（DeveloperName） | ✅ |
| ソース本文の取得 | ApexClass GET（Body）/ ApexPage GET（Markup）成功。LWC は `LightningComponentResource` クエリで **FilePath / Format / Source を js・html・css・**js-meta.xml の 3+1 ファイル全部取得可能** | ✅ **読み取りは Tooling REST で統一** |
| ApexClass の Tooling **PATCH**（本文更新） | `INSUFFICIENT_ACCESS_ON_CROSS_REFERENCE_ENTITY`（v58 / v59 / v62 すべて。POST 作成 201・GET・DELETE 204 は可。ユーザーは Author Apex あり = deploy 実績で確認） | ❌ 使用不可 |
| ApexPage の Tooling PATCH（Markup 更新） | **204 成功 → GET で反映確認済み** | ✅（ただし Apex とバラバラは避け統一） |
| `sf project deploy start --source-dir <file>`（Apex / VF / LWC） | すべて Succeeded（**`--ignore-conflicts` 必須** — なしだと source tracking 競合で status 1） | ✅ **書き込みは sf CLI deploy に統一** |
| `sf project deploy start --dry-run` | Succeeded（検証のみ） | ✅ |
| 削除 | Apex / VF = Tooling DELETE（204）・LWC = `sf project delete source --no-prompt` | ✅ |
| retrieve パッケージング | `sf project retrieve start --metadata <type>:<name>` 動作（出力先スキャンに使える） | ✅（予備経路） |

**結論: 読み取り = Tooling REST（高速・本文直接）、書き込み = `sf project deploy`（全 4 種統一・確実）。** 反映 1 回は CLI 起動込みで約 5〜10 秒。

## 3. アーキテクチャ

```
src/SfUi.Core/Services/
  SourceEditorModels.cs      … SourceMemberKind / SourceMemberInfo / SourceFileInfo / 反映結果
  SourceEditorService.cs     … 一覧・取得（Tooling REST）/ 反映・検証・削除（sf CLI + 一時プロジェクト）
src/SfUi.Presentation/ViewModels/
  SourceEditorViewModel.cs   … メタデータ ブラウザ + タブ + エディタ状態 + 反映
  SourceFileViewModel.cs     … 1 ファイル（本文・言語・未反映行・エラー行）
src/SfUi.App/Views/SourceEditorWindow.xaml        … 両アプリ共通の画面構成
src/SfUi.Avalonia/Views/SourceEditorWindow.axaml
```

- エディタ コントロール: **WPF = AvalonEdit / Avalonia = AvaloniaEdit**（既存 SOQL / Apex タブと同一。シンタックス ハイライト定義は既存の `HighlightingManager` を再利用: Apex=.cls/.trigger は C# 定義（既存 Apex タブと同じ）、LWC=.js→JavaScript / .html→HTML / .css→CSS / .js-meta.xml→XML、VF=.page→HTML 系）
- 書き込み用ワークスペース: `data/source-editor/work/`（sfdx-project.json + force-app。反映前に該当ファイルを書き出して deploy）
- ローカル状態: `data/source-editor/<orgKey>/<kind>/<name>/`
  - `baseline/` … 最後に組織と一致していた内容（取得時 / 反映成功時に更新）
  - `working/` … 編集中の内容（自動保存。ウィンドウやアプリを閉じても復元）
- 未反映ハイライト = **working と baseline の行単位 diff**（変更行 = 黄、追加行 = 緑、削除 = ガター ▲、反映成功で baseline を更新して消える）

## 4. UI 設計（両アプリ共通）

```
┌ Source Editor ─────────────────────────────────────────────────┐
│ [組織: hks4sand1] [更新]  [ﾌｨﾙﾀ: ____]  [新規▾][反映][検証][削除] [AI] │
│┌─ エクスプローラー ─┐┌─ AccountService.cls ×│helper.js ×──────────┐│
││▾ Apex クラス (185)││  1  public with sharing class ...          ││
││   ACM_Consts      ││  2      ...            ← 行番号 + 色付き     ││
││   ACM_ILogger     ││▍ 3  // 未反映行はガターに色マーカー          ││
││▸ Apex トリガー (15)││                                             ││
││▸ VF ページ (24)   ││                                             ││
││▸ LWC (n)          ││                                             ││
││▸ Flow (n) ※P4     ││                                             ││
│└───────────────────┘└─────────────────────────────────────────────┘│
│ 172 行 / 未反映 3 行 · 組織と一致(最終取得 10:32) · Ln 12, Col 8     │
└──────────────────────────────────────────────────────────────────┘
```

- 左: メタデータ エクスプローラー（種類 → メンバー。フィルタは部分一致、グループは件数表示）
- 右: 開いているファイルのタブ（LWC は js / html / css / meta をまとめてタブ展開）
- ツールバー: 更新 / フィルタ / 新規（テンプレート: クラス・トリガー・VF・LWC）/ **反映** / **検証のみ（dry-run）** / 削除 / AI パネル
- ショートカット: **Ctrl+S = 組織へ反映**（VS Code の保存に相当する一発操作。確認なし・結果はステータスに表示）、修正が無い場合は何もしない
- 反映エラー: ステータスにサマリ + エラー一覧パネル（**行・列クリックでジャンプ**、該当行に赤マーカー）
- ステータス バー: 行数 / 未反映行数 / 組織一致状態 / 最終取得・反映時刻 / Ln・Col

## 5. フェーズ計画

| Phase | 内容 | 受け入れ基準 | 検証 |
|---|---|---|---|
| **P1 閲覧** | ウィンドウ + 4 種の列挙（Tooling）/ 選択で本文取得 / タブ表示 / 行番号 + 色付き / ステータス バー / 両アプリ + ツールバー導線 + UiText 4 言語 | 実組織でクラス・トリガー・VF・LWC が表示できる → **完了**（186→184 クラス等の実測、LWC 479 件） | 単体テスト（25）+ `--smoke-source` + UI E2E（両アプリ）+ スクショ |
| **P2 編集 + 反映** | 編集可 / working・baseline 保存（自動ドラフト）/ **未反映行ハイライト** / 反映（Ctrl+S・ボタン、sf deploy）/ 検証のみ / 削除 / エラー行ジャンプ | クラスを編集 → 未反映表示 → 反映 → 組織に反映され表示が「一致」に / 構文エラーは行付きで表示 → **完了**（E2E: 実組織で編集→反映→組織検証→戻し） | 単体テスト（+28）+ スモーク（往復）+ UI E2E |
| **P3 新規作成** | テンプレート（クラス / トリガー / VF / LWC）+ API 名検証 + deploy で新規作成 | テンプレートから新規クラスを作成し組織に作成できる（スモークで往復 + 後始末） | 単体テスト + スモーク + UI E2E |
| **P4 Flow グラフ** | Flow 一覧（FlowDefinitionView）+ Metadata(XML) 解析 + **ノード/エッジのグラフ表示**（読み取り専用。start / decision / record 操作 / action / loop / screen 等を色分け）+ ノード詳細 + ズーム/スクロール | 実組織のフローが要素数どおりのグラフで表示される | パーサー単体テスト + 実組織スモーク + UI E2E + スクショ |
| **P5 自動補完** | Apex: 既存 `ApexCompletion`（スニペット / System / クラス / sObject / 項目 / SOQL）を再利用。LWC JS: キーワード + `lwc` API 基本。VF/HTML: タグ基本 | 入力中に候補が表示され、選択で挿入される | コンテキスト解析の単体テスト + 手動/UI 確認 |
| **P6 AI 支援** | パネル: 現在のソースについて**質問 / 説明 / 改善提案 / エラー修正**（AiChatClient 再利用。コンテキスト = 開いているファイル + 反映エラー）。提案コードのエディタ挿入 | AI 設定済み環境で応答が表示され、提案を挿入できる | 手動 + `--smoke-ai` 流用 |
| **P7 追加機能** | **ローカル履歴**（反映ごとにスナップショット → 一覧 / 差分 / ワンクリック巻き戻し=再反映）、横断検索（キャッシュ + 取得済み本文）、ファイル エクスポート、タブ操作改善 ほか | 反映 → 履歴から 1 つ前の版に戻せる | 単体テスト + UI E2E |

各 Phase 完了時に: ビルド → 全テスト → オフライン/実組織スモーク → UI E2E（WPF + Avalonia）→ 設計書/メモ更新 → コミット → CI グリーン、の順で検証する。

## 6. 実装メモ（スパイクで確定した定石）

- **メタデータ（-meta.xml）の扱い**: source 形式の deploy は **本体 + -meta.xml が必須**（無いと `Component conversion failed: File not found: …-meta.xml`）。
  - 既存メンバーの初回反映時は `sf project retrieve start --metadata <Type>:<Name> --ignore-conflicts` で取得した**本物のメタデータ**を使用（apiVersion / status / label 等の属性を保持）。ワーク プロジェクトにキャッシュされ、以降の反映はメタ取得なしで速い。
  - **新規メンバー（未作成）は retrieve できないため `BuildDefaultMeta` で生成**（apiVersion = 組織バージョン、status = Active。VF は label = 名前）。
- **Apex のコンパイル エラーは行・列付き**: deploy 結果 JSON の `result.details.componentFailures[]` に `fileName` / `lineNumber` / `columnNumber` / `problem` が入る（実測: `int a = ;` → 3 件、行 3 列 17）。エラー パネル → クリックで該当行へジャンプ。
- `--dry-run` = 検証のみ（アップロードなし、checkOnly true）。
- **削除**: Apex / トリガー / VF = Tooling DELETE（204）・LWC = `sf project delete source --no-prompt`。
- ドラフトは `data/source-editor/<orgKey>/<kind>/<name>/` の baseline / working に保存（編集は 800ms デバウンスで自動保存。開くときに baseline が組織内容と一致すれば working を復元、不一致なら破棄）。
- **sf CLI の偽失敗への耐性**: sf CLI（2.94.6 で確認）はまれに、デプロイが**組織に反映された後**に `Metadata API request failed: Missing message metadata.transfer:Finalizing for locale en_US.` を返して失敗扱いになる（ポーリング中のメッセージ解決の問題。検証のみは影響を受けにくい）。アプリはコンパイル エラーなしの CLI レベル失敗を検出すると、組織から本文を再取得して意図した内容と一致するか確認し、一致すれば成功として扱う（`ShouldVerifyAgainstOrg` / `ContentMatches`。retrieve / LWC 削除にも同様の確認あり）。

### スパイクの生メモ（2026-10-10）

- 一覧 SOQL（Tooling）:
  - `SELECT Id, Name, ApiVersion, LastModifiedDate, LengthWithoutComments FROM ApexClass WHERE NamespacePrefix = null ORDER BY Name`
  - `SELECT Id, Name, ApiVersion, LastModifiedDate FROM ApexTrigger ORDER BY Name`
  - `SELECT Id, Name, LastModifiedDate FROM ApexPage WHERE NamespacePrefix = null ORDER BY Name`
  - `SELECT Id, DeveloperName FROM LightningComponentBundle ORDER BY DeveloperName`
- 本文: `GET /services/data/v<v>/tooling/sobjects/ApexClass/{id}`（Body）・`ApexTrigger/{id}`（Body）・`ApexPage/{id}`（Markup）※ `?fields=` パラメーターは使わない（無指定 GET が確実）
- LWC: `SELECT Id, FilePath, Format, Source FROM LightningComponentResource WHERE LightningComponentBundle.DeveloperName = '<name>'`
- 反映: 一時 sfdx プロジェクト（`data/source-editor/work/`）へ working ファイルを書き出し
  `sf project deploy start --source-dir <path> --ignore-conflicts --target-org <org> --json`
  - **`--ignore-conflicts` 必須**（付けないと source tracking 競合で失敗することがある）
  - 結果: `result.details.componentFailures[].problem`（Apex は行・列を含む）→ エラー ジャンプに使用
- 削除: Apex / VF = Tooling DELETE、LWC = `sf project delete source --no-prompt --metadata LightningComponentBundle:<name>`
- PowerShell スパイクの教訓: `[IO.File]` の相対パスは**プロセス CWD** 基準（PS の Set-Location とは別）→ 絶対パスを使う

## 7. リスクと限界（明示）

| リスク | 対応 |
|---|---|
| 反映は CLI deploy のため 1 回 5〜10 秒 | ステータス表示 + 控えめな完了通知。連打ガード（実行中は無効化） |
| ApexClass の Tooling PATCH が組織権限で拒否される（本組織で実測） | 書き込みは deploy に統一（権限差の影響を受けない） |
| マネージド パッケージのメタデータ | 一覧から除外（NamespacePrefix=null / 対象 4 種のみ） |
| 大きなファイル（数万行の生成クラス等） | エディタは遅延なしで表示できる範囲を想定。diff は行単位で軽量実装。極端に大きい場合はハイライト省略の検討（P2） |
| Flow の Metadata XML は複雑（要素 100 種超） | 主要要素のみグラフ化（start / decision / recordX / actionCall / loop / screen / subflow / assignment / wait / connector）。未知要素はノード表示のみ |
| LWC の meta.xml 編集 | タブとして表示・編集可。ただし属性の意味は関知しない（簡易） |
| 反映中の同時編集 | 反映中は編集ロック（IsBusy）。反映成功後に baseline 更新 |

## 8. 未決事項・将来検討

- 自動補完の対象範囲（Apex は既存資産で高品質化、LWC は標準 API のみ等）
- AI 提案のエディタへの適用方式（挿入 / 置換 / 差分プレビュー）
- Flow グラフのレイアウト精度（自動整列 → 手動ドラッグは将来）
- ローカル履歴の保持数（既定 20 版など）
- 組織間のソース比較（別組織との diff）— 既存 Compare 機能との連携
