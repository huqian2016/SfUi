# 通知機能（Notification）設計書

最終更新: 2026-10-30 / ステータス: **調査・設計（実装前）**

対象リポジトリ: `c:\huqian\vscode\SfUi`（.NET 9 / SfUi.Core + SfUi.Presentation + WPF + Avalonia）

---

## 1. 目的とスコープ

SfUi から外部へ**手動・自動でメッセージを送る**機能を追加する。

| 項目 | 内容 |
| --- | --- |
| 対象 | 送信のみ（受信・返信・スレッド追跡は対象外） |
| チャネル | メール / Slack / Microsoft Teams / 汎用 Webhook / Chatwork / LINE / SMS（Twilio） |
| 手動送信 | 宛先・件名・本文・添付・送信時刻（即時 / 予約）を指定して送信 |
| 自動送信 | アプリ内イベント（ジョブ終了・バックアップ完了など）と定時スケジュールの 2 方式 |
| 非対象（今回） | 受信トレイ、双方向返信、モバイル プッシュ通知、SfUi サーバー側での常時稼働 |

> 前提: SfUi はデスクトップ アプリ。**自動送信は「アプリが動いている間」のみ実行**される（既存スケジューラと同じ制約）。

---

## 2. 調査: どのチャネルが使えるか

### 2.1 一覧と推奨度

| チャネル | 送信 API | 認証 | 費用の目安 | 主な制約 | 実装難易度 | 推奨 |
| --- | --- | --- | --- | --- | --- | --- |
| **メール (SMTP)** | 任意の SMTP サーバー（587/STARTTLS） | ユーザー名 + パスワード | サーバー次第（社内 SMTP は無料） | Gmail は**アプリ パスワード（2 段階認証必須）**。Exchange Online は**基本認証が全テナントで無効化済み**（SMTP AUTH も廃止予定） | 低（`System.Net.Mail` 標準） | ★★★ P1 |
| **Slack**（Incoming Webhook） | `POST https://hooks.slack.com/services/T…/B…/X…` | URL 自体が秘密 | 無料 | チャネル固定・削除不可・1 チャネル 1 URL | 最低 | ★★★ P1 |
| **Slack**（Web API） | `POST https://slack.com/api/chat.postMessage` | Bot トークン（`chat:write`） | 無料 | チャネル ID 指定、**1 メッセージ/秒/チャネル**、HTTP 200 でも `ok:false` を要確認 | 低 | ★★ P2 |
| **Microsoft Teams** | **Workflows（Power Automate）の Webhook URL** に `{"text": …}` か Adaptive Card | URL 自体が秘密 | Teams/Power Automate の契約に依存 | **Office 365 コネクタ（従来の Incoming Webhook）は 2026 年 5 月に廃止**。Workflows 必須。4 req/秒・28 KB 制限 | 低 | ★★★ P1 |
| **汎用 Webhook** | 任意 URL に JSON POST | 任意ヘッダー（Bearer 等） | 無料 | 受け側に合わせた JSON テンプレートが必要 | 低 | ★★★ P1 |
| **Chatwork** | `POST https://api.chatwork.com/v2/rooms/{room_id}/messages`（`x-chatworktoken`） | API トークン | 無料枠あり | ルーム ID 指定、レート制限ヘッダーあり | 低 | ★★ P3 |
| **LINE**（Messaging API） | `POST https://api.line.me/v2/bot/message/push`（`Authorization: Bearer`） | チャネル アクセス トークン | 無料枠あり（プラン依存） | **LINE Notify は 2025-03-31 で終了** → Messaging API（公式アカウント）必須。to = userId / groupId / roomId。`X-Line-Retry-Key` で重複送信防止可 | 中（公式アカウント開設が必要） | ★ P3 |
| **SMS（Twilio）** | `POST https://api.twilio.com/2010-04-01/Accounts/{SID}/Messages.json`（Basic 認証） | Account SID + Auth Token | **1 通ごとに課金**（日本宛は数円〜十数円/セグメント） | 電話番号は E.164。**試用版は検証済み番号にしか送れない**。日本語は UCS-2 のため **70 文字/セグメント**で分割課金 | 低〜中 | ★★ P3 |
| デスクトップ通知 | 既存のアプリ内トースト（`NotificationCenterViewModel`） | — | 無料 | アプリ内のみ | 実装済み | ★★★ 済 |
| OS トースト（Windows） | `Microsoft.Toolkit.Uwp.Notifications` 等 | — | 無料 | 追加パッケージ・Avalonia 非対応 | 中 | 対象外 |

### 2.2 Slack: Webhook と Web API の使い分け

| | Incoming Webhook | chat.postMessage |
| --- | --- | --- |
| 準備 | Slack アプリ作成 → Incoming Webhooks 有効化 → URL 発行 | アプリ作成 → `chat:write` スコープ → ワークスペースにインストール |
| 宛先 | 発行時に選んだ 1 チャネル固定 | `channel` で都度指定（ID 推奨） |
| メッセージ削除 | 不可 | 可（`chat.delete`） |
| エラー | HTTP 400/403/404 + エラー文字列 | **HTTP 200 + `{"ok":false,"error":"…"}`** |
| 採用 | **P1（設定が最も簡単）** | P2（宛先切替・Block Kit を使いたい場合） |

### 2.3 メール送信方式の比較（重要）

| 方式 | 対応プロバイダ | 実装 | 備考 |
| --- | --- | --- | --- |
| SMTP + ユーザー名/パスワード | 社内 SMTP、SendGrid SMTP、さくら等 | `System.Net.Mail.SmtpClient`（フレームワーク標準・追加依存なし） | **Gmail はアプリ パスワード必須**。OAuth2（XOAUTH2）は `SmtpClient` 非対応 |
| SMTP + OAuth2 | Gmail / Exchange Online | **MailKit の追加が必要** | Exchange Online は基本認証廃止のため将来必須になり得る |
| プロバイダ HTTP API | SendGrid / Mailgun / Resend / Amazon SES | `HttpClient` で JSON POST（追加依存なし） | API キー管理のみ。添付も可 |
| Microsoft Graph `sendMail` | Microsoft 365 | OAuth トークン取得が必要（アプリ登録） | 実装コスト高。P4 候補 |

→ **P1 は SMTP（基本認証）＋ HTTP API テンプレート**、OAuth 系は必要になった時点で追加。

---

## 3. 共通アーキテクチャ（Core）

```
┌────────────────┐   ┌──────────────────┐   ┌────────────────────┐
│ 手動送信 (UI)   │   │ 通知ルール (自動) │   │ スケジューラ通知    │
│ Notification   │   │ NotificationRule │   │ (予約送信ジョブ)    │
└───────┬────────┘   └────────┬─────────┘   └─────────┬──────────┘
        │                     │                       │
        └──────────┬──────────┴───────────┬───────────┘
                   ▼                      ▼
        ┌────────────────────┐   ┌──────────────────┐
        │ NotificationQueue  │──▶│ Notification     │──▶ history
        │ (outbox, 再送待ち)  │   │ Dispatcher       │    (data/notifications/history/*.json)
        └────────────────────┘   └────────┬─────────┘
                                          │ INotificationSender (チャネル別)
        ┌─────────────────────────────────┼──────────────────────────────┐
        ▼               ▼                 ▼              ▼               ▼
   SmtpSender     SlackSender      TeamsWebhookSender  WebhookSender  TwilioSmsSender
```

### 3.1 モデル（`src/SfUi.Core/Notifications/`）

```csharp
public static class NotificationChannelKinds   // "smtp" "slack" "teams" "webhook" "chatwork" "line" "twilio"
public static class NotificationSeverities    // "info" "warning" "error"
public static class NotificationStatuses      // "queued" "sending" "sent" "failed" "skipped"

/// 送信先 1 件分（秘密は CredentialProtector で保護して保存）
public sealed class NotificationChannel
{
    public string Id { get; set; } = "";          // 例 "slack-sales"
    public string Kind { get; set; } = NotificationChannelKinds.Slack;
    public string Name { get; set; } = "";        // 表示名
    public bool Enabled { get; set; } = true;
    public string Address { get; set; } = "";     // メール宛先 / Webhook URL / 電話番号 / チャネル ID
    public string? From { get; set; }             // SMTP From / Twilio From
    public string? Host { get; set; }             // SMTP ホスト
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Secret { get; set; }           // 保護済み: パスワード / トークン / Twilio AuthToken
    public Dictionary<string, string> Headers { get; set; } = new();   // 汎用 Webhook 用
    public int TimeoutSeconds { get; set; } = 30;
}

/// 送る内容（チャネル非依存）
public sealed class NotificationMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Severity { get; set; } = NotificationSeverities.Info;
    public string? Url { get; set; }                  // レコード / レポートへのリンク
    public string? DedupeKey { get; set; }            // 重複抑止キー
    public List<NotificationAttachment> Attachments { get; set; } = new();  // パス + 表示名
    public string? RuleId { get; set; }               // 自動通知の出所
    public string? OrgUsername { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public interface INotificationSender
{
    string Kind { get; }
    Task<NotificationSendResult> SendAsync(
        NotificationChannel channel, NotificationMessage message, CancellationToken ct);
}
```

### 3.2 Dispatcher の責務

1. **整形**: `NotificationFormatter` がチャネルごとに本文を作る
   - Slack / Teams / Chatwork: mrkdwn / markdown（`*太字*`、Teams は `**太字**`）
   - メール: プレーン + HTML（既存 `ReportHtmlWriter` のスタイルを流用可）
   - 件名に `[SfUi]` プレフィックス、重大度に応じて emoji（`:warning:` 等）は任意設定
2. **送信前チェック**: チャネル有効・必須項目・文字数（Slack 4,000 字、SMS 70 字/セグメント、Teams 28 KB）・添付サイズ
3. **リトライ**: 429 / 5xx / タイムアウトは指数バックオフ（1s → 2s → 4s、最大 3 回）。4xx は即失敗（設定ミス）
4. **レート制限**: チャネルごとに最小間隔（Slack 1,000 ms、Teams 250 ms）を `SemaphoreSlim` + 最終送信時刻で担保
5. **重複抑止 / まとめ送信**: `DedupeKey` が同一で `CooldownMinutes` 以内なら送らない。ルール単位で「N 分以内の複数件を 1 通にまとめる」も選択可
6. **静穏時間**: 既定 22:00–07:00 は送らず翌朝の開始時刻に繰り延べ（重大度 error は例外にできる）
7. **フォールバック**: 主チャネル失敗時に副チャネルへ（例: Slack → メール）
8. **履歴**: 成功 / 失敗 / スキップを `data/notifications/history/yyyy-MM.json` に記録（最大 1,000 件/月、90 日保持）
9. **ログ**: 例外は `AppLog` へ。**本文・トークン・Webhook URL は `CredentialMask` で伏せる**（`token=` `password=` に加え URL も伏せる拡張が必要）

### 3.3 保存場所

| ファイル | 内容 |
| --- | --- |
| `data/notifications/channels.json` | 送信先定義（秘密は `dpapi:` / `aesgcm:` で保護） |
| `data/notifications/rules.json` | 自動通知ルール |
| `data/notifications/history/yyyy-MM.json` | 送信履歴（本文は先頭 500 字まで） |
| `data/notifications/outbox.json` | 未送信 / 再送待ち（アプリ終了時に残った分） |
| `data/scheduler/jobs.json` | 予約送信 = スケジューラの `notification` ジョブとして保存（既存ストアを再利用） |

### 3.4 依存パッケージ

**追加なし**（`System.Net.Http` / `System.Net.Mail` / `System.Text.Json` で完結）。
OAuth2 メールが必要になった段階で `MailKit` を検討。

---

## 4. 手動送信（P1）

上部バーに **「通知」** ボタン（`OpenNotifications`）→ 通知ウィンドウ（両シェル）。

### 4.1 送信タブ

| 項目 | 内容 |
| --- | --- |
| 送信先 | チャネルの複数選択（チェックボックス）。既定は「既定チャネル」設定 |
| 件名 / 本文 | 差し込み: `{Date}` `{Time}` `{Org}` `{User}` `{Machine}` `{Report:名前}` など |
| 添付 | 帳票 PDF / レポート CSV / ログ ファイル（1 件 10 MB まで、チャネルごとに対応可否表示） |
| 送信時刻 | **今すぐ** / **予約**（日時指定 → スケジューラの `notification` ジョブ `once` を自動生成） |
| プレビュー | チャネルごとの実際の送信本文（文字数・SMS セグメント数・Slack 4,000 字警告） |
| テスト送信 | 内容を `[SfUi test]` に差し替えて 1 回送る（履歴にも記録） |

### 4.2 送信結果

- 結果は送信タブ下部の一覧（チャネル / 状態 / HTTP / 所要時間 / エラー）
- 既存の**トースト通知**でも「送信しました」「失敗しました」を表示（`NotificationCenterViewModel` を再利用）

---

## 5. 自動送信（トリガーの調査結果）

### 5.1 3 つの方式

| 方式 | 説明 | 実装 |
| --- | --- | --- |
| **A. イベント購読** | アプリ内の出来事（ジョブ終了・バックアップ完了など）に反応 | `NotificationRule` + `EventBus`（Core の `Action<T>` イベント） |
| **B. 定時（cron 相当）** | 毎日 9:00 / 毎週月曜 / 月末など | 既存 `SchedulerService` に `notification` ジョブ種別を追加（`ScheduleKinds` をそのまま利用） |
| **C. 条件付き** | 実行結果や SOQL 結果が条件を満たした時だけ送る | ルールに条件（`rows > 100`、`failed > 0`、`usagePercent >= 80` 等） |

### 5.2 トリガー候補（SfUi の既存機能に紐づく）

| # | トリガー | 発火点（既存コード） | 条件の例 | 優先度 |
| --- | --- | --- | --- | --- |
| 1 | スケジュール ジョブ完了 | `SchedulerService.RunUpdated` | 成功 / 失敗 / 見逃し（`JobRunStatuses`） | ★★★ |
| 2 | レポート実行結果 | `ReportJobExecutor` / `ReportsViewModel` | 行数 > N、0 件、前回比 ±X% | ★★★ |
| 3 | バックアップ完了 / 失敗 | `BackupService.RunBackupAsync` | 失敗、件数 0、想定件数と差異 | ★★ |
| 4 | ETL 実行 | `EtlViewModel` / `EtlJobRunner` | 失敗行 > 0、成功率 < 95% | ★★ |
| 5 | データ入出力 | `DataImportJobExecutor` | 失敗 > 0 | ★★ |
| 6 | 組織ヘルス | `OrgManageService`（`/limits`） | ライセンス使用率 ≥ 80% / 95% | ★★ |
| 7 | 組織接続テスト | `OrgManageService.TestConnectionAsync` | 失敗（接続不可） | ★★ |
| 8 | スクラッチ組織 | `ScratchOrgService` | 作成完了 / 失敗 / **期限切れ N 日前** | ★ |
| 9 | レコード条件（SOQL 監視） | `SoqlService` | 「未処理のケース件数 > 0」など | ★★ |
| 10 | 定時リマインダ | スケジューラ | 毎営業日 9:00 にレポート PDF を添付送信 | ★★★ |
| 11 | 手動テスト | 送信タブ | — | ★★★ |
| 12 | アプリ起動時サマリ | `MainViewModel` 起動 | 前回からの失敗ジョブ / 期限切れ scratch org の要約 | ★ |

> 1 / 2 / 10 が「実務で最も使う」組み合わせ（定時レポート + 失敗の即時通知）。P2 でこの 3 つを実装対象とする。

### 5.3 配信保証（現実的な設計）

| 状況 | 挙動 |
| --- | --- |
| アプリ起動中 | 予定時刻に送信 |
| アプリ終了中に予定時刻を過ぎた | `outbox` に「未送信」で残し、**次回起動時に「送る / 破棄」を選ばせる**（既定: 24 時間以内なら送る、それ以降は破棄） |
| 送信中にネットワーク断 | 指数バックオフで最大 3 回 → 失敗として履歴 + トースト |
| PC スリープ | 既存の `SchedulerKeepAwake` 設定を流用（通知ジョブも同じ扱い） |

> 「アプリを閉じていても必ず送る」が必要なら、P4 で **Windows タスク スケジューラ / launchd に `SfUi.exe --run-notifications` を登録**する方式を追加する（実装は数十行 + 設定 UI）。

---

## 6. 自動通知ルールの UI（P2）

「ルール」タブで一覧 + 追加 / 編集ダイアログ。

```
名前: 日次売上レポート
種別: スケジュール / イベント / 条件
契機: [レポート] 日次売上 を実行   条件: 行数 > 0
送信先: ☑ Slack (#sales)  ☑ メール (sales@example.com)
内容: 件名 [SfUi] 日次売上 {Date} / 本文 {Result.Summary}
添付: ☑ レポート PDF
抑制: 同内容は 60 分に 1 回まで / 静穏時間 22:00-07:00 は翌朝
```

AutomationId 例: `NotifRules` / `NotifRuleAdd` / `NotifRuleEditor*` / `NotifRuleChannel*`

---

## 7. 主要な設定項目（`AppSettings` 追加）

| 設定 | 既定 | 用途 |
| --- | --- | --- |
| `NotificationsEnabled` | true | 全体スイッチ |
| `NotificationDefaultChannelIds` | 空 | 手動送信の既定宛先 |
| `NotificationQuietHoursStart` / `End` | `""`（無効） | 静穏時間 |
| `NotificationMaxRetries` | 3 | 再送回数 |
| `NotificationHistoryRetentionDays` | 90 | 履歴保持 |
| `NotificationDeliveryWindowMinutes` | 1440 | 未送信を「遅延送信」する上限 |

---

## 8. ローカライズ（`Notif_*` = 約 60 キー × 4 言語）

例: `Notif_Title` / `Notif_TabSend` / `Notif_TabRules` / `Notif_TabHistory` / `Notif_TabChannels` / `Notif_ChannelKindSlack` / `Notif_StatusSent` / `Notif_ErrorAuth` / `Notif_HintQuietHours` / `Notif_ConfirmDeleteChannel` …
既存の `UiText.{En,Ja,Zh,Ko}.cs` に同一キー集合で追加（テストで検証）。

---

## 9. テスト計画

| 種別 | 内容 |
| --- | --- |
| Core 単体 | `NotificationFormatter`（チャネル別整形・文字数・SMS セグメント計算）/ `NotificationDispatcher`（リトライ・429・4xx 即失敗・レート制限・dedupe・静穏時間・フォールバック）/ ストア（JSON 往復・秘密の保護・履歴ローテーション） |
| 送信実装 | `HttpMessageHandler` 差し替えで Slack `ok:false`、Teams 429、Twilio 400 を再現（既存 `AiChatClientTests` と同じ手法） |
| SMTP | `SmtpSender` は `ISmtpTransport` 抽象 + フェイクで検証（実サーバー不要） |
| VM | `NotificationsViewModel`（宛先選択・予約生成・プレビュー・送信コマンド） |
| E2E（WPF / Avalonia） | ローカル `HttpListener`（既存 `sfui-rest-server.ps1` 方式）で Slack / Teams / Webhook / Twilio のリクエストを捕捉して検証。SMTP は `TcpListener` の簡易 SMTP シンクで `MAIL FROM / RCPT TO / DATA` を検証 |
| 回帰 | スケジューラ既存 E2E（`sfui-scheduler-e2e.ps1`）に `notification` ジョブを追加 |

---

## 10. フェーズ計画

| フェーズ | 内容 | 目安 |
| --- | --- | --- |
| **P1** | Core 基盤（モデル / ストア / Dispatcher / SMTP・Slack Webhook・Teams Webhook・汎用 Webhook）+ 通知ウィンドウ（送信 / 履歴 / チャネル）+ トースト連携 + テスト + マニュアル | 2〜3 日 |
| **P2** | 自動通知ルール（イベント / スケジュール / 条件）+ スケジューラ `notification` ジョブ + トリガー #1 #2 #10 + 添付（帳票 PDF） | 2〜3 日 |
| **P3** | SMS（Twilio）+ Chatwork / LINE + Slack Web API（宛先切替・Block Kit）+ 失敗フォールバック | 2 日 |
| **P4** | アプリ未起動時の配信（タスク スケジューラ登録）/ 通知ダッシュボード / 送信レポート | 1〜2 日 |

---

## 11. 決定事項（2026-10-30 確定）

| # | 論点 | 決定 |
| --- | --- | --- |
| 1 | P1 のチャネル | **メール + Slack + Microsoft Teams + 汎用 Webhook**（SMS / Chatwork / LINE は P3） |
| 2 | メール方式 | **SMTP（ユーザー名 + パスワード、STARTTLS）** で開始。OAuth / HTTP API は必要時に追加 |
| 3 | 自動通知トリガー | **スケジュール ジョブの完了・失敗**、**定時レポート送信**、**レポート結果の条件** の 3 つから実装 |
| 4 | 添付 | **帳票 PDF の添付を含める**（P2） |
| 5 | 配信保証 | アプリ起動中のみ + 未送信は次回起動時に確認（タスク スケジューラ登録は P4） |

### 11.1 P1 で完成する形

1. 上部バー **「通知」** → 通知ウィンドウ（送信 / 履歴 / チャネル の 3 タブ）
2. チャネル設定: SMTP（ホスト・ポート・STARTTLS・From・ユーザー・パスワード）/ Slack Webhook / Teams Workflows Webhook / 汎用 Webhook（URL + ヘッダー + JSON テンプレート）
3. 手動送信: 宛先の複数選択、件名・本文（差し込み）、**今すぐ / 予約**（予約はスケジューラの `notification` ジョブ）、テスト送信、チャネル別プレビュー
4. 送信結果の一覧 + 既存トースト通知への連携
5. 履歴（`data/notifications/history/*.json`、90 日保持）と再送

### 11.2 P2 で完成する形

1. **ルール** タブ + ルール編集ダイアログ（トリガー / 条件 / 送信先 / 文面 / 抑制）
2. トリガー:
   - ジョブ完了・失敗・見逃し（`SchedulerService.RunUpdated` / `Notification`）
   - 定時送信（毎営業日 9:00 など。帳票 PDF を添付）
   - レポート結果の条件（行数 > N / 0 件 / 前回比 ±X%）
3. 添付（帳票 PDF / レポート CSV）と、チャネル別の添付可否表示
4. dedupe / 静穏時間 / レート制限 / 失敗フォールバック
