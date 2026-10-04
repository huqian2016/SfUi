# MSIX パッケージ（Microsoft Store 提出用）

Partner Center で予約済みの ID（`AppxManifest.xml` の値と**完全一致**が必須）:

| 項目 | 値 |
|---|---|
| Name | `HKS.SfUi` |
| Publisher | `CN=F23B2630-3909-43D6-9071-F53B9E511923` |
| PublisherDisplayName | `HKSテック株式会社` |
| Package Family Name | `HKS.SfUi_m66ayswnkrx7j` |

## ビルド

```powershell
# リポジトリ ルートで実行
.\packaging\Make-Msix.ps1                 # publish → layout → dist\SfUi_x.y.z.0_x64.msix
.\packaging\Make-Msix.ps1 -SkipPublish    # 既存の dist\SfUi.exe を再利用
```

- バージョンは `src/SfUi.App/SfUi.App.csproj` の `<Version>` から 4 桁に自動変換（例: `0.2.0` → `0.2.0.0`）。**提出ごとに前回より大きい値が必須**
- 使用ツール: `makeappx.exe`（Windows SDK。`C:\Program Files (x86)\Windows Kits\10\bin\<ver>\x64` を自動検索）

## アイコン（EXE / タスクバー）

- EXE アイコン = `src\SfUi.App\SfUi.ico`（`<ApplicationIcon>` で埋め込み + WPF `Resource` でウィンドウ / タスクバーにも適用）
- 生成・更新: `\packaging\New-Icon.ps1`（既定ソース = `Assets\Square150x150Logo.png` → 16〜256px のマルチサイズ ICO）
  - 本番ロゴに差し替える場合は **512px 以上の正方形 PNG** を `-SourcePng` で指定すると 128/256px が綺麗になります

## ローカル動作テスト（署名不要）

1. Windows の**開発者モード**を ON（設定 → システム → 開発者向け → 開発者モード）
2. 登録して起動確認:

```powershell
.\packaging\Make-Msix.ps1 -SkipPublish -Register
# 起動（エクスプローラーからでも可）
explorer.exe "shell:appsFolder\HKS.SfUi_m66ayswnkrx7j!SfUi"
# テスト後に削除
.\packaging\Make-Msix.ps1 -Unregister
```

- ⚠️ パッケージ版はインストール先が読み取り専用のため、データフォルダは `%APPDATA%\SfUi` に作成されます（アプリ側で自動フォールバック。ポータブル版とは別データになります）

## Partner Center へのアップロード

1. 申請（Submission）の「**パッケージ**」ページに `dist\SfUi_<version>_x64.msix` をアップロード（**.msix の直接アップロード可**）
2. 「ストアの登録情報」: 説明（日英）、スクリーンショット 1 枚以上（1366×768 以上推奨）、カテゴリ = 開発者ツール、価格 = 無料、年齢レーティング（IARC）
3. 「プロパティ」: **「Salesforce CLI (sf) のインストールと組織の認証が必要」**を明記。サポート URL = GitHub リポジトリ
4. 提出 → 認定（通常 1〜3 営業日）→ 公開時に **Microsoft が再署名**（SmartScreen 警告ゼロ・証明書不要）

## 注意事項

- `Assets\*.png` は**仮ロゴ**（仮色 #1B4F8A + "Sf" / "SfUi"）。本番前に差し替え推奨:
  - `Square44x44Logo.png`（44×44）、`Square150x150Logo.png`（150×150）、`StoreLogo.png`（50×50）、`Square44x44Logo.targetsize-44_altform-unplated.png`、`Square44x44Logo.targetsize-24_altform-unplated.png`
- **内蔵評価キーは Store 公開前に除去/差し替えを推奨**
- 版番号は 4 桁（Major.Minor.Build.Revision）。更新のたびに `Version`（csproj）を上げて再ビルド → 再提出
- GitHub のポータブル配布（exe / zip）とは併存可能（データ場所だけ異なる: Store 版 = `%APPDATA%\SfUi`）

## macOS パッケージ（Avalonia 版、`SfUi.app`）

macOS 上で実行:

```bash
# Apple Silicon
bash packaging/make-mac-app.sh osx-arm64
# Intel
bash packaging/make-mac-app.sh osx-x64
```

- `dotnet publish`（self-contained）→ `dist/mac/SfUi.app`（`Contents/MacOS` + `Info.plist` + `SfUi.icns`）+ 配布用 zip を作成
- バージョンは `src/SfUi.Avalonia/SfUi.Avalonia.csproj` の `<Version>` から自動取得
- icns は `sips` / `iconutil` で `src/SfUi.App/Resources/SfUi.ico` から生成（ツールが無い環境ではスキップ）
- 署名 / 公証は環境変数を設定した場合のみ実行（`SFUI_CODESIGN_IDENTITY` / `SFUI_NOTARIZE_APPLE_ID` / `SFUI_NOTARIZE_TEAM_ID` / `SFUI_NOTARIZE_PASSWORD`）
- データ フォルダは実行ファイル隣接を優先し、書込不可時は `~/Library/Application Support/SfUi` にフォールバック

## CI（GitHub Actions）

`.github/workflows/ci.yml`（push / PR / 手動実行）:

1. `Test (windows-latest / macos-latest)`: `dotnet test tests/SfUi.Tests/SfUi.Tests.csproj`（Core + Presentation の 395 テスト）
2. `Build Avalonia app (windows-latest / macos-latest)`: `dotnet build src/SfUi.Avalonia/SfUi.Avalonia.csproj`
3. `Package .app (macos-latest)`: `bash packaging/make-mac-app.sh osx-arm64` → パッケージ済み実行ファイルで `--smoke`（起動 / 言語切替 / exit 0）→ zip を workflow artifact にアップロード（署名 / 公証は環境変数未設定のためスキップ）

