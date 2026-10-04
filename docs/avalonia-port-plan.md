# SfUi → Avalonia 移植 設計調査（Windows / macOS 両対応）

最終更新: 2026-10-04 / ステータス: Phase D 進行中（全タブ + AI パネル + Compare Orgs 移植済み。次: Org Info / Data I/O ウィンドウ）

## 1. 目的とスコープ

- **目的**: SfUi を Windows 専用（WPF）から **Windows / macOS の両方で動作するデスクトップ アプリ**へ移植するための設計調査。
- **調査対象**: 現行リポジトリ（v0.9.1 / SfUi.Core + SfUi.App + SfUi.Tests）の全ソース。
- **結論（要約）**:
  - **移植は現実的**。`SfUi.Core` はもともと WPF 非依存で作られており、**プラットフォーム依存は 4 ファイルに集中**（`SfCliRunner` / `ToolLauncherService` / `AppPaths` / `AppSettingsStore` の既定値・文言）。
  - `SfUi.App` は UI 全面（30 XAML + コードビハインド）を Avalonia で書き直すが、**ViewModel（42 ファイル / 約 10,800 行）はほぼそのまま流用可能**（WPF 依存は MessageBox / ファイル ダイアログ / Dispatcher の 3 系統のみ）。
  - 唯一の実質的な再設計ポイントは「**動的に DataGrid 列を生成する 5 ビュー**」と「**編集可能 ComboBox（フォルダ コンボ）**」、「**行ハイライトの DataTrigger 群（33 箇所）**」。
- **非スコープ**: 実装作業そのもの、Microsoft Store 以外の配布チャネル設計の詳細、Avalonia 以外の選択肢（MAUI 等）の比較。

## 2. 現状インベントリ

### 2-1. プロジェクト構成

| プロジェクト | TFM | 依存 | 備考 |
|---|---|---|---|
| `src/SfUi.Core` | `net9.0`（OS 非依存） | Microsoft.Extensions.DI.Abstractions / System.Text.Encoding.CodePages | サービス 30 ファイル。`System.Windows.*` / `Microsoft.Win32` / Registry 参照 **なし**（確認済み） |
| `src/SfUi.App` | `net9.0-windows` | **WPF (`UseWPF`)** / AvalonEdit 6.3.1.120（Windows 専用 lib）/ CommunityToolkit.Mvvm 8.4.0 / MS.DI | ビュー 30 XAML + コードビハインド 30 / VM 42 ファイル（10,799 行） |
| `tests/SfUi.Tests` | `net9.0` | xUnit | 387 テスト。Windows 依存は一部（後述 2-4） |

### 2-2. WPF 固有要素の使用数（`src/SfUi.App` 実測）

| 要素 | 数 | 主な場所 |
|---|---|---|
| `UpdateSourceTrigger=PropertyChanged` | 50 | 全ビュー（機械的削除で対応） |
| `MessageBox.Show` | 14 | VM 10 ファイル（ApiConsole / Command / DataImport / DataIo / OrgManage / Restore(3) / Settings / History(2) / QuickPanel / Deploy） |
| `SaveFileDialog` / `OpenFolderDialog`（Microsoft.Win32） | 8 / 2 | VM 10 ファイル（Apex / CompareOrgs / DataExport / DataImport / Deploy / Log / Main / MigrationInventory / Settings / Soql） |
| `Dispatcher.*`（`Application.Current.Dispatcher` 含む） | 18 | VM の `OnUi` ヘルパー / `AiChatView.BeginInvoke` / `App` スモークの `Dispatcher.Yield` |
| `XamlReader.Parse`（動的テンプレート） | 3 | `BackupRecordsWindow` / `OrgInfoSectionView` / `RecordAccessView` |
| コードによる DataGrid 動的列生成 | 5 ファイル | `CompareCategoryView`(4) / `BackupRecordsWindow`(4) / `FieldAccessView`(4) / `OrgInfoSectionView`(4) / `RecordAccessView`(8) |
| `DataTrigger` / `Style.Triggers` | 33 / 25（11 ファイル） | MainWindow / AiChatView / BackupCompareRecordsWindow / CompareCategoryView(+cs) / CompareTabView / HistoryView / OrgInfoCustomTabView / OrgInfoFieldsView / OrgInfoSectionView / OrgManageWindow |
| `RelativeSource` | 34 | グリッド内ボタン → DataContext のコマンド委譲など |
| `ElementName` バインド | 6 | – |
| `pack://application:,,,/SfUi.ico`（Window アイコン） | 8 | MainWindow + 全 7 ウィンドウ |
| `BooleanToVisibilityConverter` | 約 20 ファイル | ほぼ全ビュー（Avalonia では bool → IsVisible 直接バインドで不要） |
| `KeyDown` / `PreviewKeyDown` | 19 / 9 | ショートカット（MainWindow: Ctrl+1..9 / F5）、Enter 実行など |
| `System.Windows.Input` / `Media` 参照（コード） | 11 ファイル / 1 | `CompareCategoryView.xaml.cs` が `Style`/`Brush` をコードで構築 |
| `FontFamily="Consolas"` | 20 | クエリ入力・ログ・結果グリッド |
| AvalonEdit | 2 ビュー | `ApexView` / `SoqlView`（+ コードビハインドのハイライト設定） |
| `DataGrid` | 21（17 ビュー） | OrgManageWindow(3) / RestoreTabView(2) / DataImportView(2) / 他 14 |
| `ListBox` / `ListView` / `TabControl` / `ToggleButton` / `ProgressBar` | 18 / 3 / 11 / 5 / 2 | – |
| `ItemContainerStyle` / `DataGrid.RowStyle` | 4 / 10 | 選択記憶・UIA 名・差分ハイライト |
| コードでの `Window` 構築 | 1 | `Views/InputBox.cs`（簡易入力ダイアログ） |

### 2-3. `SfUi.Core` のプラットフォーム依存（全量）

| ファイル | 現状（Windows） | 影響 |
|---|---|---|
| `Services/SfCliRunner.cs` | ①`ResolveSfPath`: 明示パス → `SFUI_SF_PATH` → `%ProgramFiles%\sf\bin\sf.cmd` → `PATH` を `;` 分割し `sf.cmd`/`sf.exe` を探索。②実行: **`cmd.exe /d /s /c` 経由**（.cmd は CreateProcess 不可のため）+ Windows 引用符規則 `QuoteArgument` / `ToCmdArguments`（テスト済み） | **要改修（中）**。macOS: `sf`（拡張子なし・シェル スクリプト）を `/usr/local/bin`・`/opt/homebrew/bin`・`PATH`（`:` 区切り）から解決し、**直接起動 + `ProcessStartInfo.ArgumentList`（POSIX quoting は .NET が処理）**。Windows 用 quoting 関数群は温存しテストも維持 |
| `Services/ToolLauncherService.cs` | `wt.exe`（自動検出: `%LocalAppData%\Microsoft\WindowsApps` → PATH）/ `powershell.exe` / `cmd.exe` / `wsl.exe` / `explorer.exe [ /select ]` / `code.cmd`（`%LocalAppData%\Programs\Microsoft VS Code\bin` → PATH）/ ブラウザ = `UseShellExecute=true` + URL / `ToWslPath` | **要改修（大・全面分岐）**。macOS: Terminal = `open -a Terminal <folder>`（iTerm 対応可）・Finder = `open <path>` / `open -R <path>`・VS Code = `code`（PATH or `/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code`）・ブラウザ = `open <url>`。`powershell` / `cmd` / `wsl` は **macOS では非表示**（メニューから除外）または Terminal へフォールバック |
| `AppPaths.cs` | フォールバック先 = `Environment.SpecialFolder.ApplicationData`（`%APPDATA%`）。優先順: `--data-dir` → `SFUI_DATA_DIR` → ソリューション/ポータブル隣接 → `%APPDATA%\SfUi` | **要改修（小）**。macOS の .NET では `ApplicationData` が `~/.config` になるため、macOS は **`~/Library/Application Support/SfUi` を明示**（`RuntimeInformation.IsOSPlatform`）。ポータブル（exe 隣接）はそのまま有効 |
| `Storage/AppSettingsStore.cs` | `SfExecutablePath` / `WindowsTerminalPath`（"Windows Terminal (wt.exe)" 文言）/ `VsCodePath`（"code.cmd" 文言） | **要改修（小）**: 既定値なし・自動検出のため実装変更は文言のみ（両言語のキー改訂） |
| `CsvParser.cs` | Shift-JIS 判定 = `System.Text.Encoding.CodePages`（`CodePagesEncodingProvider`） | **変更なし**（net9.0 lib はクロスプラットフォーム。macOS での `GetEncoding(932)` を CI/スモークで確認） |
| その他 26 サービス | Salesforce CLI ラッパー / REST / SOAP / JSON ストア / 履歴 / AI / ローカライズ | **変更なし**（OS API 非依存） |
| テスト（387 件） | `sf.cmd` パス・`QuoteArgument`・`ToCmdArguments`・`ResolveSfPath` の一部・`C:\` パス前提が数件 | **要調整（小）**: プラットフォーム分岐後の期待値を OS 別に（または抽象化を差し替えて検証） |

### 2-4. その他の Windows 依存（コード外）

- 配布: `packaging/Make-Msix.ps1`（MSIX / Windows SDK `makeappx`）→ macOS は **`.app` バンドル + `.dmg`** に別途対応。
- 検証スクリプト: `sfui-*.ps1`（UIA / PrintWindow / DWM）→ **Windows 専用**。macOS は `screencapture` + Accessibility API（osascript）等に置換 or 手動チェック。
- アイコン: `SfUi.ico` → macOS は `.icns`（`iconutil`）を追加生成。

## 3. 移植方針（推奨アーキテクチャ）

### 3-1. プロジェクト戦略

```
SfUi.sln
├─ src/SfUi.Core          （変更: プラットフォーム抽象化のみ / 両 OS でビルド・テスト）
├─ src/SfUi.Presentation  （新規: 全 ViewModel + UI サービス interface。UI フレームワーク非依存）
├─ src/SfUi.App           （現行 WPF。当面維持し、移行完了まで並存。Core + Presentation を参照）
├─ src/SfUi.Avalonia      （新規: net9.0 / Avalonia 11.x。Windows / macOS 共通 UI の本命）
└─ tests/SfUi.Tests       （現行。Core / Presentation 分はそのまま両 OS で実行）
```

- **採用理由**: Core が WPF 非依存のため、WPF 版を壊さず Avalonia 版を並行開発できる。`CommunityToolkit.Mvvm` は Avalonia でもそのまま使える（`ObservableObject` / `RelayCommand`）。
- **ViewModel も共有する**: VM を `SfUi.Presentation` に集約し、WPF 版 / Avalonia 版の両方から参照する（⇒ 13 章）。ロジックの修正は常に 1 箇所で完結する。
- 移行完了後、WPF 版を削除して `SfUi.Avalonia` を `SfUi.App` にリネームするのが最終形。

### 3-2. ViewModel の流用と抽象化

VM の WPF 依存は 3 系統のみ。**VM は新設の `SfUi.Presentation`（UI フレームワーク非依存）へ移動し、`IDialogService` などのインターフェースも同プロジェクトに置く。実装は WPF 版 / Avalonia 版がそれぞれ持ち、DI 登録（composition root）の 1 箇所だけで差し替える。** これにより VM・ロジックの修正は常に 1 箇所（Presentation / Core）で完結する。

| 抽象化 | 置き換え対象 | 実装（Avalonia 側） |
|---|---|---|
| `IDialogService`（Confirm / Info / Input） | `MessageBox.Show(owner, …)` 14 + `InputBox.Show` 1 | `Window.ShowDialog`（自前）+ 確認は `MessageBox.Avalonia` 等を採用可 |
| `IFilePickerService`（SaveFile / PickFolder） | `SaveFileDialog` 8 / `OpenFolderDialog` 2 | `TopLevel.StorageProvider.SaveFilePickerAsync` / `OpenFolderPickerAsync` |
| `IUiDispatcher`（Invoke / Post） | `Application.Current.Dispatcher`・`OnUi` 18 | `Dispatcher.UIThread.Invoke/Post` |
| `IWindowOpener`（既存の Factory 群） | `*WindowFactory`（7 種） | ほぼそのまま（`Window.Owner` / `Show()` は Avalonia でも同等） |

> 補足: VM の `MessageBox` は「確認」目的が大半で、`ConfirmPolicy`（dangerous のみ確認）と組み合わせているため、抽象化しても呼び出し側の変更は機械的。

### 3-3. XAML の移植方針（機械置換 + 再設計の区分）

| 分類 | 内容 | 対応 |
|---|---|---|
| **A. 機械置換**（大半） | `UpdateSourceTrigger` 削除（Avalonia は即時バインド）・`BooleanToVisibility` → `IsVisible` へ bool バインド・`Mode=TwoWay`/`ElementName`/`RelativeSource AncestorType` はほぼ互換・`WindowStartupLocation`/`ToolTip`/`Grid`/`DockPanel`/`TabControl` 等は同等 | 正規表現 + 手作業で置換 |
| **B. 書き換え（要再設計）** | 動的 DataGrid 列（5 ビュー）・`XamlReader` テンプレート（3 箇所）・DataTrigger 行ハイライト（10 箇所）・コードでの `Style`/`Brush` 構築（CompareCategoryView）・`InputBox`・編集可能 ComboBox | 下記 3-4 / 4 章の個別設計 |
| **C. OS 依存** | ツール ランチャー・sf パス・データルート・アイコン（ico→icns）・UIA 検証スクリプト | 2-3 / 6 章 |

### 3-4. 要注意の再設計ポイント

1. **編集可能 ComboBox（フォルダ切替）**: WPF の `IsEditable="True"` + `Text` バインド + `SelectionChanged/KeyDown/LostFocus` コードビハインド → **Avalonia の ComboBox は編集非対応**（11.x 時点）。`AutoCompleteBox`（Avalonia.Controls）へ置換し、`Text`/`SelectionChanged` 相当のイベントへ移植（**要検証**）。
2. **動的 DataGrid 列**: `DataGridTextColumn` 生成コード（WPF 型）→ Avalonia `DataGridTextColumn`/`DataGridTemplateColumn` へ書き換え。`XamlReader.Parse` のテンプレートは **`FuncDataTemplate` をコードで構築**（実行時 XAML ローダーは AOT/速度面で非推奨）。
3. **行ハイライト（DataTrigger 33）**: Avalonia に `DataTrigger` はない → ①行 VM に `IsDiff`/`RowClass` を持たせ `Classes` をバインディング + `Style Selector="DataGridRow.diff"`、または ②`IValueConverter` で Background を返す方式に統一。
4. **`{loc:Tr}` マークアップ拡張**: Avalonia 版 `MarkupExtension`（`Avalonia.Markup.Xaml.MarkupExtension`）として同等実装（インデクサ + `INotifyPropertyChanged` + `Binding.IndexerName` 更新のパターンはそのまま移植可）。
5. **アイコン ジオメトリ**: `Icons.xaml` の `StreamGeometry x:Key="…">F1 M…` は **Avalonia でもパス mini-language をサポート**（`F0`/`F1` プレフィックス含む想定だが **要検証**）。NG の場合は文字列 → `Geometry.Parse` に変換するスクリプトで対応。

## 4. ビュー別 移植表（30 ビュー + MainWindow）

凡例: 難易度 = 低（機械置換）/ 中（部分書き換え）/ 高（再設計）

| ビュー（XAML 行数） | WPF 固有のポイント | 対応方針 | 難易度 |
|---|---|---|---|
| `MainWindow.xaml`(323) | アイコン ボタン スタイル（IconButton/IconToggleButton）・編集可能 ComboBox・`PreviewKeyDown` ショートカット・8 アイコン参照 | スタイル → Avalonia `Style`（Selector + Classes）。フォルダ コンボ → AutoCompleteBox。ショートカットは `KeyEventArgs`/`KeyModifiers` へ | 中 |
| `OrgManageWindow`(268) | DataGrid ×3・DataTrigger（ProgressBar 色）・`RelativeSource Window` コマンド委譲 | DataTrigger → スタイル/コンバータ。委譲は `$parent[Window]` へ | 中 |
| `SettingsView`(150) | ファイル選択（3）・`ShowDialog` | IFilePickerService 経由 | 低 |
| `RestoreTabView`(132) | DataGrid ×2・`RelativeSource`・コンボ | 機械置換 | 低 |
| `CompareOrgsWindow`(65)+`CompareTabView`(123) | DataGrid.RowStyle の色ハイライト（4 色）・`RelativeSource` | 行 Classes 方式へ | 中 |
| `CompareCategoryView`(56)+cs(126) | **コードで列 + Style/Brush 構築**（WPF 型） | Avalonia 型で再実装（Brush → `IBrush`、`DataGridLength` 互換） | 高 |
| `OrgInfoWindow`(126)+周辺 5 ビュー | DataGrid ×2・検索・`XamlReader`（Section のリンク列） | リンク列テンプレートをコード ビルダーへ | 中 |
| `DataIoWindow`(46)+Export(111)/Import(102)+Access 3 ビュー | DataGrid ×4・CheckBox/コンボ・`XamlReader`（RecordAccess リンク列） | 動的列 + テンプレート書き換え | 中 |
| `BackupWindow`(33)+BackupTab(88)/Restore(132)/CompareTab(123) | ListView 選択記憶・DataGrid・行色 | 行 Classes 方式 | 中 |
| `BackupRecordsWindow`(45)+cs(77) / `BackupCompareRecordsWindow`(76)+cs(32) | **動的列 + XamlReader リンク列** | コード ビルダーへ | 中 |
| `SoqlView`(57)/`ApexView`(56) | **AvalonEdit** + ハイライト設定・Enter/Ctrl+Enter | **AvaloniaEdit** へ API 移植（ハイライト定義はほぼ同形式） | 中 |
| `HistoryView`/`LogView`/`CommandView`/`ApiConsoleView` | 一覧 + 詳細 + ボタン（DataTrigger 少） | 機械置換 | 低 |
| `AiChatView`(117) | `Dispatcher.BeginInvoke` スクロール・DataTrigger(2) | Dispatcher.UIThread / スタイル化 | 低 |
| `QuickPanelView`/`FieldAccessView`/`ObjectAccessView`/`RecordAccessView` | グリッド中心・AutomationId 多用（検証スクリプト互換のため維持） | 機械置換 + 動的列 1 箇所 | 中 |
| `DataExportView`(111) | グリッド + フィールド選択 | 機械置換 | 低 |
| `DeployView`(98) | ログ表示 + ファイル | 機械置換 | 低 |
| `OrgInfoMySettingsView`(104) | グリッド + コードビハインド小 | 機械置換 | 低 |
| その他（ApiConsole/Command 等 50 行未満） | – | 機械置換 | 低 |

## 5. Core 改修点一覧（まとめ）

| # | ファイル | 改修内容 | 種別 | 影響 |
|---|---|---|---|---|
| 1 | `SfCliRunner.cs` | sf 解決（mac: `sf` / PATH `:` / homebrew + `/usr/local`）と起動方式（cmd.exe 経由 → 直接 + ArgumentList）のプラットフォーム分岐。`QuoteArgument`/`ToCmdArguments` は Windows 専用として維持 | 改修 | 高（全機能の基盤） |
| 2 | `ToolLauncherService.cs` | ターミナル / フォルダ / VS Code / ブラウザの macOS 実装追加。`wt/powershell/cmd/wsl` は macOS で非表示（VM とメニューの出し分け） | 改修 | 中 |
| 3 | `AppPaths.cs` | macOS の既定データ ルートを `~/Library/Application Support/SfUi` に | 改修 | 小 |
| 4 | `AppSettingsStore.cs` + `UiText.*` | 設定文言（wt.exe / code.cmd 前提）の一般化 + OS 別既定の説明 | 改修 | 小 |
| 5 | `SfUi.Core.csproj` | 変更なし（`net9.0` のまま）。CI で macOS ビルド/テストを追加 | 設定 | 小 |
| 6 | テスト | `sf.cmd`・`C:\` 前提 4〜6 箇所をプラットフォーム抽象化経由に | 改修 | 小 |

新規（Core に置く共通インターフェース案）:

```csharp
// SfUi.Core/UI/ISfUiUiServices.cs（案）
public interface IDialogService
{
    Task<bool> ConfirmAsync(string message, string caption);      // Yes/No
    Task ShowInfoAsync(string message, string caption);
    Task<string?> PromptAsync(string title, string prompt, string defaultValue = "");
}

public interface IFilePickerService
{
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string filter);
    Task<string?> PickFolderAsync(string title, string? initialDirectory);
}

public interface IUiDispatcher
{
    void Post(Action action);
    T Invoke<T>(Func<T> func);
}
```

## 6. 新規プロジェクト案（SfUi.Avalonia）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <AssemblyName>SfUi</AssemblyName>
    <ApplicationIcon>SfUi.ico</ApplicationIcon> <!-- Windows 用。macOS は .icns を別途生成 -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.*" />
    <PackageReference Include="Avalonia.Controls.DataGrid" Version="11.*" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.*" Condition="'$(Configuration)'=='Debug'" />
    <PackageReference Include="AvaloniaEdit" Version="11.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SfUi.Core\SfUi.Core.csproj" />
  </ItemGroup>
</Project>
```

- アセット: アイコンは `avares://` / `EmbeddedResource` へ。`pack://application:,,,/SfUi.ico`（8 箇所）は差し替え。
- ダイアログは自前実装 or `MessageBox.Avalonia`（MIT）を 1 つだけ採用（依存を増やしたくない場合は自前 `ConfirmWindow`）。
- `AvaloniaUseCompiledBindingsByDefault` はリスク（`RelativeSource`/インデクサ バインドの記法差）を考慮し、**最初は false（リフレクション バインド）で移植し、後で段階的に有効化**を推奨。

## 7. macOS 固有の検討事項

| 項目 | 対応 |
|---|---|
| sf CLI | Homebrew（`/opt/homebrew/bin/sf` / `/usr/local/bin/sf`）または npm インストール。実行は直接起動（`.cmd` 無し） |
| データ ルート | ポータブル（exe 隣接）優先、書込不可時 `~/Library/Application Support/SfUi` |
| アプリ バンドル | `dotnet publish -r osx-arm64/osx-x64` → `.app` を組立て（`Info.plist` + `Contents/MacOS`）+ 任意で `codesign --deep` / 公証（配布時）。Apple Silicon は arm64 ビルド |
| アイコン | `SfUi.ico` → `SfUi.icns`（`iconutil` / スクリプト変換） |
| ショートカット | `Ctrl+Enter` / `F5` / `Ctrl+1..9` → macOS では `Cmd+…` を併設（`KeyModifiers.Meta`）。UI 表記（`Common_Shortcut` 系）の OS 別出し分け |
| ツール ランチャー | Terminal.app / iTerm / Finder / `code` / `open <url>`。`powershell`・`cmd`・`wsl` は非表示 |
| フォント | `Consolas`（20 箇所）→ `Menlo` / `SF Mono` フォールバック（`FontFamily="Consolas, Menlo, monospace"` 相当を共通リソース化） |
| 検証 | UIA スクリプトは Windows 専用のため、macOS は ①手動チェックリスト + `screencapture` スクショ、②`dotnet test`（Core）、③スモーク（`--smoke`）の実行で代替 |

## 8. 段階的移行手順（案）

| フェーズ | 内容 | 目安 |
|---|---|---|
| A | Core のプラットフォーム抽象化（SfCliRunner / ToolLauncher / AppPaths / 設定文言）+ **ViewModel を `SfUi.Presentation` へ抽出**（MessageBox / ダイアログ / Dispatcher の interface 化）+ テスト調整 | 2〜3 日 |
| B | `SfUi.Avalonia` 骨組み（DI / テーマ / Icons / `{loc:Tr}` / IDialogService / IFilePicker / Dispatcher）+ MainWindow + ツール ランチャー + 設定 | 2〜3 日 |
| C | 小〜中ビュー（History / Log / Command / ApiConsole / Soql / Apex / QuickPanel / AiChat / Deploy） | 2〜3 日 |
| D | グリッド大物（Data I/O + アクセス 3 タブ / Org Info 5 ビュー / Compare Orgs）※動的列の再設計含む | 4〜6 日 |
| E | Backup（3 タブ + 2 ウィンドウ）/ Org Manage（3 タブ） | 3〜4 日 |
| F | macOS 固有（.app 化 / icns / ショートカット / フォント / 検証スクリプト）+ README / スクショ | 2〜3 日 |

**合計目安: 約 2〜3 週間（1 人）**。Core のロジックとテスト 387 件がそのまま資産になるため、UI 移植が主工程。

## 9. 検証計画

1. **Core テスト（387 件）両 OS 実行**: GitHub Actions `matrix: [windows-latest, macos-latest]` で `dotnet test`。
2. **スモーク移植**: `--smoke` / `--smoke-orginfo` / `--smoke-orgmanage` などの起動スモークを Avalonia 版でも維持（`Dispatcher.Yield` → `Task.Delay` 等へ）。
3. **スクリーンショット**: 既存のデモ データ + モック REST（`C:\SfUiDemo\mock-salesforce.ps1`）は Windows 専用のため、macOS 用に Python/Node の簡易モック or `dotnet` 製ミニ サーバーへ移植して同一データで比較。
4. **自動 UIA**: Windows は既存 `sfui-*.ps1` を継続。macOS は手動チェックリスト（主要 3 タブ × 3 ウィンドウ程度）。
5. **Keyboard / ダイアログ / ファイル保存 / ツール ランチャー** の OS 別マニュアル確認。

## 10. リスク・要検証リスト

| # | 項目 | 内容 | 対応 |
|---|---|---|---|
| 1 | 編集可能 ComboBox | Avalonia に `IsEditable` なし → `AutoCompleteBox` へ | 検証 → 置換 |
| 2 | `StreamGeometry` の `F0`/`F1` プレフィックス | Avalonia のパス解析で有効か | 実機確認。NG なら変換スクリプト |
| 3 | DataGrid 細部 | `CanUserAddRows`/`EnableRowVirtualization` 等プロパティ不在・`RowStyle` → 行 Classes | 方式統一 |
| 4 | AvaloniaEdit 互換 | ハイライト定義（`.xshd`）・検索/スクロール API | サンプル移植で先行検証 |
| 5 | 動的列 + テンプレート | `XamlReader` 3 箇所 → コード ビルダー | 設計済み（4 章） |
| 6 | 実行時 XAML ロード | 動的テンプレートを残す場合 `AvaloniaRuntimeXamlLoader` | 原則コード化で回避 |
| 7 | mac 配布 | 署名・公証・単一 exe との両立（`.app` 内バイナリ） | フェーズ F で設計 |
| 8 | Shift-JIS | macOS での `CodePagesEncodingProvider` 動作 | CI テストで確認 |
| 9 | ショートカット互換 | Windows のユーザー慣習（F5 / Ctrl+…）と macOS（Cmd） | OS 別キーマップ + 文言 |
| 10 | MSIX ストア配布 | 既存の Store 提出は WPF 版のみ対象 | Avalonia 版は当面 GitHub Releases（.exe / .dmg） |

## 11. 参考（現行資産の再利用可否）

| 資産 | 再利用 |
|---|---|
| `SfUi.Core` サービス 30 / モデル / ストア / ローカライズ辞書（967 キー ×2 言語） | **そのまま**（プラットフォーム抽象化 4 ファイルのみ） |
| ViewModel 42 ファイル（10,799 行） | **約 95% そのまま**（ダイアログ / ファイル / Dispatcher を抽象化後、`SfUi.Presentation` へ移動して WPF / Avalonia 共有） |
| `UiText` / `CsvParser` / `CsvExporter` / `AtomicJsonFile` / REST / SOAP | **そのまま** |
| 387 テスト | **そのまま**（数件 OS 別化） |
| スモーク CLI（`--smoke-*`） | 引数・ログ形式を維持して移植 |
| UIA 検証スクリプト（Windows） | Windows 版のみ継続 |
| スクリーンショット パイプライン | モック REST + デモ データは流用（スクリプトは Windows 専用 → mac は別途） |

## 12. ビルド / 配布モデル（クロス publish と CI）

### 12-1. 結論

- **macOS 実機でのビルドは必須ではない**。.NET の**クロス publish** により、**Windows から macOS 用バイナリを作成できる**。
- ただし次の 2 点は macOS（または CI の macOS ランナー）が必要:
  1. **署名・公証（`codesign` / `notarytool`）** — Apple のツールチェーン。Gatekeeper を通過する配布には macOS 上での処理が必要
  2. **実機動作確認** — Finder 連携・キーボード（Cmd）・フォント・セキュリティ警告など
- 現行 **WPF 版は Windows 専用**（win-x64 ビルドは macOS では動かない）。macOS 対応は Avalonia 版で実現する。

### 12-2. publish コマンド（Windows マシンから実行可能）

```powershell
# Windows 用
#   dotnet publish src/SfUi.Avalonia -c Release -r win-x64 --self-contained true `
#     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64

# macOS 用（Apple Silicon）。Windows 上からでも作成できる
dotnet publish src/SfUi.Avalonia -c Release -r osx-arm64 --self-contained true `
  -p:PublishSingleFile=true -o dist/osx-arm64
# Intel Mac が必要なら osx-x64 も追加（.NET はユニバーサル バイナリ非対応 → 個別配布 or lipo で結合）
```

- 制約: **Native AOT はクロス OS 不可**（本アプリは未使用のため影響なし）。ReadyToRun のクロス生成も既定無効。
- 成果物は自己完結の単一ファイル → **`SfUi.app`（`Info.plist` + `.icns`）へパッケージするスクリプトを 1 本追加**（組立は Windows でも可能。署名・公証のみ macOS）。

### 12-3. CI（推奨構成）

- GitHub Actions matrix: `windows-latest` / `macos-latest`
  - 両 OS: `dotnet build` + `dotnet test`（Core / Presentation の 387 テスト）
  - Windows: 既存スモーク（`--smoke*`）+ UIA スクリプト
  - macOS: `--smoke` 実行 + `dotnet publish -r osx-arm64`（署名は証明書がある場合のみ）
- これにより**手元に Mac が無くても両 OS のビルド・テスト検証が回る**（配布物の最終確認のみ実機推奨）。

## 13. シングルソース戦略（1 か所の修正で両 OS に反映）

「ロジックは OS 非依存・修正は 1 箇所」を守るためのルール:

1. **ロジックの置き場所を 2 プロジェクトに限定**
   - `SfUi.Core`（CLI / REST / ストア / モデル）+ `SfUi.Presentation`（全 ViewModel + UI サービス interface）
   - どちらも **UI フレームワーク参照禁止・OS API 直接参照禁止**（CI でガード可: `#if WINDOWS` / `OperatingSystem.Is` / `C:\` / `.exe` 等の grep 検査）
2. **OS 差分は「interface + 実装 2 つ + DI 登録 1 箇所」に集約**
   - `ISfCliLocator` / `IShellLauncher` / `IAppPathsProvider` / `IDialogService` / `IFilePickerService` / `IUiDispatcher`
   - 実装はアプリ側の `Platform/Windows/…` と `Platform/Mac/…` に置き、**登録だけが分岐**（composition root）
3. **UI も 1 コードベース（SfUi.Avalonia）で両 OS をカバー**
   - Windows / macOS とも同一ソースを RID 違いで publish → ビューの修正も 1 箇所
   - Windows 固有の表示差（ショートカットの Ctrl/Cmd 表記など）は `PlatformInfo.ShortcutModifier` のような**実行時プロパティ 1 つ**で出し分け
4. **WPF 版の扱い**
   - 移行期間中は凍結（バグ修正のみ）。新機能は Core / Presentation / Avalonia にのみ追加 → 二重実装を作らない
   - Presentation 抽出後は、VM の修正が WPF 版にも自動で反映される
5. **テストで担保**
   - Core / Presentation のテストを**両 OS の CI で常時実行**（OS 依存の混入はここで検出）

最終形:

```
SfUi.Core / SfUi.Presentation     ← 唯一のロジック（両 OS 共用・テストも単一）
        ▲
   SfUi.Avalonia（1 ソース）
        ├─ publish -r win-x64    → Windows 版
        └─ publish -r osx-arm64  → macOS 版
```
