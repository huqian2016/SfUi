# スクラッチ組織管理 設計メモ

最終更新: 2026-10-23 / 実装状況: **P1・P2 実装済み**

## 決定事項（ユーザー確認済み）

| 論点 | 決定 |
|---|---|
| 置き場所 | **独立ウィンドウ**（上部バーの「スクラッチ組織」）。組織管理の 4 番目のタブではなく、作成（長時間・多段階）と資源編集に十分な面積を確保する。既存の Data I/O / 組織比較 / 組織管理と同じ「別ウィンドウ」パターンに揃える |
| リポジトリ | **アプリ管理下** `data/scratch/profiles/<name>/`。Git 連携は行わない。**ZIP エクスポート / インポート**で共有する |
| シークレット | リポジトリに保存しない。認証情報は組織の Setup / 外部資格情報で設定する（プリセットはスケルトンのみ生成） |
| Experience Cloud / Named Credential | 「取得・デプロイ」と「プリセット生成」の**両方**に対応済み（P3） |
| 進め方 | P1 実装 → 動作確認 → **P2 / P3 承認済み・実装済み** → 合意後に P4 以降 |

## プロファイル構成

```
data/scratch/
  history.json                 … 実行履歴（最大 100 件）
  profiles/<name>/
    profile.json               … 選択・パラメータ（DevHub, 期限, 別名規則, shape, 資源フラグ, 権限セット）
    definition.json            … スクラッチ定義（edition / features / settings）
    sfdx-project.json          … deploy / retrieve 用の SFDX プロジェクト
    force-app/main/default/    … メタデータのソース（取り込み先）
    manifest/package.xml       … メタデータのマニフェスト
    manifest/custom-metadata.xml … カスタム メタデータ専用マニフェスト
    manifest/static-resources.xml … Static Resource
    manifest/connected-apps.xml … 接続アプリ
    manifest/reports.xml       … レポート / ダッシュボード
    data/plan.json             … シードデータ（sf data import tree --plan）
    scripts/apex/*.apex        … 作成後に実行する匿名 Apex
```

- `profile.json` 以外は**ユーザーが直接編集**できる。アプリは既存ファイルを上書きしない。
- 既定プロファイル `default` は初回表示時に自動作成する。
- **force-app/main/default にソースが無い場合、deploy 系のステップはスキップ**する（`sf project deploy start` はソースが無いと必ず失敗するため）。取り込みタブで取得するか、ファイルを置く。

## 実行ステップ（P1/P2/P3）

1. `sf org create scratch --target-dev-hub <hub> --definition-file <def> --alias <alias> --duration-days N --wait M [--set-default] [--source-org <shapeId> | --snapshot <name>] --json`
2. `sf project deploy start --manifest manifest/package.xml --target-org <alias> --wait 30 --ignore-conflicts --json`（チェック ON・ファイルあり・ソースあり）
3. `sf project deploy start --manifest manifest/custom-metadata.xml ...`（同上）
4. `sf project deploy start --manifest manifest/experience.xml ...`（同上）
5. `sf project deploy start --manifest manifest/named-credentials.xml ...`（同上）
6. `sf project deploy start --manifest manifest/static-resources.xml ...`（同上）
7. `sf project deploy start --manifest manifest/connected-apps.xml ...`（同上）
8. `sf project deploy start --manifest manifest/reports.xml ...`（同上）
9. `sf apex run --file scripts/apex/<name>.apex --target-org <alias> --json`（ファイルごと・名前順）
10. `sf org assign permset --name <ps> --target-org <alias> --json`（権限セットごと）
11. `sf data import tree --plan data/plan.json --target-org <alias> --json`（`objects` が 1 件以上ある場合のみ）
12. `sf org display --target-org <alias> --json`（確認）

- deploy 系（2〜8）は**プロファイル フォルダーを cwd** にして実行する（sfdx-project.json の `force-app` を参照）。
- チェック OFF は「そのステップを作らない」だけ。**ファイルは削除しない**。
- 実行前に結合後のコマンド一覧をプレビュー表示。実行はステップ単位で進捗・キャンセル・失敗時のエラー抜粋付き。
- 実行結果は `history.json` に保存し、履歴タブで詳細（コマンド・終了コード・出力）を確認できる。

### 別 Org からの取り込み（P2）

- 取り込みタブで取得元組織と資源グループを選び、`sf project retrieve start --manifest <m> --output-dir <temp> --ignore-conflicts --json` をグループごとに実行する（cwd = プロファイル フォルダー）。
- 取得結果と `force-app/main/default` を比較し、ファイルごとに **追加 / 変更 / 同一** を表示する。
- 「プロファイルへ反映」で追加・変更だけをコピーする（上書き前の内容は `.bak` に退避）。削除は行わない。

### 複数プロファイルの連続作成（P2）

- プロファイル一覧のチェックボックスで複数選択し、「選択分を連続作成」で順に実行する。
- エイリアスが重複する設定では実行前に警告して中止する（`{profile}` / `{time}` の利用を促す）。
- 途中で失敗した場合はそこで停止し、成功分は履歴に残る。

### Org Shape / スナップショット（P3）

- Shape タブ: `sf org list shape --json` で一覧、`sf org create shape --target-org <src> --json`（既存 org から shape を作る）、`sf org delete shape --target-org <src> --no-prompt --json`（その参照元の shape を全削除）。
- スナップショット: `sf org list snapshot --target-dev-hub <hub> --json` / `sf org create snapshot --target-dev-hub <hub> --source-org <src> --name <n> [--description <desc>] --json` / `sf org delete snapshot --target-dev-hub <hub> --snapshot <name> --no-prompt --json`。
- 作成タブでスナップショットを選ぶと「プロファイルで使用」で `--snapshot` に反映される（shape ID とは**排他**。検証でエラーになる）。
- Dev Hub が未選択の場合はスナップショット操作を実行せず、メッセージで促す。

### Experience Cloud グループ（P3）

- `manifest/experience.xml`（Network / Site / ExperienceBundle / Audience / NavigationMenu / BrandingSet / CustomSite / ManagedContent など）を deploy する。
- 「Experience 設定を追加」で `definition.json` に `Communities` / `SiteDotCom` 機能と `communitiesSettings.enableNetworks=true` を追記する（既存値は保持、書換済なら「既にある」と表示）。

### Named Credential プリセット（P3）

- `manifest/named-credentials.xml`（NamedCredential / ExternalCredential / ExternalClientApplication / AuthProvider）を deploy する。
- SAP / ServiceNow / MuleSoft / Generic のスケルトンを `force-app/main/default/namedCredentials/<name>.namedCredential-meta.xml` に生成する（レガシー `NamedCredential` + `NoAuthentication` + `Anonymous`）。
- **シークレットはリポジトリに書かない**: 認証情報は組織の Setup / 外部資格情報で設定する前提。

## UI

- **作成タブ**: Dev Hub（認証済み組織から選択）、プロファイル、有効日数（1–30）、待機（1–120 分）、エイリアス パターン（`{profile}` / `{date}` / `{time}` / `{datetime}`）、Org Shape ID、スナップショット名、作成後に既定組織に設定、資源チェック（メタデータ / カスタム メタデータ / Static Resource / 接続アプリ / レポート / Apex スクリプト / Experience Cloud / Named Credential / 権限セット / シードデータ）、Experience 設定の追加、Named Credential プリセット、権限セット一覧、プロファイル フォルダー、コマンド プレビュー、警告、実行ログ。
- **プロファイルタブ**: 一覧（連続作成用チェックボックス付き）、新規 / 保存 / 削除 / ZIP エクスポート / ZIP インポート / フォルダーを開く、連続作成。
- **取り込みタブ**: 取得元組織、資源グループ、取得して比較、差分一覧、プロファイルへ反映、取得ログ。
- **Shape / スナップショットタブ**: Shape の参照元組織・一覧・作成・削除・プロファイルで使用、スナップショットの参照元 / 名前 / 説明・一覧・作成・削除・プロファイルで使用。
- **履歴タブ**: 実行一覧（開始・プロファイル・エイリアス・Dev Hub・結果・所要時間）、選択した実行の詳細、履歴の消去。

## 今後の予定

| フェーズ | 内容 |
|---|---|
| P2（完了） | 資源グループの追加（Apex スクリプト、Static Resource、接続アプリ、レポート / ダッシュボード）、別 Org からの retrieve と差分プレビュー、依存警告の強化、複数プロファイルの連続作成 |
| P3（完了） | Org Shape の一覧 / 作成 / 削除、スナップショット（`sf org create snapshot` / `sf org create scratch --snapshot`）、Experience Cloud グループ（manifest + definition.json のパッチ）、Named Credential グループ（スケルトン生成。シークレットはコードに含めない） |
| P4 | 期限切れ監視と一括削除、作成後検証レポート、データ マスキング、プロファイル テンプレート、Dev Hub クォータ確認、複数作成の詳細制御 |

## 制約メモ

- Org Shape は**設定・機能・ライセンスのみ**を写し、メタデータは含まない（メタデータは手順 2 以降で入れる）。
- スクラッチ組織の期限は**延長できない**。必要ならスナップショット + 再作成。
- `--source-org` は 15 桁の shape ID。指定時は定義ファイルの edition / features が上書きされる。
- `sf data import tree` は `__mdt` レコードには使えないため、カスタム メタデータはメタデータ デプロイ（`customMetadata`）で入れる。
