# SfUi 公開（Publish）計画

> 対象: SfUi v0.13.0（2026-10-08 時点）
> 目的: このツールを多くのユーザー（主に Salesforce 開発者・管理者）に届けるための計画をまとめる
> 関連: `packaging/README.md`（MSIX / Store 提出）、`docs/macos-signing-notarization.md`（mac 署名・公証）、`.github/ISSUE_TEMPLATE/`（不具合報告）

---

## 1. 前提と決定事項

| 項目 | 決定 |
|---|---|
| AI キーの同梱 | **お試し用として継続する**（機能アピール優先）。DeepSeek は**上限設定済み**でコストリスクは限定的。OpenAI キーも今後**上限付きで同梱予定** |
| ターゲット | Salesforce 開発者・管理者（Windows 中心、macOS は Apple Silicon） |
| ライセンス | MIT（OSS・無償） |
| 配布チャネル | GitHub Releases（済）／ **Microsoft Store（公開済み 2026-10-06: v0.10.1 / product ID `9NX0BFFHF7B1`）**／ winget ・ Homebrew Cask（予定）／ コミュニティ告知 |
| 非公式性 | Salesforce, Inc. とは無関係の非公式ツール（README・About・Store 説明に明記する） |

### 1.1 お試し AI キーの運用方針（重要）

- 同梱キーは「評価用」と位置づけ、**上限・失効は予告なく行う**旨を README / 設定画面の説明に明記する
- **上限到達・失効時の UX（実装済み）**: 内蔵キー利用中の 401 / 402 / 403 / 429 では「同梱の評価用キーは現在利用できません。設定 → AI で自分のキーを登録してください（自分のキーが優先されます）」と案内する（`AiChatClient.ShouldSuggestOwnKey` / `Ai_BuiltInKeyUnavailableFmt`）
- **キー値の難読化（実装済み）**: settings.json と同梱キーは `enc1:` 形式（XOR + Base64。`AiKeyObfuscation`）で保持し、設定画面にも enc1 のまま表示する（アプリ内部で復元して使用。平文の直貼り付けも受け付ける）。**難読化のみで、デコンパイル・通信傍受には無力**＝「うっかりコピー防止」までが目的
- **ローテーションは二段切替**:
  1. 新しいキーを発行し `src/SfUi.Core/DefaultAiKey.cs`（DeepSeek）/ `DefaultOpenAiKey.cs`（OpenAI）を更新して**先にパッチリリース**（旧バージョンの移行期間を確保）
  2. 数日後に旧キーを**失効**
  - ※ キーは git 履歴にも残るため、**漏洩時は失効が先**（履歴の書き換えより失効・再発行が現実的）
- **プロバイダ側の防衛線（必須）**: モデル制限（必要なモデルのみ）/ レート制限 / 使用量アラート / 2FA を設定。DeepSeek にキー単位の上限が無い場合は**残高が実質上限** → 専用アカウント・小額残高で運用
- **OpenAI 内蔵キーの追加手順**: 上限付きキーを発行 → `enc1:` 値を生成（下記）→ `src/SfUi.Core/DefaultOpenAiKey.cs` の `Encoded` に貼り付け → パッチリリース
  ```powershell
  $mask=[Text.Encoding]::UTF8.GetBytes('SfUi-AiKey-2026'); $p=[Text.Encoding]::UTF8.GetBytes('<APIキー>'); $b=[byte[]]$p.Clone(); for($i=0;$i -lt $b.Length;$i++){$b[$i]=$b[$i] -bxor $mask[$i%$mask.Length]}; 'enc1:'+[Convert]::ToBase64String($b)
  ```
- 使用量の定期確認（プロバイダのダッシュボードで上限・アラートを設定）

---

## 2. 公開前チェックリスト

### 2.1 必須（ブロッカー級）

- [x] 単体テスト 482 件 / CI green（Windows・macOS）
- [x] macOS: Developer ID 署名 + 公証（CI で自動）
- [x] **非公式・商標 Disclaimer** を追加（2026-10-06: README EN/JA + About ウィンドウ（WPF/Avalonia）+ Store 説明文ドラフト）
  - README（EN / JA）、About ウィンドウ、Microsoft Store の説明欄
  - 文言例: 「本ツールは Salesforce, Inc. とは無関係の非公式ツールです。Salesforce は Salesforce, Inc. の商標です。」
- [x] **プライバシーポリシー**（`PRIVACY.md` 作成済み 2026-10-06。Store 用 URL: https://github.com/huqian2016/SfUi/blob/main/PRIVACY.md）
  - 内容: アプリ本体はテレメトリ送信なし／データはローカル保存（`data/`・`%APPDATA%\SfUi`・`~/Library/Application Support/SfUi`）／外部送信は AI チャット利用時のみ（入力内容が設定先 API へ送信される）／sf CLI テレメトリは無効化（`SF_DISABLE_TELEMETRY`）／削除方法
  - Store 提出にはプライバシー URL が必須
- [x] **Windows 配布の整理**（2026-10-06: Store 公開済み・README を「Microsoft Store 推奨」導線に更新）
  - 主導線は Microsoft Store（Store 再署名 = SmartScreen 警告なし）
  - GitHub 直配布（exe / portable / MSIX）は「署名なし・警告の出し方」を README に明記（既存の記載を維持）
- [x] **お試しキーの説明**を README / 設定に追加（2026-10-06。上限・予告なく停止を明記）

### 2.2 強く推奨

- [ ] README の**英語セクション拡充**（機能一覧・スクリーンショットを JA と同水準に）
- [ ] スクリーンショット更新（About・SOQL / Apex 補完など v0.11 の新機能）
- [x] **Dependabot**（`.github/dependabot.yml`）+ **CodeQL**（`.github/workflows/codeql.yml`）を有効化（2026-10-06 追加）
- [x] **SECURITY.md**（脆弱性の報告先: ko@hks-tech-kk.com / GitHub Security Advisories）（2026-10-06 追加）
- [ ] **CHANGELOG.md**（Releases に加えてリポジトリ内でも履歴を辿れるように）
- [ ] Intel Mac 対応の判断（対応=CI に `osx-x64` 追加／非対応=README に「Apple Silicon 専用」を明記）
- [ ] GitHub 版向けの**アップデート通知**（Releases API を照会して「新バージョンあり」を表示）

### 2.3 任意（余力があれば）

- [ ] デモ GIF / 短尺動画（機能アピール用）
- [ ] リリース自動化（tag push で 5 アセットを CI が添付）
- [x] Store 掲載用スクリーンショットの再撮影（最新 UI）（2026-10-06: `dist\SfUi-Store-Screenshots-v0.11.1.zip` / en・ja 01..08）

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
- **公開済み（2026-10-06）**: v0.10.1 — [SfUi on Microsoft Store](https://apps.microsoft.com/store/detail/9NX0BFFHF7B1)（Product ID `9NX0BFFHF7B1`）
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
  ├─ Microsoft Store 提出 → 公開 ✅（2026-10-06 完了）
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
- [ ] Store の次回提出タイミング（0.11.1 を 2026-10-06 に提出済み・審査待ち。以降もリリース毎に 4 桁バージョンを上げて更新提出。掲載説明文も現行機能（マルチプロバイダー AI・4 言語 UI 等）に更新）
- [ ] OpenAI プリセット + 上限付きキーの追加時期

---

## 6. 直近の ToDo（Phase 0）

1. [x] README（EN / JA）+ About に非公式・商標 Disclaimer を追加
2. [x] `PRIVACY.md` 作成（Store 用 URL: https://github.com/huqian2016/SfUi/blob/main/PRIVACY.md）
3. [x] README / 設定画面の説明に「お試しキー（上限あり）」の文言を追加
4. [x] `.github/dependabot.yml` + CodeQL + `SECURITY.md` を追加
5. [ ] README EN セクションの拡充
6. [x] v0.12.0 としてリリース済み（2026-10-08 / GitHub release id 406292083 / 5 アセット）→ Store 再提出（0.12.0.0）はユーザー作業

> 2026-10-06 進捗: 1〜4 完了（About/README/設定の文言 + PRIVACY/SECURITY/Dependabot/CodeQL）。5（README EN 拡充）は任意・未着手。**6 完了: v0.11.1 リリース（GitHub release id 404838840 / 5 アセット）+ Partner Center 更新提出済み（審査 1〜3 営業日待ち）**。掲載文 = `dist\store-listing-0.11.1.md`、スクショ = `dist\SfUi-Store-Screenshots-v0.11.1.zip`。
> 2026-10-08 進捗: **v0.12.0 リリース**（組織情報ウィンドウの定義書エクスポート = オブジェクト / 項目 / 画面レイアウト / リストビュー / フローを xlsx + CSV 出力。WPF / Avalonia 両対応・UI E2E 検証済み・テスト 504 件）。GitHub release id 406292083 / 5 アセット（SfUi.exe + portable + MSIX + cer + notarized osx-arm64）。README（EN/JA）にスクリーンショット 2 枚（en/ja）を追加。**Store 再提出用 MSIX = `dist\SfUi-0.12.0-x64.msix`（Partner Center で 0.12.0.0 をアップロード + 掲載説明の更新推奨）**。
