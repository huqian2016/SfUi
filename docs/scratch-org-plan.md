# スクラッチ組織管理 設計メモ

最終更新: 2026-10-23 / 実装状況: **P1 実装済み**

## 決定事項（ユーザー確認済み）

| 論点 | 決定 |
|---|---|
| 置き場所 | **独立ウィンドウ**（上部バーの「スクラッチ組織」）。組織管理の 4 番目のタブではなく、作成（長時間・多段階）と資源編集に十分な面積を確保する。既存の Data I/O / 組織比較 / 組織管理と同じ「別ウィンドウ」パターンに揃える |
| リポジトリ | **アプリ管理下** `data/scratch/profiles/<name>/`。Git 連携は行わない。**ZIP エクスポート / インポート**で共有する |
| シークレット | リポジトリに保存しない。必要になった時点で DPAPI（`CredentialProtector`）によるローカル保存を使う（P3） |
| Experience Cloud / Named Credential | 「取得・デプロイ」と「プリセット生成」の**両方**を選べるようにする（P3） |
| 進め方 | **P1 のみ**実装して動作確認 → 合意後に P2 以降 |

## プロファイル構成

```
data/scratch/
  history.json                 … 実行履歴（最大 100 件）
  profiles/<name>/
    profile.json               … 選択・パラメータ（DevHub, 期限, 別名規則, shape, 資源フラグ, 権限セット）
    definition.json            … スクラッチ定義（edition / features / settings）
    manifest/package.xml       … メタデータのマニフェスト
    manifest/custom-metadata.xml … カスタム メタデータ専用マニフェスト
    data/plan.json             … シードデータ（sf data import tree --plan）
```

- `profile.json` 以外は**ユーザーが直接編集**できる。アプリは既存ファイルを上書きしない。
- 既定プロファイル `default` は初回表示時に自動作成する。

## 実行ステップ（P1）

1. `sf org create scratch --target-dev-hub <hub> --definition-file <def> --alias <alias> --duration-days N --wait M [--set-default] [--source-org <shapeId>] --json`
2. `sf project deploy start --manifest manifest/package.xml --target-org <alias> --wait 30 --ignore-conflicts --json`（チェック ON かつファイルあり）
3. `sf project deploy start --manifest manifest/custom-metadata.xml ...`（同上）
4. `sf org assign permset --name <ps> --target-org <alias> --json`（権限セットごと）
5. `sf data import tree --plan data/plan.json --target-org <alias> --json`（`objects` が 1 件以上ある場合のみ）
6. `sf org display --target-org <alias> --json`（確認）

- チェック OFF は「そのステップを作らない」だけ。**ファイルは削除しない**。
- 実行前に結合後のコマンド一覧をプレビュー表示。実行はステップ単位で進捗・キャンセル・失敗時のエラー抜粋付き。
- 実行結果は `history.json` に保存し、履歴タブで詳細（コマンド・終了コード・出力）を確認できる。

## UI

- **作成タブ**: Dev Hub（認証済み組織から選択）、プロファイル、有効日数（1–30）、待機（1–120 分）、エイリアス パターン（`{profile}` / `{date}` / `{time}` / `{datetime}`）、Org Shape ID、作成後に既定組織に設定、資源チェック、権限セット一覧、プロファイル フォルダー、コマンド プレビュー、実行ログ。
- **プロファイルタブ**: 一覧、新規 / 保存 / 削除 / ZIP エクスポート / ZIP インポート / フォルダーを開く。
- **履歴タブ**: 実行一覧（開始・プロファイル・エイリアス・Dev Hub・結果・所要時間）、選択した実行の詳細、履歴の消去。

## 今後の予定

| フェーズ | 内容 |
|---|---|
| P2 | 資源グループの追加（Apex スクリプト、Static Resource、Connected App、レポート等）、別 Org からの retrieve と差分プレビュー、依存警告の強化、複数プロファイルの連続作成 |
| P3 | Org Shape の一覧 / 作成 / 削除、スナップショット（`sf org create snapshot` / `sf org create scratch --snapshot`）、Experience Cloud グループ（Network / Site / ExperienceBundle / Audience / NavigationMenu / BrandingSet / CommunitiesSettings と deploy 順序）、Named Credential（ExternalCredential + NamedCredential + AuthProvider、SAP / ServiceNow / MuleSoft プリセット、DPAPI 保存） |
| P4 | 期限切れ監視と一括削除、作成後検証レポート、データ マスキング、プロファイル テンプレート、Dev Hub クォータ確認、複数作成 |

## 制約メモ

- Org Shape は**設定・機能・ライセンスのみ**を写し、メタデータは含まない（メタデータは手順 2 以降で入れる）。
- スクラッチ組織の期限は**延長できない**。必要ならスナップショット + 再作成（P3）。
- `--source-org` は 15 桁の shape ID。指定時は定義ファイルの edition / features が上書きされる。
- `sf data import tree` は `__mdt` レコードには使えないため、カスタム メタデータはメタデータ デプロイ（`customMetadata`）で入れる。
