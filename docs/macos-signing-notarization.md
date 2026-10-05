# macOS 署名・公証（Developer ID）手順書

最終更新: 2026-10-05 / ステータス: **未実施（実施は後回し・保留中）**
→ 証明書の準備ができたら「Step 5」から実行する。リポジトリ側の配線は実装済み（§0 参照）。

## 0. 目的と現状

- **目的**: `SfUi.app` を **Developer ID 署名 + 公証（notarization）** し、ユーザーがダウンロード後に「右クリック → 開く」なしで（ダブルクリックで）起動できるようにする
- **現状（未署名）**: ブラウザーでダウンロードした zip は macOS が quarantine（隔離）属性を付けるため、初回のみ Gatekeeper がブロックする
  - 回避策（現行リリースに記載済み）: `SfUi.app` を右クリック →「開く」、または `xattr -dr com.apple.quarantine SfUi.app`
  - 参考: `curl` 等のコマンドでダウンロードした場合は quarantine が付かないため初回からダブルクリックで起動できる
- **費用**: Apple Developer Program 年会費 **¥12,980/年（日本・税込）**。証明書の発行も公証サービスもこの年会費に含まれる（公証のたびの追加課金はなし）
- **本プロジェクトの状況（2026-10-05 時点）**:
  - 使用予定の Apple ID は **2015-09-10 で期限切れの個人メンバーシップ（Team ID `WSDCSNQC59`）の Account Holder** になっている
  - そのため新規登録は「別のメンバーシップの Account Holder に関連付けられています」エラーでブロックされる → **新規登録ではなく「更新（Renew）」** が必要（Step 1）
- **リポジトリ側の対応は実装済み**（追加作業なしで Step 5 を実行できる）:
  - `packaging/make-mac-app.sh` … 環境変数を設定すると `codesign`（hardened runtime）+ `notarytool` + `stapler` を自動実行（未設定時はスキップ）
  - `packaging/mac/entitlements.plist` … **.NET の JIT 許可**（`allow-jit` / `allow-unsigned-executable-memory`）。hardened runtime 下でこれが無いと署名後に起動クラッシュする（`c0641a7` で配線済み）

## 1. メンバーシップの更新（既存 Apple ID）

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

**方法 A: Xcode（推奨・簡単）**
1. Xcode → Settings → Accounts → Apple ID → Manage Certificates
2. 「+」→ **Developer ID Application** → キーチェーンに秘密鍵ごと作成

**方法 B: ポータル（Xcode が無い Mac 用）**
1. Certificates, Identifiers & Profiles → Certificates → 「+」→ Developer ID Application
2. キーチェーンアクセスで CSR を作成 → アップロード
3. `.cer` をダウンロード → ダブルクリックでキーチェーンへ

> `Developer ID Installer` は `.pkg` 配布用。今回の `.app` + zip 配布には**不要**。
> 証明書（秘密鍵入り .p12）はバックアップを取っておくとマシン移行時に楽。

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

## 8. CI（GitHub Actions）での自動署名（任意）

secrets（リポジトリ設定 → Secrets and variables → Actions）:

| secret | 内容 |
|---|---|
| `MACOS_CERT_P12` | Developer ID Application 証明書（.p12）を base64 化した文字列 |
| `MACOS_CERT_PASSWORD` | .p12 のパスワード |
| `NOTARIZE_APPLE_ID` / `NOTARIZE_TEAM_ID` / `NOTARIZE_PASSWORD` | 公証用の認証情報 |

`package-macos` ジョブに追加するステップ例:

```yaml
      - name: Import signing certificate
        run: |
          echo "$MACOS_CERT_P12" | base64 --decode > cert.p12
          security create-keychain -p actions build.keychain
          security default-keychain -s build.keychain
          security unlock-keychain -p actions build.keychain
          security import cert.p12 -k build.keychain -P "$MACOS_CERT_PASSWORD" -T /usr/bin/codesign
          security set-key-partition-list -S apple-tool:,apple: -s -k actions build.keychain

      - name: Build .app bundle (sign + notarize)
        env:
          SFUI_CODESIGN_IDENTITY: "Developer ID Application: <名前> (WSDCSNQC59)"
          SFUI_NOTARIZE_APPLE_ID: ${{ secrets.NOTARIZE_APPLE_ID }}
          SFUI_NOTARIZE_TEAM_ID: ${{ secrets.NOTARIZE_TEAM_ID }}
          SFUI_NOTARIZE_PASSWORD: ${{ secrets.NOTARIZE_PASSWORD }}
        run: bash packaging/make-mac-app.sh osx-arm64
```

> base64 化: `base64 -i cert.p12 | pbcopy`（macOS）でクリップボードへ。

## 9. トラブルシューティング

| 症状 | 対処 |
|---|---|
| 署名後に起動クラッシュ（`Killed: 9` 等） | entitlements 不足。`packaging/mac/entitlements.plist`（`allow-jit`）が付与されているか確認（スクリプトは自動で付与） |
| 公証が `Invalid` | `xcrun notarytool log <submission-id> --apple-id … --team-id … --password …` で理由を確認。`--deep` の限界が原因なら、内側の `*.dylib` と apphost を個別に `codesign` → 最後に `SfUi.app` の順へ切替 |
| 外部由来のネイティブ ライブラリを読み込む場合のみ | `com.apple.security.cs.disable-library-validation` を entitlements に追加（同チーム署名なら通常不要） |
| CI で `errSecInternalComponent` | Step 8 の `security set-key-partition-list` を実行しているか確認 |
| `spctl` が `rejected` | 署名が古い/公証前の zip を配布していないか確認（**publication 後の zip** を添付すること。ステープル後に再作成される） |

## 10. 再開時のチェックリスト

- [ ] Apple ID の 2FA 有効化
- [ ] メンバーシップ更新（Team ID `WSDCSNQC59`）→ 有効化を確認
- [ ] Developer ID Application 証明書を発行（+ .p12 バックアップ）
- [ ] アプリ用パスワードを発行
- [ ] Step 5 を実行（署名 + 公証 + ステープル）
- [ ] Step 6 で検証（spctl / stapler / 隔離再現テスト）
- [ ] Release に公証済み zip を添付し、README / リリースノートの回避策記載を削除
- [ ] （任意）CI 配線（Step 8）
- [ ] 本手順書と PLAN.md のステータスを「実施済み」に更新

## 参考リンク

- Apple Developer Program: https://developer.apple.com/programs/
- メンバーシップ / 証明書ポータル: https://developer.apple.com/account
- サポート（Membership）: https://developer.apple.com/contact/
- アプリ用パスワード: https://appleid.apple.com （サインインとセキュリティ → アプリ用パスワード）
- notarytool ドキュメント: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution
