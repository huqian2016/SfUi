# ER 図エクスポート計画（v0.22.0 候補）

> 目的: 組織メタデータから **編集可能な ER 図** を出力する。
> 配置: **組織情報 → オブジェクト** と **データ入出力** の両方に「ER 図を出力」ボタン。
> 関連: `docs/report-export-plan.md`（レポート出力の共通基盤）

---

## 1. 方針

| 項目 | 決定 |
| --- | --- |
| 「編集可能」の解釈 | テキスト形式（Mermaid / DBML / DOT / PlantUML）はそのまま手編集、draw.io は図としてドラッグ編集 |
| 形式 | **Mermaid / DBML / draw.io / PlantUML / DOT / HTML** の 6 つ + **画像**（PNG / JPEG / WebP / SVG） |
| 名前表示 | ダイアログで **ラベル / API 名 / ラベル＋API 名** を選択（既定 = ラベル＋API 名） |
| 画像生成 | 自前レイアウト（グリッド配置 + 直交エッジ）→ **SVG はテキスト生成**、ラスターは **SkiaSharp** で描画（クロスプラットフォーム） |
| 生成方向 | 一方向（再生成で手編集は失われる）。手編集は `.drawio` 側で行う運用を README に明記 |

---

## 2. データ取得

```
SObjectDescribeService.ListObjectsAsync(org)          → 選択ダイアログの候補（API 名 / ラベル / カスタム判定）
SalesforceRestClient.QueryAsync(tooling: true)        → 参照関係（1 回のクエリ + 選択オブジェクトで絞り込み）
    SELECT EntityDefinition.QualifiedApiName, QualifiedApiName, Label, DataType,
           ReferenceTo, RelationshipName, IsNillable
    FROM FieldDefinition
    WHERE EntityDefinition.QualifiedApiName IN (...)
```

- `ReferenceTo` が空でない項目 = 参照項目。`DataType` に `Master-Detail` を含むものは必須側。
- 多態参照（`WhoId` / `WhatId` など `ReferenceTo` が複数）は **参照先ごとに 1 エッジ**を張る。
- 既定で除外: `__mdt` / `__e` / `__history` / `__share` / `__ChangeEvent` / 設定系（`IsCustomSetting`）。
- 自己参照（`Account.ParentId`）は 1 本のループエッジ。
- 中間オブジェクト（Id + 参照 2 つ）は「多対多に畳む」オプション（既定 OFF）。

---

## 3. 出力形式のマッピング

| 形式 | 拡張子 | 内容 |
| --- | --- | --- |
| Mermaid | `.md` / `.mmd` | `erDiagram` + エイリアス（ラベル）。GitHub / VS Code / mermaid.live で描画 |
| DBML | `.dbml` | `Table` + `[pk]` / `[ref: > X.Id]` → dbdiagram.io に貼り付け |
| draw.io | `.drawio` | `mxGraphModel` のノード（スイムレーン）+ エッジ。diagrams.net / VS Code で編集 |
| PlantUML | `.puml` | `entity` + `||--o{`。plantuml.com / VS Code |
| DOT | `.dot` | Graphviz。`dot -Tsvg` で高品質レイアウト |
| HTML | `.html` | **自己完結**（外部ファイル参照なし）。SVG 図の埋め込み + リレーション表 / オブジェクト表 + 絞り込み検索 + 印刷（PDF 保存）。行クリックで図のノードを強調 |
| SVG | `.svg` | 自前レイアウトのベクター画像（Excel/PowerPoint に貼れる） |
| PNG / JPEG / WebP | `.png` / `.jpg` / `.webp` | SkiaSharp で SVG と同じレイアウトをラスタライズ |

- 名前表示の切替は全形式に共通（ノード見出し / エイリアス / シート名）。
- 画像は **1 枚**（複数形式を選んだ場合は形式ごとに 1 ファイル）。

---

## 4. レイアウト（画像・HTML・draw.io 共通）

1. **放射状レイアウト**: 関連数（次数）降順 → API 名順に並べ、最も関連の多いエンティティを中心に置き、残りを同心円のリングへ外側に並べる。
   - リングの定員は円周から算出（`2πR / (ノード幅 + 間隔)`、`ErdImageOptions.Columns` で固定可）。
   - リング内の配置角度は、既に配置済みの関連先がある方角に最も近いスロットを選ぶ（関連が近くに集まる）。
   - ノードが重なる場合はそのリングの半径を外側へ広げる（放射状の見た目を保ったまま重なりを解消）。
2. ノード: 幅 280px、高さ = 見出し 34px + 行数 × 18px + 余白（最大 12 行）。
3. エッジ: 親 → 子の中心を結ぶ向きで矩形境界を求め、境界点の間に折れ点を置く（`lane` で平行線を扇状に分ける）。`1` 側に `|`、`N` 側に鳥の足。密な図（40 本超）では `1` / `N` の文字を省略、12 本超では項目名ラベルも省略。
4. 背景白、見出しは濃色、参照項目は `FK` バッジ。
5. 上限: エンティティ 80 / エッジ 200（超える場合は警告して先頭のみ）。

---

## 5. ダイアログ UX

- オブジェクト選択（検索 + チェック、全選択 / 標準のみ / 選択解除）
- 表示名: `ラベル` / `API 名` / `ラベル＋API 名`
- オプション: 標準オブジェクトを含む、中間オブジェクトを多対多に畳む、カスタム項目のみ
- 出力形式: Mermaid / DBML / draw.io / PlantUML / DOT / HTML / SVG / PNG / JPEG / WebP（複数選択）
- 出力先フォルダー + ファイル名（既定 `erd-<org>-<yyyyMMdd-HHmmss>`）
- **設定の記憶**: 形式・表示名・オプション・出力先は `data/export-settings.json` に保存し、次回起動時（ダイアログを開いたとき）に復元する（`ExportSettingsStore`）。

---

## 6. 実装ステージ

| Stage | 内容 | 状態 |
| --- | --- | --- |
| **1** | Core モデル + 5 テキスト形式ライター + 単体テスト | ✅ 完了（13 テスト） |
| **2** | 参照関係の取得（`ErdService`）+ モデル構築 + テスト | ✅ 完了（12 テスト） |
| **3** | 画像レンダラー（SVG + SkiaSharp PNG/JPEG/WebP） | ✅ 完了（5 テスト・目視確認済み） |
| **4** | ダイアログ VM + 出力サービス + WPF / Avalonia ダイアログ | ✅ 完了（4 テスト） |
| **5** | 組織情報（オブジェクト）とデータ入出力にボタン追加 | ✅ 完了（両シェル・DI 登録済み） |
| 6 | UI E2E、README / マニュアル、リリース | 未着手 |

### 実組織での確認結果（2026-10-24 / hks4sand1）

- Tooling API の `FieldDefinition` は `DataType = "Lookup(Account)"` / `"MasterDetail(Account)"`、`ReferenceTo = { referenceTo: [...] }` 形式で返る
  （当初 `DataType IN ('Lookup','Master-Detail')` で 0 件だったため、**サーバー側で型フィルタせず** `ReferenceTo` のある行だけをクライアント側で採用する方式に変更）
- 614 オブジェクト取得 → Account / Contact / Opportunity を選択 → **3 エンティティ / 13 リレーション**を生成
- 8 ファイル（mmd / md / dbml / drawio / puml / dot / svg / png）を出力、PNG を目視確認（日本語ラベル・PK/FK・直交エッジ）
- エッジが多い（> 12 本）ときはラベルを省略して重なりを回避、ラベルは中央の縦線分に白フチ付きで配置


---

## 7. テスト方針

- `ErdDiagramWriterTests`: 名前スタイル 3 種、master-detail の必須カーディナリティ、多態参照の 2 エッジ、XML エスケープ（`&` / `<` / 日本語）、空ドキュメント。
- `ErdServiceTests`: 偽 REST（FieldDefinition の JSON）からエッジ生成、システムオブジェクト除外、多対多の畳み込み。
- `ErdImageRendererTests`: SVG に `<svg` / ノード数 / エッジ数、PNG の先頭バイト（89 50 4E 47）。
- UI は E2E（`sfui-ui-e2e`）で「ボタン → ダイアログ → 出力」を確認。
