# REST API タブ「エンドポイント ツリー」計画

REST API タブの左側に、フォルダのように展開できるエンドポイント ツリーを置き、
**選ぶだけでメソッド・パス・引数が自動的に入る**ようにする機能の設計メモです。

- 状態: **P1 実装済み（2026-10-22）**
- 関連: `src/SfUi.Core/Rest/RestApiCatalog.cs`、`src/SfUi.Presentation/ViewModels/RestTreeViewModels.cs`、
  `src/SfUi.Presentation/ViewModels/ApiConsoleViewModel.cs`、`src/SfUi.App/Views/ApiConsoleView.xaml`、
  `src/SfUi.Avalonia/Views/ApiConsoleView.axaml`

## 決定事項（すべて推奨案を採用）

| 論点 | 決定 |
|---|---|
| ツリーの内容 | **静的カタログ + 動的 SObject 一覧の併用**（静的だけでは実用性が低く、動的だけでは定番 API に辿り着けない） |
| SObject の取得 | 既存の `SObjectDescribeService.ListObjectsAsync`（組織ごとにキャッシュ）を**遅延**呼び出し。ツリー構築は待たない |
| 入力の反映範囲 | **自動入力のみ**（メソッド・パス・本文テンプレート・引数）。自動送信はしない（誤爆防止） |
| 引数 UI | パス文字列の手編集ではなく、**エンドポイント専用の引数入力欄**を動的生成し、入力に応じてパスを再構築 |
| 履歴・お気に入り | ツリー内に「お気に入り」「履歴」ノードとして表示（既存のコンボは廃止） |
| 未入力の必須引数 | 送信前に検出してステータスに表示（`{id}` のままリクエストしない） |
| API バージョン | 組織の最新バージョン（`GetApiVersionAsync`）を `{version}` に差し込み |
| 絞り込み | フィルタ ボックスで全リクエスト + SObject 名を検索（一致グループは自動展開） |
| 言語 | 4 言語（`UiText` の `ApiTree_*` キー） |

## ツリー構成（P1）

```
お気に入り
履歴
組織 / データ (SObject)          ← 遅延: A (496) … Z (n) / # (n)
システム
  ├ GET  API バージョン          /services/data/
  ├ GET  組織の制限              /services/data/v{version}/limits
  ├ GET  テーマ                  /services/data/v{version}/theme
  ├ GET  最近使った項目          /services/data/v{version}/recent
  ├ GET  クエリ (q)              /services/data/v{version}/query?q={q}
  └ GET  検索 (q)                /services/data/v{version}/search?q={q}
Tooling
  ├ GET  クエリ (q)              /services/data/v{version}/tooling/query?q={q}
  └ ApexClass / ApexTrigger / ApexPage / Flow → GET /sobjects/{type}/{id}
Composite
  ├ POST sobjects                /composite/sobjects
  ├ POST batch                   /composite/batch
  └ POST graph                   /composite/graph
アクション
  ├ 標準アクション (standard)     /sobjects/{object}/quickActions
  └ カスタムアクション (custom)   /sobjects/{object}/quickActions
UI API
  ├ GET レコード (id)            /sobjects/{object}/{id}
  └ GET レイアウト (objectName)  /sobjects/{object}/describe/layouts/{objectName}
Bulk API 2.0
  ├ POST 取り込みジョブ作成      /jobs/ingest
  ├ GET  取り込みジョブ一覧      /jobs/ingest
  ├ GET  取り込みジョブ (id)     /jobs/ingest/{id}
  └ GET  クエリ ジョブ一覧       /jobs/query
```

各 SObject ノードの子（8 件）:

```
GET    n 件取得      /services/data/v{version}/sobjects/{object}
GET    1 件取得      /services/data/v{version}/sobjects/{object}/{id}
POST   作成          /services/data/v{version}/sobjects/{object}
PATCH  更新          /services/data/v{version}/sobjects/{object}/{id}
DELETE 削除          /services/data/v{version}/sobjects/{object}/{id}
GET    describe      /services/data/v{version}/sobjects/{object}/describe
GET    レイアウト    /services/data/v{version}/sobjects/{object}/describe/layouts
GET    リストビュー  /services/data/v{version}/sobjects/{object}/listviews
```

## 実装の要点

- `RestApiNode`（`record`）で 1 ノードを表現。`Children` か `ChildFactory`（遅延）のどちらかを持つ。
  遅延ノードにはプレースホルダ行を 1 つ入れて「展開できる」ことを UI に示す。
- `RestApiCatalog.BuildPath(node, version, values)` は `{version}` → `{object}` → 各引数（`Uri.EscapeDataString`）
  の順に置換。未入力の引数は `{id}` のまま残す。
- `RestParameterViewModel` は引数 1 個に対応。`Value` 変更で `RebuildPath()` を呼び、パスを即時更新。
- ツリー再構築時は**展開状態と選択を退避 → 復元**（`RestoreExpansion` は 3 パスまで試行）。
  組織データを後から読み込んでもユーザーの操作状態が消えない。
- WPF の `TreeView.SelectedItem` は読み取り専用のため、`SelectedItemChanged` イベントから
  ViewModel へ渡す。WPF の UIA 名は `RestTreeNodeViewModel.ToString()`（`"GET 1 件取得"`）を使う。
- メソッド バッジは DataTrigger で色分け（GET = 緑 / POST = 青 / PATCH = 橙 / DELETE = 赤）。

## リクエスト バー（Workbench の REST Explorer 風・2026-10-24）

Workbench の REST Explorer と同じ「入力 → 実行 → 応答」の流れになるよう、上部のリクエスト バーを再構成しました。

```
[☰] [GET][POST][PATCH][DELETE]   [お気に入りに追加] [⟳]
[ /services/data/v68.0/sobjects/Account/{id} ................................ ][ 実行 ]
https://xxx.my.salesforce.com/services/data/v68.0/sobjects/Account/{id}

（選択エンドポイントの引数） (パスを入力すると即時に書き換わる)
（リクエスト本文 — POST / PATCH のときだけ表示）
応答                                            [ ] 生の応答を表示
┌────────────────────────────────────────────────────────────────┐
```

- **メソッドはボタン**（`SetMethodCommand` + `IsGetMethod` 等）。選択中のものだけ色付き。
- **パス欄 + 実行ボタン**を 1 行にし、その下に**絶対 URL**（インスタンス URL + パス）を常時表示。
  インスタンス URL は組織データ取得時に `GetInstanceUrlAsync` で 1 回取得してキャッシュ。
- **本文欄は GET / DELETE では非表示**（`IsBodyVisible`）。
- **「生の応答を表示」**（`IsRawResponse`）で整形済み JSON ↔ 生の応答を切替
  （`RawResponseText` を保持し、`DisplayResponse` が選択に応じて返す）。
- 初期表示のパスは組織の API バージョンに追従（ユーザーが書き換えていれば触らない）。
- **WPF の罠**: `TextBox.Text` は既定 TwoWay なので、読み取り専用プロパティ（`DisplayResponse`）に
  束縛するときは `Mode=OneWay` を明示しないと起動時に InvalidOperationException でウィンドウが壊れる。
- **Avalonia のメソッド ボタン**: `Classes.selected="{Binding IsGetMethod}"` のような
  クラス バインディング + `Style Selector="Button.method.selected"` で色分け（DataTrigger は無い）。

## 今後の候補（P2 / P3）

- **P2**: 送信結果からの「このリクエストをツリーに保存」、履歴のメソッド別グルーピング、
  ツリーのキーボード操作（← → で開閉、Enter で送信）。
- **P3**: OpenAPI 風の検索（`/sobjects/Account/describe` を入力するとツリーが該当ノードまで自動展開）、
  describe の結果から項目名を補完する本文ビルダー、ツリー定義の外部ファイル化（ユーザー追加 API）。

## 検証

- 単体テスト: `tests/SfUi.Tests/RestApiCatalogTests.cs`（9 件。静的グループ / メソッドとパス / バージョン差し込み /
  パス組み立てとエスケープ / 必須引数 / 本文 / SObject 8 リクエスト / 先頭文字グルーピング）。
- UI 自動操作 E2E（WPF）: ツリー表示 → SObject → A → Account → 「GET 1 件取得」→ パス自動入力 →
  引数 `id` 入力でパス即時更新 → 未入力での送信ガード → 絞り込み（`limits`）→ 絶対 URL →
  メソッド ボタン（POST で本文表示 / GET で非表示）→ 実行（HTTP 200）→ 「生の応答を表示」で本文が変わる →
  ツリー非表示（全 18 項目）。
- スモーク: WPF / Avalonia ともに終了コード 0。
