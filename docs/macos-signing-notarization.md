# macOS 署名・公証（Developer ID）手順書

最終更新: 2026-10-05 / ステータス: **証明書発行済み（方法 A・別の Mac の Xcode）。次の作業 = .p12 エクスポート（§2 A-1）→ 公証用パスワード（§4）→ GitHub secrets 登録（§8）または別の Mac で §5 実行**
→ CI の署名 + 公証ステップは配線済み（`.github/workflows/ci.yml`。secrets 未登録の間は自動スキップ = 従来どおり未署名）。

## 0. 目的と現状

- **目的**: `SfUi.app` を **Developer ID 署名 + 公証（notarization）** し、ユーザーがダウンロード後に「右クリック → 開く」なしで（ダブルクリックで）起動できるようにする
- **現状（未署名）**: ブラウザーでダウンロードした zip は macOS が quarantine（隔離）属性を付けるため、初回のみ Gatekeeper がブロックする
  - 回避策（現行リリースに記載済み）: `SfUi.app` を右クリック →「開く」、または `xattr -dr com.apple.quarantine SfUi.app`
  - 参考: `curl` 等のコマンドでダウンロードした場合は quarantine が付かないため初回からダブルクリックで起動できる
- **費用**: Apple Developer Program 年会費 **¥12,980/年（日本・税込）**。証明書の発行も公証サービスもこの年会費に含まれる（公証のたびの追加課金はなし）
- **本プロジェクトの状況（2026-10-05 更新）**:
  - **Apple Developer Program の更新（Renew）が完了** — チーム ID `WSDCSNQC59`・プログラム = Apple Developer Program・登録タイプ = 個人・**更新日（有効期限）2027-10-06**・年間登録料 ¥12,980（手順は §1）
  - **Developer ID Application 証明書を発行済み（方法 A・別の Mac の Xcode）** — 秘密鍵はその Mac のキーチェーンにのみ存在するため、まず **.p12 として書き出す**（§2 A-1）
  - **次にやること**: ① .p12 エクスポート（§2 A-1）→ ② 公証用のアプリ用パスワード（§4）→ ③ GitHub secrets 登録（§8。CI 配線済みなので登録だけで署名 + 公証ビルドが有効化）または その Mac で §5 を実行
- **リポジトリ側の対応は実装済み**（追加作業なしで Step 5 を実行できる）:
  - `packaging/make-mac-app.sh` … 環境変数を設定すると `codesign`（hardened runtime）+ `notarytool` + `stapler` を自動実行（未設定時はスキップ）
  - `packaging/mac/entitlements.plist` … **.NET の JIT 許可**（`allow-jit` / `allow-unsigned-executable-memory`）。hardened runtime 下でこれが無いと署名後に起動クラッシュする（`c0641a7` で配線済み）
  - `.github/workflows/ci.yml` … `package-macos` に**署名（証明書インポート + 署名 ID 自動検出）+ 公証**ステップを配線済み（`MACOS_CERT_P12` 未登録の間は自動スキップ）

## 1. メンバーシップの更新（既存 Apple ID）

> **✅ 完了（2026-10-05）**: 下記手順で更新済み（チーム ID `WSDCSNQC59` 継続・更新日 2027-10-06・年間登録料 ¥12,980）。以下は実施記録として残す。

1. [developer.apple.com/account](https://developer.apple.com/account) に **同じ Apple ID** でサインイン
2. **Membership details** → 「**Renew Membership**（メンバーシップを更新）」→ 年額 **¥12,980** を支払う
   - iPhone の「Apple Developer」アプリからも可（アカウント → Membership → Renew）
3. 完了後も **Team ID は `WSDCSNQC59` のまま**引き継がれる
4. 事前確認: Apple ID の **2 段階認証（2FA）** が有効であること（必須要件）

> ⚠️ 10 年近く期限切れのため「Renew」ボタンが出ない/エラーになる場合は、
> [Apple Developer Support](https://developer.apple.com/contact/)（トピック: Membership）へ
> 「期限切れメンバーシップ（Team ID: WSDCSNQC59）を更新したい」と問い合わせる。

### 表示名に関する注意（任意の追加検討）

このメンバーシップは登録タイプ**「個人」**のため、Gatekeeper の確認画面には**個人名**が表示される（会社名ではない）。署名・公証の**機能は Organization と同等**。

| やりたいこと | 方法 |
|---|---|
| 右クリック問題の解消（最優先） | 個人のまま更新（最短・¥12,980/年） |
| Gatekeeper に「HKSテック株式会社」を表示 | ① Apple サポート経由で Individual → Organization 変更を申請（D-U-N-S 番号が必要）② 別の Apple ID で Organization として新規登録 |
| 両方 | まず個人で更新 → 後日サポートに Organization 変更を相談 |

## 2. Developer ID Application 証明書の発行

**方法 A: Xcode（推奨・簡単）** — **✅ 実施済み（2026-10-05・別の Mac）**
1. Xcode → Settings → Accounts → Apple ID → Manage Certificates
2. 「+」→ **Developer ID Application** → キーチェーンに秘密鍵ごと作成

**方法 B: ポータル（Xcode が無い Mac 用）**
1. Certificates, Identifiers & Profiles → Certificates → 「+」→ Developer ID Application
2. キーチェーンアクセスで CSR を作成 → アップロード
3. `.cer` をダウンロード → ダブルクリックでキーチェーンへ

**方法 C: Windows（OpenSSL。手元に Mac / Xcode が無い場合）**
1. CSR を作成（Git for Windows 同梱の OpenSSL を使用）
   ```powershell
   & 'C:\Program Files\Git\usr\bin\openssl.exe' req -new -newkey rsa:2048 -nodes `
       -keyout sfui-developerid.key `
       -out sfui-developerid.certSigningRequest `
       -subj "/CN=HKS Tech/emailAddress=<Apple ID のメール>/C=JP"
   ```
2. [ポータル](https://developer.apple.com/account) → Certificates → 「+」→ **Developer ID Application** → `sfui-developerid.certSigningRequest` をアップロード → `.cer` をダウンロード
3. `.cer` を秘密鍵入り `.p12` に変換（CI の secret `MACOS_CERT_P12` 用）
   ```powershell
   & 'C:\Program Files\Git\usr\bin\openssl.exe' x509 -inform DER -in DeveloperIDApplication.cer -out sfui-developerid.pem
   & 'C:\Program Files\Git\usr\bin\openssl.exe' pkcs12 -export -inkey sfui-developerid.key -in sfui-developerid.pem -out sfui-developerid.p12
   ```
4. 署名 + 公証（codesign / notarytool）は macOS 専用 → Mac があれば §5、無ければ §8（CI）へ `.p12` を登録
5. `sfui-developerid.key` と `.p12` は厳重に保管（漏洩すると第三者が署名できる）。**PR / Issue / チャットには絶対に貼らない**

> `Developer ID Installer` は `.pkg` 配布用。今回の `.app` + zip 配布には**不要**。
> 証明書（秘密鍵入り .p12）はバックアップを取っておくとマシン移行時に楽。

### A-1. 別の Mac で発行した証明書の .p12 エクスポート（次の作業）

証明書の**秘密鍵は発行した Mac のキーチェーンにしか無い**。CI（§8）で署名する場合やバックアップのために必ず `.p12` を書き出す。

1. 発行した Mac で**キーチェーンアクセス**を開く（下記「開き方」参照）→ サイドバーの「ログイン」→ カテゴリ「自分の証明書」→ `Developer ID Application: <名前> (WSDCSNQC59)` を右クリック →「書き出す…」→ ファイル形式 **.p12** → `sfui-developerid.p12` として保存（**書き出し用パスワード**を設定 = `MACOS_CERT_PASSWORD` に使う）

    キーチェーンアクセスの開き方（いずれか）:
    - **Spotlight**: `⌘ + Space` → 「キーチェーンアクセス」と入力 → Enter
    - **Finder**: アプリケーション → ユーティリティ → キーチェーンアクセス
    - **Launchpad**: その他 → キーチェーンアクセス
    - **ターミナル**: `open -a "Keychain Access"`

2. 確認: `security find-identity -v -p codesigning`（§3）に `Developer ID Application: … (WSDCSNQC59)` が表示されること
3. GitHub secrets（`MACOS_CERT_P12`）用に base64 化
   - Mac: `base64 -i sfui-developerid.p12 | pbcopy`
   - Windows（.p12 をコピーした場合）: `[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\path\sfui-developerid.p12')) | Set-Clipboard`
4. `.p12` とパスワードはパスワード マネージャー等に**バックアップ**し、**チャット / PR / Issue には絶対に貼らない**

## 3. 署名できることの確認

```bash
security find-identity -v -p codesigning
# → "Developer ID Application: <名前> (WSDCSNQC59)" が表示されれば OK
```

## 4. 公証用の認証情報を用意

- **アプリ用パスワード（簡単・スクリプトが想定している方式）**
  1. [appleid.apple.com](https://appleid.apple.com) → サインインとセキュリティ → アプリ用パスワード → 生成（`xxxx-xxxx-xxxx-xxxx`）
- （チーム運用なら App Store Connect API キー（.p8）でも可。その場合はスクリプトの notarytool 呼び出しを `--key/--key-id/--issuer` 方式に変更する）

## 5. 署名 + 公証の実行（本リポジトリ）

```bash
cd <repo>
SFUI_CODESIGN_IDENTITY="Developer ID Application: <名前> (WSDCSNQC59)" \
SFUI_NOTARIZE_APPLE_ID="<Apple ID のメール>" \
SFUI_NOTARIZE_TEAM_ID="WSDCSNQC59" \
SFUI_NOTARIZE_PASSWORD="<アプリ用パスワード>" \
bash packaging/make-mac-app.sh osx-arm64
```

スクリプトが内部で自動実行する内容:

1. `dotnet publish` → `SfUi.app` 組立（アイコン icns 込み）
2. **署名**: `codesign --force --deep --options runtime --entitlements packaging/mac/entitlements.plist --sign … SfUi.app` → `codesign --verify --deep --strict`
3. **公証**: zip 化 → `xcrun notarytool submit --wait`（Apple 側スキャン。通常数分〜十数分）
4. **ステープル**: `xcrun stapler staple SfUi.app`（公証チケットを埋め込み → **初回起動にネット接続不要**になる）
5. 公証済みの配布用 zip を再作成

## 6. 検証

```bash
codesign --verify --deep --strict --verbose=2 dist/mac/SfUi.app   # 署名の検証
spctl -a -vvv dist/mac/SfUi.app        # → accepted, source=Notarized Developer ID を確認
stapler validate dist/mac/SfUi.app     # → The validate action worked!
```

仕上げに「ユーザー入手時」の再現テスト:

```bash
cp -R dist/mac/SfUi.app /tmp/SfUi-test.app
xattr -w com.apple.quarantine 0081 /tmp/SfUi-test.app   # 隔離属性を付与
open /tmp/SfUi-test.app                                  # 警告なしで起動することを確認
```

## 7. リリースへの反映

1. 公証済みの `SfUi-<version>-osx-arm64.zip` を GitHub Release に添付
2. **README / リリースノートから「右クリック → 開く」の回避策の記載を削除**（`README.md` の EN/JA ダウンロード節、リリースノートの macOS 初回起動手順）
3. PLAN.md / 本手順書のステータスを「実施済み」に更新

## 8. CI（GitHub Actions）での自動署名（配線済み）

> **配線済み（2026-10-05）**: `.github/workflows/ci.yml` の `package-macos` ジョブに「証明書インポート（署名 ID を自動検出）→ 署名 + 公証」のステップを追加済み。**secrets 未登録の間はインポート ステップが自動スキップ**され、従来どおり未署名ビルドになる（= 安全に段階導入できる）。

secrets（リポジトリ設定 → Secrets and variables → Actions）:

| secret | 内容 |
|---|---|
| `MACOS_CERT_P12` | Developer ID Application 証明書（.p12）を base64 化した文字列（§2 A-1） |
| `MACOS_CERT_PASSWORD` | .p12 の書き出しパスワード |
| `NOTARIZE_APPLE_ID` | Apple ID のメール（メンバーシップ所有者） |
| `NOTARIZE_TEAM_ID` | `WSDCSNQC59`（固定文字列） |
| `NOTARIZE_PASSWORD` | アプリ用パスワード（§4） |

- 登録後は **Actions → CI → Run workflow**（`workflow_dispatch`）で署名 + 公証ビルドを実行できる（main への push でも実行される）
- 署名 ID はワークフローが `security find-identity` で証明書から自動検出するため、名前の登録は不要
- 失敗時は Actions ログ（`Signing identity: …` / notarytool の出力）→ §9 のトラブルシューティング参照

> base64 化: `base64 -i sfui-developerid.p12 | pbcopy`（macOS）。Windows は §2 A-1 の PowerShell 版。

## 9. トラブルシューティング

| 症状 | 対処 |
|---|---|
| 署名後に起動クラッシュ（`Killed: 9` 等） | entitlements 不足。`packaging/mac/entitlements.plist`（`allow-jit`）が付与されているか確認（スクリプトは自動で付与） |
| 公証が `Invalid` | `xcrun notarytool log <submission-id> --apple-id … --team-id … --password …` で理由を確認。`--deep` の限界が原因なら、内側の `*.dylib` と apphost を個別に `codesign` → 最後に `SfUi.app` の順へ切替 |
| 外部由来のネイティブ ライブラリを読み込む場合のみ | `com.apple.security.cs.disable-library-validation` を entitlements に追加（同チーム署名なら通常不要） |
| CI で `errSecInternalComponent` | Step 8 の `security set-key-partition-list` を実行しているか確認 |
| `spctl` が `rejected` | 署名が古い/公証前の zip を配布していないか確認（**publication 後の zip** を添付すること。ステープル後に再作成される） |

## 10. 再開時のチェックリスト

- [x] Apple ID の 2FA 有効化（更新手続き時点で有効）
- [x] メンバーシップ更新（Team ID `WSDCSNQC59`）→ ✅ 2026-10-05 完了（更新日 2027-10-06）
- [x] Developer ID Application 証明書を発行（§2 方法 A・別の Mac の Xcode）→ ✅ 2026-10-05
- [ ] 別の Mac から `.p12` をエクスポート + バックアップ（§2 A-1）
- [ ] アプリ用パスワードを発行（§4）
- [ ] GitHub secrets を登録（§8 の 5 つ）して署名 + 公証ビルドを実行（またはその Mac で §5）
- [ ] Step 6 で検証（spctl / stapler / 隔離再現テスト）
- [ ] Release に公証済み zip を添付し、README / リリースノートの回避策記載を削除
- [ ] 本手順書と PLAN.md のステータスを「実施済み」に更新

## 参考リンク

- Apple Developer Program: https://developer.apple.com/programs/
- メンバーシップ / 証明書ポータル: https://developer.apple.com/account
- サポート（Membership）: https://developer.apple.com/contact/
- アプリ用パスワード: https://appleid.apple.com （サインインとセキュリティ → アプリ用パスワード）
- notarytool ドキュメント: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution
