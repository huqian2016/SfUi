# 複数選択と一括操作 対応計画（テーブル行の一括操作）

最終更新: 2026-10-08 / ステータス: **全 Phase 完了（実装 + 動作確認済み: WPF + Avalonia）**
対象: WPF (`SfUi.App`) + Avalonia (`SfUi.Avalonia`) の両方

---

## 1. 目的・背景

- ユーザー要望:
  - テーブルの行を**複数選択して一括操作できるなら一括操作**したい。
  - 一括操作できない機能なら、**1 行を選択するようユーザーにメッセージ**する。
  - 例: 組織管理ウィンドウで組織を複数選択して「疎通テスト」を押すと現状は 1 件しか実行しない → 選択中の全行を実行する。

- 現状の問題（調査で確認）:
  - WPF / Avalonia の DataGrid は既定で Extended 選択（Ctrl / Shift で複数選択は**見た目上は可能**）。
  - しかし ViewModel は `SelectedItem`（現在行 1 件）しか見ておらず、**複数選択しても 1 件にしか適用されない**。
  - 単一必須の操作（既定組織の設定など）は、複数選択時に**黙って現在行へ適用**される（誤操作の温床）。
  - 履歴タブの「パラメータをコピー」「選択を削除」、復元タブの「バックアップ削除」は**未選択時にサイレント no-op**（メッセージなし）。

## 2. 方針

| # | 方針 | 内容 |
|---|---|---|
| P1 | 一括化できる操作は一括化 | 選択行すべてに**順次**適用（並列化しない。API 負荷とキャンセル性を優先） |
| P2 | 単一必須の操作はガード | 0 行選択 → 「行を選択」、複数行選択 → 「1 行だけ選択」を**状態バーへ表示**（サイレント無視を禁止） |
| P3 | 破壊的一括は件数入り確認 | ログアウト / 履歴削除 / バックアップ削除は「N 件」を明記した確認ダイアログ + 実行後サマリ（成功/失敗） |
| P4 | 「現在行」の意味は維持 | `SelectedItem` = 組織コンボ・ヘルス/棚卸しタブの対象・タグ/メモ入力欄の同期（従来どおり） |
| P5 | 選択 UI は Extended のみ | チェックボックス列は導入しない（Ctrl / Shift + クリックで複数選択） |

## 3. 調査結果（行操作インベントリ）

### 3.1 一括化する（本計画の対象）

| 画面 | 操作 | 現在 | 変更後 | Phase |
|---|---|---|---|---|
| 組織管理 / 組織タブ | 疎通テスト | 現在行 1 件 | **選択行すべて**（進捗・キャンセル・OK/NG サマリ） | 1 |
| 組織管理 / 組織タブ | ブラウザーで開く | 現在行 1 件 | **選択行すべて**（オープン数/失敗サマリ） | 1 |
| 組織管理 / 組織タブ | ログアウト | 現在行 1 件 | **選択行すべて**（件数入り確認・進捗・サマリ） | 1 |
| 履歴タブ | 選択を削除 | 現在行 1 件 | **選択行すべて**（件数入り確認・削除件数表示） | 2 |
| バックアップ / 復元タブ | バックアップ削除 | 現在行 1 件 | **選択行すべて**（件数入り確認・サマリ） | 3 |

### 3.2 単一必須としてメッセージを追加・維持する

| 画面 | 操作 | 現在 | 変更後 | Phase |
|---|---|---|---|---|
| 組織管理 / 組織タブ | 既定に設定 | 複数選択でも現在行に適用 | 複数選択時「組織を 1 つだけ選択してください」 | 1 |
| 組織管理 / 組織タブ | 別名を設定 | 同上 | 同上 | 1 |
| 組織管理 / 組織タブ | タグ/メモ保存 | 同上 | 同上 | 1 |
| 履歴タブ | パラメータをコピー | 未選択でサイレント | 未選択「行を選択」/ 複数「1 行だけ選択」 | 2 |
| 復元タブ | バックアップ削除 | 未選択でサイレント | 未選択「行を選択」（3.1 の一括化と同時に） | 3 |
| ログタブ | Fetch | ガードあり（`Log_SelectToFetch`） | 変更なし | - |
| バックアップ / 比較タブ | Compare | ガードあり（`BackupCompare_NeedTwo`） | 変更なし | - |
| 組織情報 / オブジェクト項目 | 使用箇所を検索 | ガードあり（`OrgInfo_Fields_SelectField`） | 変更なし | - |

### 3.3 対象外（対応しないと決定したもの + 理由）

| 画面 | 操作 | 理由 |
|---|---|---|
| 組織管理 / 移行棚卸しタブ | Setup（行内セルボタン） | 行固有のリンクボタン（選択とは独立）。CSV 出力は既に全表示行対象 |
| 組織管理 / ヘルスタブ | - | 行操作なし（表示専用） |
| 履歴タブ | ダブルクリック再実行 | 行固有の操作（対象行が明確） |
| 履歴タブ | Delete all | 既に全件一括（件数入り確認あり） |
| バックアップ / バックアップタブ | オブジェクト選択 | チェックボックスで既に複数選択対応 |
| 復元タブ | オブジェクト選択 | 同上 |
| データ入出力 / アクセス系タブ | マトリクス操作 | チェックボックスで既に複数選択対応 |
| 組織情報 / 各セクション | Open Setup / Data I/O | 行選択は任意（未選択でも可） |
| 組織情報 / マイ設定 | Move up/down / Remove | ピッカー UI の並べ替え。複数一括は仕様が曖昧（単一行操作が自然） |
| 項目使用箇所ウィンドウ | 行リンク / ダブルクリック | 行固有のリンク遷移 |
| ログ解析ウィンドウ | イベント行選択 | 詳細表示用（行固有） |
| クイックパネル / お気に入り | 実行 / 削除 | ランチャー UI（行固有） |
| 比較系ウィンドウ各タブ | ↗ セルリンク | 行固有のリンク遷移 |
| 各タブの履歴 ComboBox | 選択で復元 | コンボボックス（テーブルではない） |

## 4. 設計

### 4.1 選択の同期（View → ViewModel）

DataGrid / ListBox の `SelectedItems` は直接バインディングできないため、コードビハインドの `SelectionChanged` から ViewModel へ渡す。

```csharp
// WPF (OrganizationWindow.xaml.cs / HistoryView.xaml.cs / RestoreTabView.xaml.cs)
private void OrgsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (sender is DataGrid grid && DataContext is OrgManageViewModel vm)
    {
        vm.SetSelectedOrgRows(grid.SelectedItems.OfType<OrgManageOrgRowViewModel>().ToList());
    }
}
```

```csharp
// Avalonia（コードビハインド ctor で購読）
if (this.FindControl<DataGrid>("OrgsGrid") is { } grid)
{
    grid.SelectionChanged += (_, _) =>
        _viewModel.SetSelectedOrgRows(grid.SelectedItems?.OfType<OrgManageOrgRowViewModel>().ToList() ?? []);
}
```

ViewModel 側は次の共通形とする。

```csharp
private readonly List<OrgManageOrgRowViewModel> _selectedOrgRows = new();
public IReadOnlyList<OrgManageOrgRowViewModel> SelectedOrgRows => _selectedOrgRows;

/// <summary>View から呼ばれる: 複数選択の内容を反映する。</summary>
public void SetSelectedOrgRows(IReadOnlyList<OrgManageOrgRowViewModel> rows) { … }

/// <summary>一括操作の対象（複数選択が空なら現在行 1 件へフォールバック）。</summary>
private IReadOnlyList<OrgManageOrgRowViewModel> EffectiveRows() =>
    _selectedOrgRows.Count > 0 ? _selectedOrgRows.ToList()
    : SelectedOrgRow is { } row ? new[] { row } : Array.Empty<OrgManageOrgRowViewModel>();
```

- XAML では `SelectionMode="Extended"` を明示（WPF / Avalonia とも既定 Extended だが意図を明示）。
- `_suspendSelection`（一覧再構築中）でも選択リストの更新は受けるが、副作用（ApplyTarget）は従来どおり抑止。

### 4.2 コマンド仕様（組織管理 / 組織タブ）

| コマンド | 0 行 | 1 行 | 複数行 |
|---|---|---|---|
| 疎通テスト | `OrgManage_NeedOrg` | 従来どおり（1 件実行 + 結果表示） | 順次実行 + `OrgManage_TestingFmt` 進捗 + `OrgManage_TestSummaryFmt` サマリ（キャンセル可） |
| ブラウザーで開く | `OrgManage_NeedOrg` | 従来どおり | 順次オープン + `OrgManage_OpenSummaryFmt`（失敗件数付き） |
| ログアウト | `OrgManage_NeedOrg` | 従来の確認 + 実行 | `OrgManage_LogoutConfirmMultiFmt`（件数）→ 順次実行 + `OrgManage_LogoutProgressFmt` → `OrgManage_LogoutSummaryFmt` → 一覧再読込 |
| 既定に設定 | `OrgManage_NeedOrg` | 従来どおり | `OrgManage_NeedSingleOrg`（実行しない） |
| 別名を設定 | `OrgManage_NeedOrg` | 従来どおり | `OrgManage_NeedSingleOrg` |
| タグ/メモ保存 | `OrgManage_NeedOrg` | 従来どおり | `OrgManage_NeedSingleOrg` |

- 実行前に対象リストをローカル変数へコピーする（途中の再読込・選択変更に影響されない）。
- 一括実行中は `IsBusy`（ログアウト/オープン）または `IsTestingConnections`（テスト）でボタンを無効化し、Cancel で中断可能。
- `Test all orgs` は**全登録組織**のまま（ラベル通り。ソースコメントの「表示中」は誤記のため修正）。

### 4.3 コマンド仕様（履歴タブ / 復元タブ）

| 画面 | コマンド | 0 行 | 1 行 | 複数行 |
|---|---|---|---|---|
| 履歴 | 選択を削除 | `Common_NeedRow` | 従来の確認 + 削除 | `History_DeleteConfirmMultiFmt`（件数）→ 一括削除 → `History_DeletedFmt` |
| 履歴 | パラメータをコピー | `Common_NeedRow` | 従来どおり | `Common_NeedSingleRow` |
| 復元 | バックアップ削除 | `Common_NeedRow` | 従来の確認 + 削除 | `Restore_DeleteConfirmMultiFmt` → 一括削除 → `Restore_DeletedMultiFmt` |

- 履歴タブは状態表示欄が無いため、ツールバー下にステータス行（TextBlock）を追加（WPF / Avalonia）。
- 削除は既存 API をループ（`HistoryStore.Delete(type, id)` / `BackupService.DeleteBackup(id)`）。途中失敗は件数集計しサマリへ反映。

### 4.4 UiText 追加キー（4 言語: En / Ja / Zh / Ko）

| キー | EN 例 | JA 例 |
|---|---|---|
| `OrgManage_NeedSingleOrg` | Select only one org for this operation | この操作は組織を 1 つだけ選択してください |
| `OrgManage_LogoutConfirmMultiFmt` | Logout from the {0} selected orgs?\n(You can log in again with sf org login web.) | 選択した {0} 件の組織からログアウトしますか？\n（再度 sf org login web でログインできます） |
| `OrgManage_LogoutProgressFmt` | Logging out… {0}/{1} | ログアウト中… {0}/{1} |
| `OrgManage_LogoutSummaryFmt` | Logged out from {0} org(s) ({1} failed) | {0} 件の組織からログアウトしました（失敗 {1} 件） |
| `OrgManage_OpenSummaryFmt` | Opened {0} org(s) in a browser ({1} failed) | {0} 件の組織をブラウザーで開きました（失敗 {1} 件） |
| `Common_NeedRow` | Select a row | 行を選択してください |
| `Common_NeedSingleRow` | Select only one row for this operation | この操作は 1 行だけ選択してください |
| `History_DeleteConfirmMultiFmt` | Delete the {0} selected entries?\n(Result files are also removed) | 選択した {0} 件の履歴を削除しますか？\n（結果ファイルも削除されます） |
| `History_DeletedFmt` | Deleted {0} history item(s) | 履歴を {0} 件削除しました |
| `Restore_DeleteConfirmMultiFmt` | Delete the {0} selected backups?\n(This cannot be undone) | 選択した {0} 件のバックアップを削除しますか？\n（元に戻せません） |
| `Restore_DeletedMultiFmt` | Deleted {0} backup(s) ({1} failed) | {0} 件のバックアップを削除しました（失敗 {1} 件） |

- 既存キーの文言更新: `OrgManage_TestTip`（複数選択で全件対象の旨を追記）、`OrgManage_LogoutTip`（org(s) 表記）。
- `UiTextTests.AllLanguages_HaveSameKeys` のため **4 ファイルすべてに追加必須**。

### 4.5 エッジケース

| ケース | 対応 |
|---|---|
| 一括実行中に組織一覧が再構築される（ログアウト後） | 対象は実行開始時にコピー済み。再読込はループ完了後に 1 回 |
| 一覧再構築でグリッドの選択がクリアされる | `RebuildRows` の既存の選択復元（1 件）を維持。`_selectedOrgRows` は SelectionChanged 経由で自然に更新 |
| フィルタ（検索）中の一括テスト | 選択行のみ対象（フィルタと独立）。`Test all orgs` は従来どおり全登録組織 |
| 部分失敗（一部の org で NG） | サマリに失敗件数を表示し、各行の Connection 列も個別更新 |
| キャンセル | 既存の Cancel（`_testCts` / `_cts`）でループを中断（部分結果は保持） |
| 複数選択時のタグ/メモ入力欄 | ガードメッセージで保存不可。入力欄は現在行の値を表示（従来どおり） |
| Avalonia の Ctrl+A | 非対応（既定の範囲選択のみ）。必要になったら KeyBinding を追加 |

## 5. フェーズと変更ファイル

### Phase 1: 組織管理 / 組織タブ（本要望の主目的）✅ 完了（2026-10-08）
- [x] `src/SfUi.Presentation/ViewModels/OrgManageViewModel.cs`: 選択リスト + 一括テスト/オープン/ログアウト + 単一ガード
- [x] `src/SfUi.App/Views/OrgManageWindow.xaml(.cs)`: `SelectionMode="Extended"` + `SelectionChanged` 配線
- [x] `src/SfUi.Avalonia/Views/OrgManageWindow.axaml(.cs)`: 同上
- [x] UiText 4 ファイル: キー 5 追加 + 2 文言更新
- [x] 検証: ビルド / 全テスト 552 緑 / UIA チェック（`C:\huqian\sfui-orgmanage-bulk-check.ps1` **7/7 PASS**（WPF）、`sfui-orgmanage-bulk-avalonia.ps1` **7/7 PASS**）
  - 実測: 2 組織選択 →「疎通テスト」=「Connection test finished: 2 OK / 0 failed」/ 2 行の Connection 列更新 / 複数選択で「Set as default」→「Select only one org for this operation」/ 「Logout」→「Logout from the 2 selected orgs?」表示 →「No」でキャンセル（実ログアウトなし）
- ▶ 副産物: **Avalonia `MessageDialog` の重大バグを発見・修正**（`AvaloniaXamlLoader.Load` のため x:Name フィールドが null → すべての確認ダイアログが例外で無表示だった。`InitializeComponent()` に変更）

### Phase 2: 履歴タブ ✅ 完了（2026-10-08）
- [x] `HistoryViewModel.cs`: `SelectedEntries` + 一括削除 + 単一ガード + `StatusMessage` 追加
- [x] `HistoryView.xaml(.cs)` / `HistoryView.axaml(.cs)`: Extended + SelectionChanged + ステータス行
- [x] UiText: キー 4 追加
- [x] 検証: 単体テスト（`HistoryViewModelTests` 新規 6 件: 実ストア + フェイクサービスで一括削除/ガード）→ 全 558 テスト緑（並列フレーク 1 件も `OrgRecordCompareServiceTests` の Localization コレクション参加で恒久修正）
- [x] UIA 検証: WPF `C:\huqian\sfui-history-bulk-check.ps1` **5/5 PASS** / Avalonia `sfui-history-bulk-avalonia.ps1` **5/5 PASS**（2 行選択 → 確認に「2」→「Deleted 2 history item(s)」→ 実際に 2 件削除 / Copy params ガード）

### Phase 3: 復元タブ（バックアップ削除） ✅ 完了（2026-10-08）
- [x] `RestoreTabViewModel.cs`: `SelectedBackups` + 一括削除 + 未選択メッセージ
- [x] `RestoreTabView.xaml(.cs)` / `.axaml(.cs)`: 複数選択 + SelectionChanged（Avalonia は ListBox のため `SelectionMode="Multiple"`）
- [x] UiText: キー 2 追加
- [x] 検証: UIA（コピーした `sfui-backup-e2e-data` で 2 件削除。原本は不可侵）: WPF `sfui-restore-bulk-check.ps1` **7/7 PASS** / Avalonia `sfui-restore-bulk-avalonia.ps1` **7/7 PASS**（確認「2 selected backups」→「Deleted 2 backup(s) (0 failed)」→ 4→2 件 / 未選択ガード）

### Phase 4: 仕上げ ✅ 完了（2026-10-08）
- [x] PLAN.md 更新、本ドキュメントのステータス更新
- [x] 全テスト（558 緑）+ 両アプリの smoke（WPF/Avalonia exit 0）
- [x] 既存 UIA チェックの回帰: `sfui-orgmanage-ui-check.ps1` **24/24 PASS** / `sfui-backup-ui-check.ps1` **21/21 PASS**
  - 両スクリプトのウィンドウ検索を v0.14.0 のオーナー廃止に合わせてトップレベル方式へ修正（`FindTopWindows`）

### 実装中に発見・修正した副次問題（記録）
- **Avalonia `MessageDialog` が完全に動作しない重大バグ**: `AvaloniaXamlLoader.Load(this)` では Avalonia の XAML コンパイラが生成する x:Name フィールド（MessageText / ButtonPanel / InputBox）が null のままで、すべての確認ダイアログが NullReferenceException で無表示だった（ログアウト/削除/ログイン確認など全部）。`InitializeComponent()` 呼び出しに修正（`OrgInfoExportView` と同じ方式）。
- `OrgRecordCompareServiceTests` の並列フレーク（言語依存文字列）→ `[Collection("Localization")]` 参加で恒久修正。
- Avalonia の差（実測）: ListBox の複数選択は `SelectionMode="Multiple"`（`Extended` は無い）/ DataGrid は `Extended` が有効。UIA: DataGridRow に SelectionItemPattern なし（マウス必要）、ListBoxItem にはあり。モーダルを開くボタンの UIA Invoke は COM タイムアウトするため実マウスクリックを使用。ダイアログはメインウィンドウ所有（TopLevelAccessor）のため UIA 検索はメイン配下も対象にする。
- `--seed-samples` は WPF 専用（Avalonia では GUI が開くだけ）。E2E シードは WPF exe で実行。

## 6. 検証計画（詳細）

1. **単体テスト**（`tests/SfUi.Tests`）
   - `HistoryViewModelTests` / `RestoreTabViewModelTests`: 実ストア（一時ディレクトリ）+ `IDialogService` / `IClipboardService` / `IUiDispatcher` フェイクで
     一括削除の件数・確認文言・ガードメッセージを検証。
   - VM が sf CLI に依存する組織管理は UIA で検証（既存方針と同じ）。
2. **UIA チェックスクリプト**（`C:\SfUiDemo`、英語 UI へ一時切替の既存パターン）
   - 複数選択: 先頭行をクリック → `Shift+Down`（WPF は `SelectionItemPattern.AddToSelection` も可）。
   - 疎通テスト: 2 組織を選択 → 実行 → サマリ「2 OK / 0 failed」+ 2 行の Connection 列更新を検証。
   - 単一ガード: 2 選択で「既定に設定」→ 状態バーのメッセージ検証（実行されない）。
   - ログアウト: 2 選択 → 確認ダイアログに件数が出ることを検証 → **キャンセル**（実行しない）。
3. **回帰**: 既存 552+ テスト / `--smoke` / `sfui-orgmanage-ui-check.ps1` / `sfui-backup-ui-check.ps1`。

## 7. 対象外・決定事項の記録

- 組織コンボ（上部）とグリッドの「現在行」は従来どおり 1 件の意味を維持（ヘルス/棚卸しタブの対象組織と同期）。
- 一括操作は順次実行のみ（並列化しない）。
- チェックボックス列は導入しない（Extended 選択のみ）。
- 履歴の再実行（ダブルクリック）・各ウィンドウの行リンクは行固有操作として対象外。
