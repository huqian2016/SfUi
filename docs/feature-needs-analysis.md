# 次期機能 Needs 調査 — A. 変化の記録・監視 / B. スケジュール実行

最終更新: 2026-10-08 / ステータス: **調査完了（実装方針は未確定）**

## 1. 調査の目的と方法

- 目的: 次に実現する候補 A（設定・情報・レコードの変化を記録する機能）と B（操作のスケジュール実行）について、
  **どのサブ機能の Needs が大きいか**を外部エビデンスと SfUi の既存資産の両面から整理する。
- 方法: ① 商用 Salesforce DevOps ツール（Gearset 等）の機能訴求 ② 大手 OSS（sfdx-hardis）の監視機能 ③
  Salesforce 公式コミュニティ（Admin Blog / Help・Salesforce ネイティブ機能の制約）④ コミュニティの声（Reddit 等）
  を参照し、機能分解 → Needs 評価 → 優先順位（案）にまとめた。

## 2. 外部調査の結果（要点）

| ソース | 要点 | 示唆 |
|---|---|---|
| **Gearset「Change Monitoring」**（商用・主力機能） | メタデータ変更の自動監視ジョブ。**日次レポート + 通知**、監視履歴（いつからいつまでの差分を遡れる）、コンプライアンス監査用エクスポート（HIPAA/SOX/GDPR）、コード品質の推移。「変更を見逃さない」をコアメッセージにしている | **設定/メタデータの変化監視は商用ツールの主力＝支払意欲の高い Needs** |
| **sfdx-hardis「Salesforce Org Monitoring」**（OSS・Cloudity、Dreamforce 登壇あり） | 「*Salesforce Audit Trail tells you who updated what, but not the before/after. A git-based metadata backup gives you the full picture.*」→ **日次メタデータバックアップ + 前回との差分 + 通知**（Slack/Teams/メール/Grafana）。頻度は daily/weekly/biweekly/monthly を設定可能。監視項目は Apex エラー・権限・未使用メタデータ・API 制限値・リリース更新まで多岐 | **「定期 + 差分 + 通知」が定番の型**。GUI・ローカル実行型の同種機能は空白 |
| **Salesforce Admin Blog「Monitor Unwanted Changes to Reports」（MVP 執筆）** | 管理者が「レポートの意図しない変更」を検知したい → カスタムレポートタイプ + レポートのレポート + **毎朝 7 時のメール購読（変化があった時だけ届く）** で解決。週 1 件あった問い合わせが四半期 1 回未満に減少 | 管理者は **「読まなくても変化があった時だけ教えてくれる」監視 + 通知**を強く求める（ネイティブだけでは不足し、ワークアラウンドを作っている） |
| **Salesforce ネイティブ機能の制約** | Setup Audit Trail = 誰が何をしたかは分かるが **before/after が分からない**・保持 180 日。Field Audit Trail/Field History = **オブジェクトあたり 20 項目まで**・保持期間あり（Field Audit Trail は有償）。Event Monitoring は有償。データエクスポートは週次（ダウンロードは API で 30 日） | **「前回との差分を自分の手元に残す」需要はネイティブで満たせない**（=機能価値の核） |
| **スケジュール実行の関連動向** | Salesforce 標準の週次データエクスポート / SFDC File Exporter は「ワンタイム予約〜繰り返し」の 3 モードを訴求 / sfdx-hardis は CI の夜間ジョブとして実行（*every night or on your own schedule*） | **「予約して自動で回す」は管理業務の定番ニーズ**。実行基盤（アプリ未起動でも動く）が価値を左右する |
| コミュニティ（Reddit「Track junior Admin changes」等） | 下位管理者の変更追跡は「毎日の変更レポート（Gearset）」等の監視系ツールで解決しているという回答 | 小規模チームでも「変更の把握」は関心が高い |

## 3. 機能の分解と Needs 評価

凡例: ★=Needs 大（根拠が明確） / ☆=中 / ・=小。コスト感は SfUi の既存資産からの実装規模（小/中/大）。

### A. 変化の記録・監視

| # | サブ機能 | Needs | 主な根拠 | SfUi との相性・既存資産 | コスト感 |
|---|---|---|---|---|---|
| A1 | **組織設定/メタデータのスナップショット + 前回との差分（タイムライン）** | ★ | Gearset・sfdx-hardis の中核。Audit Trail は before/after が無い | OrgInfoCache（fetchedAt 付きセクション JSON）+ 組織比較エンジン（突合/差分/CSV）をそのまま履歴化できる | 小〜中 |
| A2 | **定期監視 → 変化があった時だけ通知（アプリ内→メール/Slack は将来）** | ★（B2 と連動） | 両ツールの通知がコア。Admin Blog も「変化時のみ毎朝メール」を選択 | B（スケジューラ）+ A1 の合成。通知はステータスバー/実行履歴 + デスクトップ通知から | 中 |
| A3 | **レコードの増減・更新の記録（対象オブジェクトを限定して定期取得し差分）** | ☆〜★ | 連携データの流入確認・意図しない削除・件数推移。ネイティブは Field History 20 項目/保持限定 | バックアップ（全件スナップショット）+ バックアップ比較（レコード突合）が既にあり、対象を「監視」として定期実行するだけ | 中 |
| A4 | **変化の一覧 UI（横断・検索・フィルタ）と AI 要約（「今週何が変わった？」）** | ☆ | 変化を“読む”コスト削減。sfdx-hardis も AI エージェント連携を開始 | 比較ビューの再利用 + AI チャット（差分コンテキスト添付） | 小〜中 |
| A5 | **監査用エクスポート（誰がいつ何を）** | ☆ | コンプライアンス用途。ただし監査は商用製品の領域 | CSV/Excel 出力は既存 | 小 |

### B. スケジュール実行

| # | サブ機能 | Needs | 主な根拠 | SfUi との相性・既存資産 | コスト感 |
|---|---|---|---|---|---|
| B1 | **操作テンプレート（履歴/お気に入りから予約を作成）** | ★ | SFDC File Exporter 等が「繰り返し予約」を主力訴求。日次エクスポート/バックアップは定番 | 履歴（種別+パラメータ）とお気に入り（Quick Panel）が既に“テンプレート”として存在 | 小 |
| B2 | **スケジューラ本体（日時 + 繰り返し: 日/週/月/複数曜日）** | ★ | 同上。sfdx-hardis は daily/weekly/biweekly/monthly を実装 | 新規（Core にスケジュール定義 + 実行エンジン） | 中 |
| B3 | **アプリ未起動でも実行（Windows タスク スケジューラ登録 + ヘッドレス実行 `--run-task`）** | ★ | 「予約したのに動いていなかった」を防ぐ。CI 前提でない個人/小規模チームには必須 | 既存のコマンドライン基盤（--smoke 等の引数処理）を拡張 | 中 |
| B4 | **実行結果の記録・確認（成功/失敗・出力ファイル・所要時間）** | ☆ | 信頼性の前提。両ツールも履歴と成果物を保存 | 履歴ストア + 既存ウィンドウ（出力パス保存） | 小〜中 |
| B5 | **失敗通知・条件実行（変化がある時だけ実行 等）** | ☆ | ノイズ削減。Gearset は通知しきい値設定あり | A2 と共通 | 中 |

### まとめ（Needs の大きい順）

1. **A1 設定/メタデータの差分履歴**（商用の主力機能・ネイティブで代替不能）
2. **B2+B3 スケジュール実行（特にアプリ未起動でも動く形）**
3. **A2 定期監視 + 変化時通知（A1+B の合成 = 最も「欲しい体験」）**
4. **B1 テンプレート**（既存の履歴/お気に入りから作れるので低コストで満足度が高い）
5. **A3 レコード変化の記録**（対象を絞れば高価値。ボリューム/API 消費とストレージ設計が論点）
6. 以降: B4/B5 実行結果管理・失敗通知、A4 AI 要約、A5 監査エクスポート

## 4. 推奨する進め方（段階案・たたき台）

- **Phase 1（A の核 / 手動でも成立）**: 組織情報スナップショットの履歴保存（実行ごとに data 配下へ）+
  「前回との差分」ビュー（既存の比較ビューを流用）+ AI 要約ボタン。**スケジューラ無しでも価値が完結**する
- **Phase 2（B の核）**: スケジュール定義 UI（履歴/お気に入りから予約、日/週/月）+ Windows タスク スケジューラ登録 +
  `SfUi.exe --run-task <id>` ヘッドレス実行 + 実行結果を履歴/レポートに保存
- **Phase 3（A×B）**: 定期監視プリセット（組織情報の日次差分、レコード件数の日次記録、選択オブジェクトの変化監視）+
  「変化があった時だけ」通知（アプリ内から。メール/Slack は将来検討）
- **Phase 4（発展）**: レコード変化の記録（対象オブジェクト/項目を限定）、監視ダッシュボード、通知連携（メール/Slack）

## 5. 設計上の注意（先に決めておきたい論点）

- **スナップショットの保持ポリシー**: 日次×組織数×セクションでディスクが増える → 世代数上限・圧縮・差分のみ保持
- **API 消費**: 監視対象を「変更検知に必要な最小」に絞る（Tooling `LastModifiedDate` の活用、増分のみ）
- **アプリ未起動問題**: B3 を採らない場合、「起動中のみ実行」であることを UI 上で明確に（失敗と誤解されない）
- **通知の出しすぎ問題**: 「変化があった時だけ」「重要度しきい値」を最初から設計（Gearset/sfdx-hardis とも設定可能にしている）

## 6. 参考リンク

- Gearset — Change Monitoring: https://gearset.com/solutions/automate/change-monitoring/
- sfdx-hardis — Salesforce Org Monitoring: https://sfdx-hardis.cloudity.com/salesforce-monitoring-home/
- Salesforce Admin Blog — How I Solved It: Monitor Unwanted Changes to Reports: https://admin.salesforce.com/blog/2022/how-i-solved-it-monitor-unwanted-changes-to-reports
- Salesforce Help — Manage Scheduled Exports for a Data Backup / Field Audit Trail / Setup Audit Trail（保持期間・20 項目上限の制約）
- Reddit r/salesforce — Track junior Admin changes（変更追跡の相談に対する「日次変更レポート」回答）
