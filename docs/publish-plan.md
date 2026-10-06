# SfUi 公開（Publish）計画

> 対象: SfUi v0.11.0（2026-10-06 時点）
> 目的: このツールを多くのユーザー（主に Salesforce 開発者・管理者）に届けるための計画をまとめる
> 関連: `packaging/README.md`（MSIX / Store 提出）、`docs/macos-signing-notarization.md`（mac 署名・公証）、`.github/ISSUE_TEMPLATE/`（不具合報告）

---

## 1. 前提と決定事項

| 項目 | 決定 |
|---|---|
| AI キーの同梱 | **お試し用として継続する**（機能アピール優先）。DeepSeek は**上限設定済み**でコストリスクは限定的。OpenAI キーも今後**上限付きで同梱予定** |
| ターゲット | Salesforce 開発者・管理者（Windows 中心、macOS は Apple Silicon） |
| ライセンス | MIT（OSS・無償） |
| 配布チャネル | GitHub Releases（済）／ Microsoft Store（予定）／ winget ・ Homebrew Cask（予定）／ コミュニティ告知 |
| 非公式性 | Salesforce, Inc. とは無関係の非公式ツール（README・About・Store 説明に明記する） |

### 1.1 お試し AI キーの運用方針（重要）

- 同梱キーは「評価用」と位置づけ、**上限・失効は予告なく行う**旨を README / 設定画面の説明に明記する
- **上限到達・失効時の UX**: AI 応答が失敗したら「同梱の評価用キーは現在利用できません。設定 → AI で自分のキーを登録してください」と案内できるようにする（自キー設定フローは実装済み）
- **キーのローテーション手順（漏洩・異常使用時）**:
  1. プロバイダ（DeepSeek / OpenAI）の管理画面で該当キーを**失効**
  2. 新しいキーを発行し `src/SfUi.Core/DefaultAiKey.cs` を更新
  3. パッチリリース（例: v0.11.1）として配布
  - ※ キーは git 履歴にも残るため、**漏洩時は必ず失効が先**（履歴の書き換えより失効・再発行が現実的）
- 使用量の定期確認（プロバイダのダッシュボードで上限・アラートを設定）

---

## 2. 公開前チェックリスト

### 2.1 必須（ブロッカー級）

- [x] 単体テスト 443 件 / CI 6 ジョブ green（Windows・macOS）
- [x] macOS: Developer ID 署名 + 公証（CI で自動）
- [ ] **非公式・商標 Disclaimer** を追加
  - README（EN / JA）、About ウィンドウ、Microsoft Store の説明欄
  - 文言例: 「本ツールは Salesforce, Inc. とは無関係の非公式ツールです。Salesforce は Salesforce, Inc. の商標です。」
- [ ] **プライバシーポリシー**（`PRIVACY.md` + 公開 URL）
  - 内容: アプリ本体はテレメトリ送信なし／データはローカル保存（`data/`・`%APPDATA%\SfUi`・`~/Library/Application Support/SfUi`）／外部送信は AI チャット利用時のみ（入力内容が設定先 API へ送信される）／sf CLI テレメトリは無効化（`SF_DISABLE_TELEMETRY`）／削除方法
  - Store 提出にはプライバシー URL が必須
- [ ] **Windows 配布の整理**
  - 主導線は Microsoft Store（Store 再署名 = SmartScreen 警告なし）
  - GitHub 直配布（exe / portable / MSIX）は「署名なし・警告の出し方」を README に明記（既存の記載を維持）
- [ ] **お試しキーの説明**を README / 設定に追加（§1.1 の方針）

### 2.2 強く推奨

- [ ] README の**英語セクション拡充**（機能一覧・スクリーンショットを JA と同水準に）
- [ ] スクリーンショット更新（About・SOQL / Apex 補完など v0.11 の新機能）
- [ ] **Dependabot**（`.github/dependabot.yml`）+ **CodeQL**（Actions）を有効化
- [ ] **SECURITY.md**（脆弱性の報告先: ko@hks-tech-kk.com / GitHub Security Advisories）
- [ ] **CHANGELOG.md**（Releases に加えてリポジトリ内でも履歴を辿れるように）
- [ ] Intel Mac 対応の判断（対応=CI に `osx-x64` 追加／非対応=README に「Apple Silicon 専用」を明記）
- [ ] GitHub 版向けの**アップデート通知**（Releases API を照会して「新バージョンあり」を表示）

### 2.3 任意（余力があれば）

- [ ] デモ GIF / 短尺動画（機能アピール用）
- [ ] リリース自動化（tag push で 5 アセットを CI が添付）
- [ ] Store 掲載用スクリーンショットの再撮影（最新 UI）

---

## 3. 公開手順（チャネル別）

### 3.1 GitHub Releases（完了済み・今後の手順）

1. バージョン更新: `src/SfUi.App.csproj` / `src/SfUi.Avalonia.csproj` / `packaging/AppxManifest.xml`（4 桁）/ README の版数
2. `git commit "Bump version to X.Y.Z"` → `git push` → `git tag vX.Y.Z` → `git push origin vX.Y.Z`
3. Windows アセット: `dotnet publish src/SfUi.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist` → portable zip → `packaging/Make-Msix.ps1 -SkipPublish`
4. macOS: CI の `SfUi-macos-app` アーティファクトをダウンロード（署名・公証済み zip）
5. リリース作成 + アセット 5 点アップロード（過去手順: `C:\huqian\sfui-release-*.ps1`。API トークンは git credential から取得）
6. 公開後の確認: exe の `--smoke`（exit 0）

### 3.2 Microsoft Store（Windows の主導線）

- Partner Center: **HKS.SfUi / HKSテック株式会社**（予約済み ID と完全一致のマニフェスト）
- 提出物: MSIX（`dist\SfUi_x.y.z.0_x64.msix`）、説明（日英）、スクリーンショット 1 枚以上（1366×768+）、カテゴリ = 開発者ツール、年齢レーティング（IARC）、サポート URL = GitHub、**プライバシー URL**
- 説明文に「Salesforce CLI (sf) のインストールと組織の認証が必要」「非公式ツール」を明記
- 審査は通常 1〜3 営業日。公開時は Microsoft が再署名（警告ゼロ）
- 更新のたびに 4 桁バージョンを上げて再提出

### 3.3 winget（Windows 開発者チャネル）

- `microsoft/winget-pkgs` へマニフェスト PR（`wingetcreate` コマンドが便利）
- 参照先: GitHub Release の `SfUi.exe`（portable）または MSIX
- 承認後: `winget install SfUi` で導入可能

### 3.4 Homebrew Cask（macOS）

- `homebrew-cask` へ cask PR（URL = 公証済み `SfUi-x.y.z-osx-arm64.zip`）
- 承認後: `brew install --cask sfui`

### 3.5 コミュニティ告知

- **日本語圏**: Zenn / Qiita の紹介記事（使い方 + スクショ + セットアップ手順）、X（旧 Twitter）
- **英語圏**: Reddit r/salesforce、Salesforce Trailblazer Community、SFXD Discord、`awesome-salesforce` 系リポジトリへの PR
- 記事には必ず「非公式ツール」「sf CLI が必要」「お試し AI キーは上限あり」を明記

---

## 4. 段階的公開プラン

```
Phase 0: 出荷前仕上げ（今週）
  ├─ 2.1 の必須項目（Disclaimer / プライバシー / キー説明）
  ├─ 2.2 から: Dependabot + CodeQL + SECURITY.md / README EN
  └─ v0.11.1 としてリリース（or 0.12.0）

Phase 1: ソフトローンチ（1〜2 週間）
  ├─ Zenn / Qiita で紹介記事（日本語圏ファースト）
  ├─ winget 提出
  ├─ 身近な Salesforce 関係者へ先行配布 → Issue / 感想を収集
  └─ ゲート: 致命バグ 0・Issue の初動対応が回っている

Phase 2: パブリックベータ（2〜4 週間）
  ├─ Microsoft Store 提出 → 公開
  ├─ Homebrew Cask 追加
  ├─ 英語圏へ告知（Reddit 等）＋ README EN 充実版
  └─ ゲート: ストア審査通過・レビュー対応が回っている

Phase 3: 一般公開・拡大
  ├─ EN の記事・Product Hunt など
  ├─ アップデート通知 / リリース自動化などの運用強化
  └─ Intel Mac / 他言語など要望に応じて拡張
```

**原則**: 「日本語圏の Salesforce コミュニティ → winget / Store → 英語圏」の順で広げ、各フェーズでフィードバックを潰してから次へ進む。

---

## 5. 未決事項

- [ ] Windows コード署名証明書（OV / EV）を直配布用に購入するか（Store 一本化なら不要）
- [ ] Intel Mac（osx-x64）対応の有無
- [ ] アップデート通知・リリース自動化の実装時期
- [ ] Store の新規提出タイミング（Phase 0 完了後の v0.11.1 / v0.12.0 で提出）
- [ ] OpenAI プリセット + 上限付きキーの追加時期

---

## 6. 直近の ToDo（Phase 0）

1. [ ] README（EN / JA）+ About に非公式・商標 Disclaimer を追加
2. [ ] `PRIVACY.md` 作成（Store 用 URL も確保: GitHub のファイル URL で可）
3. [ ] README / 設定画面の説明に「お試しキー（上限あり）」の文言を追加
4. [ ] `.github/dependabot.yml` + CodeQL + `SECURITY.md` を追加
5. [ ] README EN セクションの拡充
6. [ ] 上記をまとめて v0.11.1 としてリリース → Store 再提出
