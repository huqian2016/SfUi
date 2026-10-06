# Privacy Policy — SfUi

Last updated: 2026-10-06

SfUi is a local desktop application that wraps the Salesforce CLI (`sf`).
This policy describes what data the app handles and what leaves your device.

## Summary

- SfUi itself collects **no telemetry, analytics or crash reports** and requires no account.
- Your settings, history, favorites, logs and cached org data are stored **locally on your device**.
- Network traffic originates only from actions you trigger: Salesforce CLI commands contacting Salesforce, and AI chat requests to the AI endpoint you configure.

## Data stored on your device

SfUi stores its data in a `data/` folder (next to the executable in portable mode, otherwise `%APPDATA%\SfUi`; on macOS `~/Library/Application Support/SfUi`). This includes:

- `settings.json` — your preferences (org selection, language, AI endpoint / model, AI API key). The API key is stored obfuscated (enc1:…, XOR+Base64 — obfuscation only, not encryption).
- `history/`, `results/` — what you executed and its results.
- `logs/` — app logs.
- `orginfo/`, `backups/`, `org-manage.json` — cached org metadata, backups you created, and your local tags/notes.

To remove all SfUi data, delete this folder. Uninstalling (Microsoft Store / MSIX) removes the app; a data folder in `%APPDATA%\SfUi` can be deleted manually afterwards.

## What is sent off your device (and when)

1. **Salesforce CLI operations (you trigger these).** When you run SOQL, Apex, deploys, data import/export, backups, etc., SfUi runs the Salesforce CLI on your behalf; those commands send your request and its contents to Salesforce (your org), exactly as if you ran `sf` yourself. Salesforce's own terms and privacy policy apply to that data. SfUi disables the CLI's usage telemetry (`SF_DISABLE_TELEMETRY=true`).
2. **AI chat (you trigger this).** Messages you type in the AI panel — plus any history result or tab data you explicitly attach — are sent to the AI endpoint configured in Settings → AI (default: DeepSeek; can be switched to OpenAI, Anthropic's compatibility layer, or a local LLM, in which case nothing leaves your machine). If you use the bundled evaluation key, requests go to the default endpoint and the key has usage limits and may stop without notice.
3. **Links / browser pages.** Opening a browser page (org home, Setup, records, GitHub) hands the URL to your default browser.

SfUi sends nothing else. There is no automatic update check, no ads, and no third-party SDKs.

## Contact

Questions: ko@hks-tech-kk.com or https://github.com/huqian2016/SfUi/issues

---

# プライバシーポリシー — SfUi

最終更新: 2026-10-06

SfUi は Salesforce CLI（`sf`）をラップするローカル動作のデスクトップ アプリです。本ポリシーは、アプリが扱うデータと外部へ送信されるものを説明します。

## 要点

- SfUi 自体は**テレメトリ・解析・クラッシュレポートを一切送信しません**（アカウント不要）
- 設定・履歴・お気に入り・ログ・組織情報のキャッシュは**端末内**にのみ保存されます
- 外部通信は、あなたが実行した操作に限られます: Salesforce CLI による Salesforce へのアクセスと、設定した AI 接続先への AI チャットの送信

## 端末内に保存されるデータ

データは `data/` フォルダ（ポータブル時 = exe の隣、それ以外 = `%APPDATA%\SfUi`。macOS は `~/Library/Application Support/SfUi`）に保存されます:

- `settings.json` — 設定（組織・言語・AI 接続先 / モデル・AI API キー）。API キーは難読化（enc1:…、XOR+Base64。暗号化ではなく難読化です）
- `history/`・`results/` — 実行内容と結果
- `logs/` — アプリのログ
- `orginfo/`・`backups/`・`org-manage.json` — 組織情報キャッシュ・作成したバックアップ・タグ/メモ

全データの削除はこのフォルダを削除すれば完了です（アンインストール後は `%APPDATA%\SfUi` を手動で削除してください）。

## 外部へ送信されるもの（とタイミング）

1. **Salesforce CLI の操作（あなたが実行したとき）**: SOQL / Apex / デプロイ / データ入出力 / バックアップ等は、SfUi が Salesforce CLI を実行するため、あなた自身が `sf` を実行した場合と同様に、リクエスト内容が Salesforce（あなたの組織）へ送信されます。そのデータには Salesforce の利用規約・プライバシーポリシーが適用されます。CLI の利用統計（テレメトリ）は SfUi が無効化します（`SF_DISABLE_TELEMETRY=true`）
2. **AI チャット（あなたが実行したとき）**: 入力したメッセージ（+ 明示的に添付した履歴結果やタブデータ）が、設定 → AI で構成した接続先へ送信されます（既定: DeepSeek。OpenAI / Anthropic 互換レイヤー / ローカル LLM に切替可。ローカル LLM なら端末外へ出ません）。同梱の評価用キーを使う場合、送信先は既定の接続先で、キーには上限があり予告なく停止することがあります
3. **リンク・ブラウザー表示**: 組織ホーム / Setup / レコード / GitHub を開く操作は既定のブラウザーに URL を渡します

上記以外の送信はありません（自動アップデート確認・広告・第三者 SDK なし）。

## 連絡先

ko@hks-tech-kk.com / https://github.com/huqian2016/SfUi/issues
