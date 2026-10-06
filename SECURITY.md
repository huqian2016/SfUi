# Security Policy

## Supported versions

The latest release (Microsoft Store / GitHub Releases) is supported with security fixes. Please reproduce issues on the latest version before reporting.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Report privately via either:

- GitHub Security Advisories: https://github.com/huqian2016/SfUi/security/advisories/new (preferred)
- E-mail: ko@hks-tech-kk.com

Include: the SfUi version, OS version, reproduction steps, and any logs (please remove org names, usernames and access tokens before sending).

We aim to acknowledge within a few business days and to ship a fix or mitigation as soon as practical. Credit is given in the release notes unless you prefer otherwise.

## Scope notes

- SfUi runs the Salesforce CLI and stores its data locally. Access tokens are obtained at runtime from the CLI and are not persisted by SfUi.
- The AI API key in `settings.json` is obfuscated (`enc1:…`, XOR+Base64) — this is casual-copy protection, not encryption; treat any file you share accordingly.
- The bundled evaluation AI key is intentionally shared with all users (with usage limits). Do not report it as a leak; report abuse concerns by e-mail instead.

---

## セキュリティ ポリシー（日本語）

- サポート対象: 最新リリース（Microsoft Store / GitHub Releases）。修正は最新版に対して行います
- 脆弱性の報告は**公開 Issue を避け**、GitHub の Security Advisories（推奨）または ko@hks-tech-kk.com へ。バージョン・OS・再現手順・ログ（組織名・ユーザー名・アクセス トークンはマスク）を添えてください
- SfUi はアクセス トークンを永続保存しません（実行時に CLI から取得）。`settings.json` の AI キーは難読化であり暗号化ではありません。同梱の評価用キーは全ユーザーでの共有が仕様です（漏洩報告ではなく、悪用の懸念をメールでご連絡ください）
