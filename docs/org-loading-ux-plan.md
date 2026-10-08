# 組織一覧の読み込み中 UX 改善 設計書

最終更新: 2026-10-08 / ステータス: **Phase 1〜4 完了（UIA E2E 検証済み）**
対象: メインウィンドウ + 組織管理ウィンドウ（WPF / Avalonia 両方）

---

## 1. 目的・背景

組織数の多い環境では `sf org list` に時間がかかる。その間にユーザーが操作（例: 組織管理で組織登録をクリック）しても
**反応がないように見える**というフィードバックがあった。

原因は 2 つ:

1. **取得中の表示が弱い** — ステータスバーの「組織一覧を取得中…」だけで、上部バーやコンボは通常表示のまま。
2. **操作できない理由が見えない** — 組織一覧の取得中／組織未選択のとき、ボタンが無効化されている・コマンドが黙って return する
   などで「なぜ反応しないのか」が画面に出ない。

本改善では「取得中」を明確に可視化し、操作できない状態では **無効化（ブロック）+ 理由の常時表示** で
「反応がない」と感じさせない UI にする。

## 2. 確定事項（ユーザー回答 2026-10-08）

| 項目 | 決定 |
|---|---|
| 操作方法のブロック方式 | **併用**: ボタンを無効化 + 理由を画面上に常時表示。万が一実行された場合（メニュー等）はステータスバーに理由メッセージ |
| 取得中の見せ方 | **上部バーに進捗バー + 「組織一覧を取得中…」表示**（組織コンボは「取得中…」表示 + 無効化） |
| 適用範囲 | **メインウィンドウ + 組織管理ウィンドウ**（メニュー等のクリック経路はガードでメッセージ表示） |

## 3. 現状の問題点（調査結果）

| # | 場所 | 現状 | 問題 |
|---|---|---|---|
| 1 | `MainViewModel.RefreshOrgsAsync` | `IsBusy` ガードで再入時に**黙って return** | 更新ボタン連打時に無反応（メッセージなし） |
| 2 | メイン上部バー | 取得インジケーターなし。組織コンボ・各ボタンは通常表示 | 「取得中」が分からない |
| 3 | メイン: 組織情報 / データ入出力 / バックアップ | `IsEnabled=HasSelectedOrg` で無効化済み | **理由が画面に出ない**（ツールチップにも記載なし） |
| 4 | メイン: 組織比較 | `CanCompareOrgs`（= 2 組織以上のとき有効） | 取得中も有効のまま（古い一覧で開けてしまう） |
| 5 | メイン: ブラウザー メニュー（組織ホーム / セットアップ） | クリック可。コマンド内で `Msg_SelectOrg` をステータスバーへ | ステータスバーは気づきにくい |
| 6 | 組織管理ウィンドウ | 取得中は `CanInteract` で主要ボタン無効化済み。ただし **取得中の StatusMessage を設定していない**（完了後に「組織一覧: N 件」のみ） | 無効化の理由が分からない・何か動いているか不明 |
| 7 | 各タブ（SOQL / Apex / REST / デプロイ）実行 | 組織未選択時は実行時に `Msg_SelectOrg` 表示（実装済み） | 今回は変更しない（クリック時メッセージ方式で既に機能） |

- 組織コンソール（sf 自由コマンド）は組織不要のコマンドもあるため、**ガードしない**（現状維持）
- 組織管理を開くボタンは取得中も**有効のまま**とする（組織管理は独立して動く + 組織ゼロからの復帰導線であるため）

## 4. 設計

### 4.1 状態モデル（MainViewModel）

| プロパティ | 意味 | トリガー |
|---|---|---|
| `IsLoadingOrgs`（`IsBusy` をリネーム） | 組織一覧を取得中 | `RefreshOrgsAsync` |
| `CanSelectOrg` | `!IsLoadingOrgs`（組織コンボ・更新ボタン） | IsLoadingOrgs |
| `CanUseOrgFeature` | `!IsLoadingOrgs && HasSelectedOrg`（組織情報 / データ入出力 / バックアップ） | IsLoadingOrgs / SelectedOrg |
| `CanCompareOrgs` | `!IsLoadingOrgs && Orgs.Count > 1`（既存式を拡張） | IsLoadingOrgs / Orgs |
| `HasNoOrgs` | `!IsLoadingOrgs && Orgs.Count == 0` | IsLoadingOrgs / Orgs |
| `OrgHintText` | 理由文言（下記の優先順位。null = 非表示） | IsLoadingOrgs / SelectedOrg / Orgs / 言語切替 |
| `HasOrgHint` | `OrgHintText is not null` | 同上 |

`OrgHintText` の優先順位:

1. 取得中 → 「組織一覧を取得中です。完了までお待ちください。」（`Msg_OrgsLoadingWait`）
2. 組織 0 件 → 「組織がありません。「組織管理」からログインしてください。」（`Main_NoOrgsHint`）
3. 組織未選択 → 「組織が選択されていません。上部の一覧から組織を選択してください。」（`Main_OrgRequiredHint`）

備考: `Orgs.CollectionChanged` / `SelectedOrg` 変更時に上記従属プロパティを再通知する。
`UiText.LanguageChanged` を購読し `OrgHintText`（と `StatusDetail`）を再通知する。

### 4.2 コマンドガード（防御・ステータスバーに理由表示）

無効化で通常は到達しないが、メニュー・ショートカット・自動化などの経路に備えて全コマンドの先頭で:

```
if (IsLoadingOrgs) { StatusMessage = Msg_OrgsLoadingWait; return; }
if (SelectedOrg is null) { StatusMessage = Msg_SelectOrg; return; }   // 既存ガードを維持
```

対象: `OpenOrgInfo` / `OpenDataIo` / `OpenBackup` / `OpenCompareOrgs` / `OpenBrowserAsync`（org-home / org-setup）/
`RefreshOrgsAsync`（黙って return → メッセージ付きに変更）。

### 4.3 メインウィンドウ UI

- **取得インジケーター**: 組織コンボの直後に `ProgressBar`（IsIndeterminate, 70×6）+「取得中…」（`Main_OrgLoading`）を
  `IsLoadingOrgs` の間だけ表示
- **組織コンボ**: `IsEnabled=CanSelectOrg`。取得中はコンボ上にグレーの「取得中…」オーバーレイ
  （Grid + ヒットテスト無効）を重ねて、通常の ◯◯ (org) 表示と区別
- **ヒント行**: 上部バー下段に `OrgHintText` をオレンジ（#B35C00）で常時表示（`HasOrgHint` の間）
- **ボタン**: 組織情報 / データ入出力 / バックアップ → `CanUseOrgFeature`、組織比較 → `CanCompareOrgs`、
  更新ボタン → `CanSelectOrg`

### 4.4 組織管理ウィンドウ UI

- **取得インジケーター**: ツールバーの更新ボタン横に `ProgressBar`（IsIndeterminate）+「取得中…」を `IsLoadingOrgs` 中表示
- **StatusMessage**: 取得開始時に「組織一覧を取得中…」（既存 `Msg_LoadingOrgs`）を設定（従来は完了後のみ）
- **ヒント行**: 取得中は「完了までお待ちください」、組織 0 件は登録パネルへの導線文を表示
- ボタン無効化は既存の `CanInteract` / `CanTestConnections` を維持（理由をヒント行で補う）

### 4.5 UiText 新キー（4 言語: En / Ja / Zh / Ko）

| キー | Ja | En |
|---|---|---|
| `Main_OrgLoading` | 取得中… | Loading… |
| `Msg_OrgsLoadingWait` | 組織一覧を取得中です。完了までお待ちください。 | Loading the org list — please wait until it finishes. |
| `Main_OrgRequiredHint` | 組織が選択されていません。上部の一覧から組織を選択してください。 | No org selected — choose one in the top bar. |
| `Main_NoOrgsHint` | 組織がありません。「組織管理」からログインしてください。 | No orgs found — sign in from Org Management. |
| `OrgManage_NoOrgsHint` | 組織がありません。「組織を登録」からログインしてください。 | No orgs found — use "Register org" to sign in. |

## 5. フェーズ計画

- ✅ **Phase 1（状態モデル + ガード）**: MainViewModel（`IsBusy` → `IsLoadingOrgs` に改名・`CanSelectOrg` / `CanUseOrgFeature` / `CanCompareOrgs` / `HasNoOrgs` / `OrgHintText` / `HasOrgHint` 追加・コマンドガード・`UiText.LanguageChanged` 購読）/ OrgManageViewModel（`IsLoadingOrgs` + `OrgListHintText`・取得開始時 StatusMessage）/ UiText 5 キー ×4 言語。ビルド 0 警告 + テスト 520 件グリーン
- ✅ **Phase 2（WPF UI）**: `MainWindow.xaml`（上部バーを縦 2 段化: インジケーター + ヒント行 / コンボの「取得中…」オーバーレイ / `IsEnabled` 切替 / 更新ボタン無効化）+ `OrgManageWindow.xaml`（進捗バー + 「Loading…」 + ヒント行 + コンボ無効化）
- ✅ **Phase 3（Avalonia UI）**: 同等の変更（`IsVisible` 直接バインド・`ProgressBar IsIndeterminate`）
- ✅ **Phase 4（動作確認 + 仕上げ）**: 遅延スタブ / 空一覧スタブを使った UIA E2E 4 状態検証 + スクリーンショット 4 枚（下記§6）。設計書更新 + コミット

## 6. 検証方法（Phase 4 の詳細）

- **遅延スタブ**: `C:\SfUiDemo\sf-slow-orgs.cmd`（`timeout` 相当で 15 秒待ってから `sf org list --json` 相当の出力を返す）
- **空一覧スタブ**: `C:\SfUiDemo\sf-empty-orgs.cmd`（組織 0 件の JSON）
- **UIA チェック**（新規スクリプト `C:\SfUiDemo\sfui-org-loading-check.ps1`）:
  1. 遅延スタブで起動 → **取得中**: ProgressBar が存在・ヒント（取得中…）が表示・組織情報ボタンが `IsEnabled=false`・
     スクリーンショット（`compare-...` と同様の PrintWindow）
  2. 取得完了後: ボタン `IsEnabled=true`・ヒントが消える・組織が選択される
  3. 空一覧スタブで起動 → ヒント（組織がありません…）表示・ボタン無効のまま
  4. 組織管理ウィンドウを開き、取得中の進捗表示 + StatusMessage を確認
- テスト: 既存 520 件 + LocalizationUsageTests（新キーの存在）が緑であること。ビルド 0 警告。

## 7. リスク・注意

- `IsBusy` → `IsLoadingOrgs` リネームは MainViewModel 内のみ（ビューは未バインド）。XAML 側の既存バインディングに影響しないことをビルドで確認する
- 言語切替時に `OrgHintText` が古い言語のまま残る問題への対処（`UiText.LanguageChanged` 購読）を含める
- 取得中の ProgressBar は「組織一覧」のものと分かるよう、コンボの直後に置く（他の処理の進捗と混同しない）
- 組織管理の「組織を登録」ボタンは取得中に無効化されたまま（仕様）。理由（取得中）をヒント行で必ず表示する

## 8. 検証結果（2026-10-08）

検証スクリプト: `C:\SfUiDemo\sfui-org-loading-check.ps1` / スタブ: `sf-slow-orgs.cmd`（約 15 秒遅延）・`sf-empty-orgs.cmd`

| 状態 | 結果 |
|---|---|
| 取得中（メイン） | 進捗バー 1 個・「Loading orgs…」表示・待機ヒント表示・組織情報/データ入出力/バックアップ/比較の各ボタンが無効（`IsEnabled=false`）✓ ／ `org-loading-during.png` |
| 取得完了（メイン） | 進捗バー消滅・ヒント消滅・ボタン有効化・「Loaded 3 org(s)」✓ ／ `org-loading-after.png` |
| 取得中（組織管理） | 進捗バー・「Loading orgs…」・待機ヒント・「Reload orgs」無効・登録パネルの「Register」無効（報告事象の再現状態で無効化理由が明示されることを確認）✓ ／ `org-loading-orgmanage.png` |
| 空一覧 | 「No orgs found — sign in from Org Management.」ヒント・ボタン無効のまま・「Loaded 0 org(s)」✓ ／ `org-loading-empty.png` |

- テスト 520 件グリーン / ビルド 0 警告・0 エラー（WPF + Avalonia 両方）
