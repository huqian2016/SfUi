# 起動時ウェルカム画面（Welcome）実装プラン

作成日: 2026-10-05 / 対象: WPF (SfUi.App) + Avalonia (SfUi.Avalonia)

## 目的

起動時に Welcome ウィンドウを表示し、

1. 主要機能を一覧でアピールする
2. Salesforce CLI（sf コマンド）が見つからない場合にインストール案内を出す
3. 4 言語（英語 / 日本語 / 簡体中文 / 韓国語）対応を案内する

ことで、初めて使う人にも既存ユーザーにも価値がある入口を作る。

## 決定事項（ユーザー確認済み）

| 項目 | 決定 |
|---|---|
| 表示タイミング | **毎回表示**。「今後表示しない」チェックで抑制（`welcomeDismissed`） |
| 表示形式 | **モーダルの別ウィンドウ**（メインウィンドウのオーナー付き） |
| 掲載内容 | 機能一覧（アイコン付き）/ SF CLI 未検出時のインストール案内 / 検出時のパス表示 / 「使い始める」「設定を開く」ボタン / 言語案内 |
| 再表示手段 | 設定タブの「ようこそ画面を表示」ボタン |
| 不採用 | GitHub リンク、Welcome 内での言語切替 UI |

## 表示ポリシー

- `AppSettings.WelcomeDismissed`（bool、既定 false）が false の間、**起動のたびに表示**する
- Welcome 内の「今後表示しない」チェックは **トグル即 `settings.Save()`**（アプリ全体の即時保存方針に合わせる）
- 設定タブから手動で開いた場合もチェック状態は現在値を反映し、外せば次回起動から再び表示される
- 抑制して閉じた場合は `WelcomeDismissed = true` になっているため次回は表示されない

## 自動化（スモーク / E2E / スクリーンショット）保護

Welcome はモーダルのため、自動化と衝突しないよう以下を実装する。

- **自動抑制**: `--smoke`（両アプリ）、`--tab`（両アプリ）、`--open`（Avalonia）時は表示しない
- **`--no-welcome`**: 明示抑制フラグ（UIA E2E / スクリーンショット用）
- **`--welcome`**: `welcomeDismissed` でも強制表示（スクリーンショット用）
- **`--welcome-missing`**: `--welcome` + sf 未検出状態をシミュレート（未検出 UI の検証用。実行時は実際の検出を回避）
- 抑制時は **`WelcomeDismissed` を書き換えない**（CI / E2E のデータディレクトリを汚さない）

外部スクリプト（リポジトリ外）の追随:

- `C:\huqian\sfui-avalonia-probe.ps1` に `-NoWelcome` スイッチを追加
- `sfui-dataio-e2e.ps1` / `sfui-access-tabs-check.ps1` / `sfui-backup-ui-check.ps1` / `sfui-orgmanage-ui-check.ps1` / `sfui-readme-shots.ps1` の起動引数に `--no-welcome` を追加

## アーキテクチャ

```
起動（WPF App.OnStartup / Avalonia App.OnFrameworkInitializationCompleted）
  └─ ポリシー判定（smoke/tab/open/no-welcome/force/welcomeDismissed）
       └─ MainViewModel.ShowWelcome()
            └─ IAppWindowService.OpenWelcome(openSettings コールバック)
                 ├─ WPF: WelcomeWindowFactory → WelcomeWindow.ShowDialog(owner)   [モーダル]
                 └─ Avalonia: AvaloniaAppWindowService → new WelcomeWindow(vm).ShowDialog(owner)

WelcomeViewModel（SfUi.Presentation に共有実装）
  - sf 検出: SfCliRunner.ResolveSfPath(settings.SfExecutablePath)（同期・軽量）
  - 「再チェック」: ResolveSfPath → SfCliRunner.SetExecutablePath()（再起動なしで以降の実行に反映）
  - 「公式インストーラーを開く」: ToolLauncherService.LaunchBrowser()
  - 「設定を開く」: OpenSettingsRequested → MainViewModel が SelectedTabIndex=7 に切替
  - 「使い始める」/「設定を開く」: CloseRequested → ウィンドウ Close
```

- 起動トリガは **ウィンドウ表示後**（WPF: `ContentRendered` を Show 前にフック / Avalonia: `window.Opened`）。
  `OnStartup` 内でモーダルをブロック表示しない。
- `StartupOptions`（Presentation、DI シングルトン）で `SimulateSfMissing` を VM に伝える。

## 画面構成

```
┌ WelcomeWindow（820×660 固定・モーダル・中央表示）─────────────────┐
│ [✦] Welcome to SfUi                                             │
│     タグライン（機能の要約 1 行）                                 │
│                                                                 │
│ 主要機能                                                        │
│ [</>] クエリ & 開発      SOQL ・ Apex ・ デバッグログ ・ 履歴    │
│       説明 1 行                                                  │
│ [🏢] 組織管理            組織情報 ・ 比較 ・ データ入出力 ・ …   │
│ [✦] 効率化               AI ・ クイックパネル ・ ターミナル …   │
│ [🌐] 4 言語対応の案内                                           │
│                                                                 │
│ Salesforce CLI                                                  │
│  ✔ 検出済み: C:\Program Files\sf\bin\sf.cmd        ← 検出時     │
│  ⚠ Salesforce CLI（sf コマンド）が見つかりません   ← 未検出時   │
│    インストールすると…の案内                                     │
│    [公式インストーラーを開く] [再チェック]                       │
│    npm install --global @salesforce/cli                         │
│ （再チェック結果メッセージ）                                    │
├─────────────────────────────────────────────────────────────────┤
│ ☐ 今後表示しない                        [設定を開く] [使い始める] │
└─────────────────────────────────────────────────────────────────┘
```

- 機能名は既存キー（`Tab_*` / `Main_*` / `Quick_Title`）を再利用し、翻訳キーを最小化する
- アイコンは既存の `Icons.xaml` / `Icons.axaml`（Fluent）を再利用する

## 変更ファイル

| ファイル | 変更 |
|---|---|
| `src/SfUi.Core/Storage/AppSettingsStore.cs` | `WelcomeDismissed`（JSON `welcomeDismissed`、既定 false） |
| `src/SfUi.Presentation/StartupOptions.cs` | 新規（`SimulateSfMissing`） |
| `src/SfUi.Presentation/UI/IAppWindowService.cs` | `OpenWelcome(Action? openSettings)` |
| `src/SfUi.Presentation/ViewModels/WelcomeViewModel.cs` | 新規 |
| `src/SfUi.Presentation/ViewModels/MainViewModel.cs` | `ShowWelcome()` + 設定タブへの遷移コールバック |
| `src/SfUi.Presentation/ViewModels/SettingsViewModel.cs` | `OpenWelcomeCommand` |
| `src/SfUi.Core/Localization/UiText.{En,Ja,Zh,Ko}.cs` | `Welcome_*` / `Settings_ShowWelcome` キー追加 |
| `src/SfUi.App/App.xaml.cs` | フラグ解析・DI 登録・起動トリガ |
| `src/SfUi.App/Services/WelcomeWindowFactory.cs` | 新規 |
| `src/SfUi.App/Services/WpfAppWindowService.cs` | `OpenWelcome` 実装 |
| `src/SfUi.App/Views/WelcomeWindow.xaml(.cs)` | 新規 |
| `src/SfUi.App/Views/SettingsView.xaml` | ボタン追加 |
| `src/SfUi.Avalonia/App.axaml.cs` | フラグ解析・DI 登録・起動トリガ |
| `src/SfUi.Avalonia/Services/AvaloniaAppWindowService.cs` | `OpenWelcome` 実装（ShowDialog） |
| `src/SfUi.Avalonia/Views/WelcomeWindow.axaml(.cs)` | 新規 |
| `src/SfUi.Avalonia/Views/SettingsView.axaml` | ボタン追加 |
| `tests/SfUi.Tests/AppSettingsStoreTests.cs` | `WelcomeDismissed` の既定値 / 往復 / 旧ファイル互換 |

## 検証

1. `dotnet build SfUi.sln -c Debug` → 0 warnings / 0 errors
2. `dotnet test tests\SfUi.Tests\SfUi.Tests.csproj -c Debug` → 全 green（既存 396 + 追加分、4 言語キー parity 含む）
3. WPF / Avalonia `--smoke` → 自動抑制で exit 0（既存動作に回帰なし）
4. 新規データディレクトリで `launch` → モーダル表示をキャプチャ（ja / zh / ko / en）
5. `--welcome-missing` → 未検出 UI（案内 + ボタン + npm 行）をキャプチャ。検出時はパス表示を確認
6. 操作確認: 「設定を開く」→ 設定タブへ遷移 / 「再チェック」→ メッセージ表示 / 「今後表示しない」→ 再起動で非表示、設定から再表示で復帰
7. UIA スクリプト（例 `sfui-orgmanage-ui-check.ps1`）に `--no-welcome` 追加後に PASS
8. コミット + push → CI 全ジョブ green

## スコープ外

- バージョン bump / GitHub Release（リリース作業は別タスク）
- README への zh / ko 追記（従来方針どおり）
- Welcome からの言語切替 UI、GitHub リンク
