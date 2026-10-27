# スケジューラ（ジョブ予約実行）設計書

最終更新: 2026-10-06 / ステータス: **Phase 1〜2 実装済み（Core 基盤 + WPF UI）→ Phase 3〜5 未着手**

対象リポジトリ: `c:\huqian\vscode\SfUi`（.NET 9 / SfUi.Core + SfUi.Presentation + WPF + Avalonia）

---

## 1. 目的とスコープ

SfUi に **カレンダー形式のジョブ予約実行（スケジューラ）** を追加する。SOQL 実行・匿名 Apex・バックアップ・データ投入・`sf` コマンド・REST API・（任意で）OS コマンドラインといった既存機能を「予定」として登録し、指定日時に無人で実行する。実行結果は履歴として残し、開始前・開始時・終了時にフェードアウトするポップアップで通知する。

### 1.0 確定事項（2026-10-06 ユーザー確認済み）

| 論点 | 決定 |
| --- | --- |
| 実行主体 | アプリ起動中のみ実行。未起動時の予定は**見逃し記録のみ**（Windows タスク スケジューラ連携はしない） |
| カレンダー | **月表示 + 日別予定**（週表示は将来の追加候補） |
| 繰り返し | 1 回 / 毎日 / 毎週 / 毎月 / N 分間隔 ＋ **土日スキップ・日本の祝日スキップ**（Cron 式は対象外） |
| 通知 | **アプリ内のフェード ポップアップのみ**（OS 通知・メール・Slack は将来の候補） |
| 実装順 | **Phase 1（Core 基盤）+ Phase 2（WPF UI）を先に完成** → Phase 3 以降は段階的に |

### 1.1 ユーザー要件（原文）

1. **カレンダー形式**でジョブ実行予定を管理できる UI
2. ジョブの種類が複数（例: Apex 実行、SOQL 実行、データバックアップ、データ投入、ほかの SF コマンド機能、REST API などの API 操作、コマンドライン）
3. **ジョブ実行履歴**を見られる
4. 実行のため**PC がスリープしない**ようにする
5. **実行できなかった場合**（アプリ未起動・スリープ中などで予定時刻を過ぎた場合）は**勝手に再実行しない**（後から手動で実行できる）
6. **手動で実行する手段**を用意する
7. 実行前・実行後、（必要なら開始時も）**フェードアウトするポップアップ**で通知。**何分前／何秒前に通知するか設定**でき、**ジョブごとに通知の有無**を選べる

### 1.2 提案する基本方針

| 論点 | 提案 |
| --- | --- |
| 実行主体 | **SfUi アプリ内の常駐スケジューラ**（アプリ起動中のみ実行）。外部タスク スケジューラ登録はしない |
| アプリ未起動時の予定 | 起動時に「**見逃し（Missed）**」として履歴に記録するだけで、**自動再実行はしない**（要件 5） |
| スリープ防止 | 次回実行が近い間だけ OS のスリープを抑止（Windows: `SetThreadExecutionState` / macOS: `caffeinate`）。**電源オフ・強制スリープ・ログオフは防げない** |
| 通知 | アプリ内の**最前面フェード ポップアップ**（右下、自動フェードアウト、クリックでジョブ詳細）。OS 標準トーストは初期版では使わない |
| 予定の持ち方 | 「**ジョブ定義**（何を・どこで実行するか）+ **スケジュール**（いつ実行するか）」の 1 レコードにまとめ、カレンダーは**発生予定（occurrence）を計算して表示**する |
| 実行エンジン | 既存サービスをそのまま呼ぶ **ジョブ種別ごとの Executor**。スケジューラ本体は Core に置き UI 非依存 |
| 同時実行 | 既定は **同時 1 実行（待ち行列）**。設定で並列数変更可 |

---

## 2. スケジュール仕様

### 2.1 繰り返し種別

| 種別 | 指定内容 | 例 |
| --- | --- | --- |
| `Once`（1 回） | 日付 + 時刻 | 2026-10-10 22:00 に 1 回だけ |
| `Daily`（毎日） | 時刻 | 毎日 07:00 |
| `Weekly`（毎週） | 曜日（複数可）+ 時刻 | 月・水・金 18:30 |
| `Monthly`（毎月） | 日にち（複数可、1–31 / 31 は月末扱い）+ 時刻 | 毎月 1 日・15 日 09:00 |
| `Interval`（間隔） | N 分 / N 時間ごと + 開始時刻 | 30 分ごと（09:00 開始） |

- 共通オプション: **終了日時**（任意）、**土日スキップ**（任意）、**有効/無効**。
- タイムゾーンは **ローカル時刻**固定（`DateTimeOffset` で保存し、表示はローカル）。夏時間のある地域でも「壁時計の時刻」で発火する。
- 「毎月 31 日」で 31 日が無い月は**その月の末日**に発火（2 月なら 28/29 日）。
- Cron 式・営業日カレンダー・祝日除外は**初期版では対象外**（未決事項 3 参照）。

### 2.2 次回実行時刻の計算（中核ロジック）

`JobScheduleCalculator`（純粋関数・UI/IO 非依存）を新設し、単体テストで固める。

```csharp
// 次に after より後に来る発火時刻（無ければ null）
DateTimeOffset? NextOccurrence(JobSchedule schedule, DateTimeOffset after, DateTimeOffset now);

// [from, to] の範囲に含まれる発火時刻（見逃し検出用・上限件数あり）
IReadOnlyList<DateTimeOffset> OccurrencesBetween(JobSchedule schedule, DateTimeOffset from, DateTimeOffset to, int maxCount = 500);
```

- `Interval` は「開始時刻 + N 分 × k」で計算し、**アプリ再起動をまたいでも位相がずれない**。
- `Once` は発火後に自動で無効化（`Enabled = false`）し、履歴に残す。

### 2.3 見逃し（Missed）の扱い（要件 5）

1. スケジューラ起動直後（およびスリープ復帰時）に `NextRunAt` と現在時刻を比較する。
2. `現在時刻 > NextRunAt + 猶予（既定 60 秒）` の場合、その発生分を **`Missed` として履歴に記録**し、`NextRunAt` を次回へ進める。**実行はしない**。
3. アプリが起動していなかった期間の予定も、起動時に `OccurrencesBetween(最終把握時刻, 現在)` で列挙して `Missed` として記録（**最大 500 件まで。超過分は「N 件省略」と要約**）。
4. UI ではカレンダー上で薄いグレー + 「見逃し」バッジ、履歴では状態列に `Missed`。**「今すぐ実行」で手動実行できる**（要件 6 と共通）。
5. 猶予（`MissedGraceSeconds`）はジョブごとに設定可。既定 60 秒。`0` にすれば「1 秒でも遅れたら見逃し」。

> スリープ復帰直後に実行しないのは要件 5 の指示どおり。復帰時に「予定時刻を過ぎた」＝見逃しとして扱う。

---

## 3. ジョブ種別（要件 2）

| 種別キー | UI 名 | パラメータ | 呼び出す既存サービス | 結果サマリ例 |
| --- | --- | --- | --- | --- |
| `Soql` | SOQL 実行 | クエリ、最大行数、CSV 保存先（任意） | `SoqlService.ExecuteSoqlAsync` | `1,234 行 / 0.8 秒` |
| `Apex` | 匿名 Apex 実行 | Apex ソース、ログ取得有無 | `ApexService.ExecuteAnonymousAsync` / `ListLogsAsync` / `GetLogAsync` | `成功 / コンパイル 0 エラー` |
| `Backup` | データ バックアップ | オブジェクト一覧、ラベル、説明、削除済み含む | `BackupService.RunBackupAsync` | `12 オブジェクト / 8,420 行 / backup-20261006-0700` |
| `DataImport` | データ投入 | オブジェクト、CSV パス、方式（REST/Bulk）、外部 Id、操作 | `DataImportService.RunRestAsync` / `RunBulkAsync` | `成功 980 / 失敗 20` |
| `DataExport` | データ エクスポート | SOQL またはオブジェクト、出力先フォルダ、方式 | `DataExportService.RunRestAsync` / `RunBulkAsync` | `1,200 行 → D:\out\acc.csv` |
| `SfCommand` | Salesforce CLI コマンド | 引数（`sf` 以降）、作業フォルダ | `SfCliRunner.RunAsync` | `終了コード 0 / 12 行出力` |
| `RestApi` | REST API 呼び出し | メソッド、パス（`/services/data/vXX.X/...`）、ヘッダー、ボディ | `SalesforceRestClient` | `200 OK / 45 バイト` |
| `ShellCommand`※ | OS コマンドライン | 実行ファイル、引数、作業フォルダ | `Process`（`SfUi.Core` の新規 `ShellRunner`） | `終了コード 0` |
| `Etl`※ | ETL 移行ジョブ | 保存済み ETL ジョブ ファイル | `EtlJobRunner`（要ジョブ ファイル保存機能） | `3 ステップ成功` |
| `Deploy`※ | デプロイ / 検証 | ソース フォルダ、検証のみ、対象組織 | `DeployService.ExecuteAsync` | `検証成功 / 42 コンポーネント` |

※ = 追加候補（Phase 2/4）。ユーザー要件に明示があるのは `Soql` / `Apex` / `Backup` / `DataImport` / `SfCommand` / `RestApi` / `ShellCommand`。

- 認証情報は**保存しない**。`sf` CLI の認証（`sf org list` の username）を利用する。
- `ShellCommand` はユーザーが入力した任意コマンドを実行するため、**ジョブ編集ダイアログで警告を表示**し、既定では一覧の「危険」アイコン付きで表示する（未決事項 5）。
- 種別ごとのパラメータは `PayloadJson` に**型付きで直列化**（`SoqlJobPayload` など）。種別を変えたらパラメータは初期化（確認ダイアログ）。

---

## 4. 実行履歴（要件 3）

`JobRunRecord` を 1 実行 = 1 JSON ファイルで保存（`AtomicJsonFile`）。

```csharp
public sealed class JobRunRecord
{
    public string RunId { get; set; }            // 20261006-070000-<jobId 短縮>
    public string JobId { get; set; }
    public string JobName { get; set; }
    public string JobType { get; set; }
    public string? TargetOrg { get; set; }
    public string Trigger { get; set; }          // Schedule / Manual
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; }           // Running / Succeeded / Failed / Missed / Canceled / Skipped
    public string Summary { get; set; }          // 一覧に出す 1 行
    public string? Error { get; set; }           // 失敗理由（例外メッセージ）
    public string? OutputPreview { get; set; }   // 先頭 4,000 文字
    public string? OutputPath { get; set; }      // 大きい出力の別ファイル
    public double DurationSeconds { get; set; }
}
```

- 保存先: `data\scheduler\runs\<yyyy-MM>\<runId>.json`（＋`.bak`）。出力が大きい場合のみ `runs\<yyyy-MM>\<runId>.out.txt`。
- 保持: 既定 **90 日 / 最大 500 件**（設定可）。起動時に古いものを削除。
- 履歴タブ: 一覧（時刻・ジョブ名・種別・状態・所要時間・対象組織・サマリ）+ 検索/絞り込み + 詳細パネル（出力全文）+ **CSV / レポート出力**（既存 `ReportModel` を再利用・`sfui-report-export` スキルの手順）。
- 既存の「履歴」機能（`HistoryStore`）にも `schedule` 種別で 1 行追記し、SOQL 履歴などと横断で見られるようにする（`HistoryTypes.Schedule` を追加）。

---

## 5. PC をスリープさせない（要件 4）

### 5.1 実装

`IKeepAwakeService`（Core のインターフェイス、`NoopKeepAwakeService` が既定）を新設し、UI 側で OS 実装を差し込む。

| OS | 実装 | 内容 |
| --- | --- | --- |
| Windows | `src/SfUi.App/Services/WindowsKeepAwakeService.cs` | `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED \| ES_AWAYMODE_REQUIRED)` を有効化（`ES_AWAYMODE_REQUIRED` により画面は消灯可・スリープのみ抑止）。不要になったら `ES_CONTINUOUS` のみで解除 |
| macOS | `src/SfUi.Avalonia/Services/MacKeepAwakeService.cs` | `caffeinate -i -s -w <SfUi の PID>` を子プロセス起動。解除時は `Kill()` |
| その他 | `NoopKeepAwakeService` | 何もしない |

### 5.2 有効化ポリシー

次を満たす間だけスリープ抑止を ON にする（`SchedulerService` が判定）。

- **実行中**のジョブがある、または
- 有効なジョブの `NextRunAt` が `現在 + リード時間（既定 10 分・設定可）` 以内、または
- 事前通知の送信時刻が近い

抑止していない間は即座に OFF（バッテリー消費とセキュリティ配慮）。

### 5.3 制約（明示する）

- スリープ抑止は**アイドル スリープのみ**。ユーザーが明示的にスリープ/シャットダウンした場合は防げない → 復帰時に**見逃し**扱い（要件 5）。
- OS の電源設定（AC 時のスリープ時間など）は**変更しない**。
- 抑止中はステータスバー / スケジューラ画面に「スリープ防止中」を表示し、ユーザーが認識できるようにする。

---

## 6. 通知ポップアップ（要件 7）

### 6.1 通知 3 種

| 種別 | タイミング | 文言例 |
| --- | --- | --- |
| 事前通知 | `NextRunAt - 事前通知時間` | 「まもなく実行: 毎朝 SOQL（10/07 07:00）— 3 分後」+［今すぐ実行］［閉じる］ |
| 開始 | 実行開始時（任意・既定 ON） | 「実行中: 毎朝 SOQL」 |
| 終了 | 成功 / 失敗 | 「完了: 毎朝 SOQL — 1,234 行 / 0.8 秒」/「失敗: 毎朝 SOQL — 認証エラー」 |

- **事前通知時間は「分 + 秒」で指定**（例: `0 分 30 秒前`、`5 分 0 秒前`）。ジョブごとに保持し、既定値は設定画面から一括指定（既定 1 分前）。
- **ジョブごとに「通知する / しない」**（`Notification.Enabled`）。個別に「事前だけ」「終了だけ」も選べる。
- グローバル設定で「すべての通知を止める」（集中モード）。

### 6.2 表示

- 画面右下（プライマリ モニタの作業領域）に**最大 3 枚**まで積み上げ。
- フェードイン 250ms → 保持（既定 6 秒、事前通知は 10 秒）→ **フェードアウト 600ms** → 破棄。
- クリックでスケジューラ ウィンドウの該当ジョブを開く。× で即時クローズ。失敗通知は赤、見逃しはグレー。
- WPF: `NotificationWindow`（`WindowStyle=None` / `AllowsTransparency=True` / `ShowActivated=False` / `Topmost=True` + `DoubleAnimation`）。
- Avalonia: 同じく透過 `Window` + `Transitions`（`Opacity`）。
- 通知は**スケジューラ画面を開いていなくても**出る（メイン ウィンドウの裏でも可）。

### 6.3 通知の実装形

- Core: `JobNotification`（`Kind`, `Job`, `Run`, `Message`, `At`）+ `SchedulerService.NotificationRaised` イベント。
- Presentation: `NotificationCenterViewModel`（キュー + 自動消去、`IUiDispatcher` 経由）。テストはフェイク タイマーで検証。
- UI: `IToastHost` 実装（WPF/Avalonia）。`AutomationId` を付けて UIA E2E で検証可能にする。

> OS 標準トースト（アクション センター）は初期版では使わない。理由: 追加パッケージ依存、MSIX/単一 exe 配布との相性、Avalonia 側の実装差。将来オプションとして追加可能（未決事項 4）。

---

## 7. 手動実行（要件 6）

- スケジューラ ウィンドウの［今すぐ実行］（選択ジョブ）、カレンダーの予定をダブルクリック → 実行確認、履歴の Missed 行からも実行可。
- 実行中のジョブは**再実行しない**（`Skip` として履歴に記録し、通知に「実行中のためスキップ」）。
- 実行は既定 **同時 1 本 + 待ち行列（FIFO）**。待機中は状態 `Queued`。設定 `schedulerMaxConcurrent`（1–4）で変更可。
- 手動実行は次回予定（`NextRunAt`）を**変更しない**（予定とは独立）。`Trigger = Manual` で履歴に残す。
- キャンセル: 実行中ジョブは `CancellationToken` で中断（状態 `Canceled`）。`sf` 子プロセスは kill。

---

## 8. アーキテクチャ

```
SfUi.Core
 ├ Scheduling/
 │   ├ ScheduledJob.cs / JobSchedule.cs / JobRunRecord.cs / JobNotification.cs
 │   ├ JobScheduleCalculator.cs          … 次回計算・発生列挙（純粋関数）
 │   ├ IScheduledJobExecutor.cs          … JobType / ExecuteAsync
 │   ├ SchedulerService.cs               … 1 秒 tick・キュー・見逃し検出・通知発火・スリープ抑止連携
 │   ├ ScheduleRunnerHost.cs             … アプリ起動時に Start / 終了時に Stop
 │   ├ Executors/                        … Soql / Apex / Backup / DataImport / DataExport /
 │   │                                     SfCommand / RestApi / ShellCommand / Etl / Deploy
 │   ├ IKeepAwakeService.cs              … NoopKeepAwakeService
 │   └ ShellRunner.cs                    … OS コマンドライン実行（タイムアウト・出力取得）
 └ Storage/
     ├ ScheduledJobStore.cs              … data/scheduler/jobs.json（AtomicJsonFile）
     └ JobRunStore.cs                    … data/scheduler/runs/<yyyy-MM>/*.json（保持期間で削除）

SfUi.Presentation
 ├ ViewModels/SchedulerViewModel.cs      … カレンダー月・日別予定・履歴・詳細・コマンド
 ├ ViewModels/SchedulerJobEditorViewModel.cs
 ├ ViewModels/NotificationCenterViewModel.cs
 └ UI/IAppWindowService.cs               … OpenScheduler() を追加

SfUi.App（WPF）
 ├ Views/SchedulerWindow.xaml(.cs) / SchedulerJobEditorDialog.xaml(.cs)
 ├ Views/NotificationWindow.xaml(.cs)
 └ Services/WindowsKeepAwakeService.cs / WpfToastHost.cs

SfUi.Avalonia
 └ 同名の axaml 実装 / MacKeepAwakeService.cs / AvaloniaToastHost.cs
```

### 8.1 スケジューラ ループ

- `PeriodicTimer`（**1 秒**）+ `TimeProvider`（テストでは `FakeTimeProvider`）。
- 1 tick の処理は軽量（ジョブ数 数十想定）:
  1. 起動直後フラグがあれば**見逃しスキャン**
  2. 各ジョブ: 事前通知の送信時刻を跨いだか → `NotificationRaised(Pre)`
  3. `NextRunAt <= now` → 実行キューへ投入（`NextRunAt` を次回へ進めてから投入。二重起動防止）
  4. 実行完了 → `JobRunRecord` 保存 → `NotificationRaised(Finish)`
  5. スリープ抑止の要否を再判定
- 実行は `Task.Run` + `SemaphoreSlim(maxConcurrent)`。**UI スレッドでは実行しない**。
- UI へのイベント通知は Presentation 側で `IUiDispatcher.Post(...)` に包む（Core は UI 非依存を維持）。

### 8.2 二重起動の防止

アプリが 2 つ起動していると同一ジョブが二重実行される。`data\scheduler\scheduler.lock` を排他オープンし、取得できたプロセスだけがスケジューラを起動する（取得できなければスケジューラ画面に「別の SfUi がスケジューラを実行中」と表示）。

### 8.3 スリープ復帰

WPF: `Microsoft.Win32.SystemEvents.PowerModeChanged`（`Resume`）で即時評価。
Avalonia: 復帰イベントが無いため、**tick（1 秒）が遅延していたことを検出**（前回 tick との差が 60 秒超）したら見逃しスキャン。

---

## 9. データ

### 9.1 配置

```
data/scheduler/
  jobs.json              … ジョブ定義（ScheduledJob の配列）
  jobs.json.bak
  runs/2026-10/20261006-070000-ab12cd34.json
  runs/2026-10/20261006-070000-ab12cd34.json.bak
  scheduler.lock         … 二重起動防止
```

### 9.2 ジョブ定義 JSON 例

```json
{
  "Id": "j_7f3a91c2",
  "Name": "毎朝の案件 SOQL",
  "Enabled": true,
  "JobType": "Soql",
  "TargetOrg": "hks4sand1",
  "PayloadJson": "{\"Query\":\"SELECT Id, Name FROM Opportunity WHERE CreatedDate = TODAY\",\"MaxRows\":2000}",
  "Schedule": {
    "Kind": "Weekly",
    "TimeOfDay": "07:00",
    "DaysOfWeek": ["Monday", "Wednesday", "Friday"],
    "EndAt": null
  },
  "Notification": { "Enabled": true, "PreNoticeSeconds": 180, "NotifyOnStart": true, "NotifyOnFinish": true },
  "TimeoutSeconds": 1800,
  "MissedGraceSeconds": 60,
  "NextRunAt": "2026-10-07T07:00:00+09:00",
  "LastRunId": "20261005-070000-7f3a91c2",
  "LastRunStatus": "Succeeded",
  "LastRunAt": "2026-10-05T07:00:03+09:00",
  "CreatedAt": "2026-10-06T10:00:00+09:00",
  "UpdatedAt": "2026-10-06T10:00:00+09:00"
}
```

### 9.3 設定（`AppSettings` に追加）

| キー | 既定 | 内容 |
| --- | --- | --- |
| `schedulerEnabled` | `true` | スケジューラ全体の ON/OFF |
| `schedulerNotifyDefault` | `true` | 新規ジョブの通知既定 |
| `schedulerPreNoticeMin` / `schedulerPreNoticeSec` | `1` / `0` | 新規ジョブの事前通知既定 |
| `schedulerKeepAwake` | `true` | スリープ抑止を使う |
| `schedulerKeepAwakeLeadMin` | `10` | 何分前から抑止するか |
| `schedulerMaxConcurrent` | `1` | 同時実行数 |
| `schedulerMissedGraceSec` | `60` | 見逃し猶予の既定 |
| `schedulerRunRetentionDays` | `90` | 履歴保持日数 |
| `schedulerRunRetentionMax` | `500` | 履歴最大件数 |
| `schedulerAllowShellCommand` | `false` | OS コマンドライン種別の許可（未決事項 5） |

---

## 10. 画面設計

### 10.1 スケジューラ ウィンドウ（非モーダル・1 ウィンドウ + 2 タブ）

```
┌ スケジュール ────────────────────────────────────────────────────────────┐
│ [＋ 新規ジョブ] [今すぐ実行] [有効/無効] [更新]  検索:[______]  [レポート出力] │
│ ┌ カレンダー ┐┌ 実行履歴 ┐                                               │
│ ◀ 2026 年 10 月 ▶  [今日]                          ● 成功 ● 失敗 ● 見逃し  │
│ 日  月  火  水  木  金  土                                                │
│              1   2   3                                                   │
│  4   5  [6]  7   8   9  10      ← 選択日をハイライト                      │
│ 11  12  13  14  15  16  17                                               │
│ …                                                                        │
│ ┌ 10/07（水）の予定 ─────────────────────────────────────────────────┐   │
│ │ 07:00  毎朝の案件 SOQL      SOQL     次回 10/07 07:00   [今すぐ実行] │   │
│ │ 09:00  月次バックアップ     BackUp   次回 11/01 09:00   [今すぐ実行] │   │
│ └──────────────────────────────────────────────────────────────────┘   │
│ ┌ 選択ジョブの詳細 ──────────────────────────────────────────────────┐   │
│ │ 種別: SOQL / 対象: hks4sand1 / 通知: 3 分前 + 終了 / 前回: 成功 0.8 秒 │   │
│ └──────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
```

- カレンダーは**月表示**（前月/翌月/今日、日付セルに時刻 + ジョブ名のチップ）。
- 日付クリック → その日の予定一覧、予定クリック → 詳細、ダブルクリック → 編集ダイアログ、右クリック → 今すぐ実行 / 有効・無効 / 複製 / 削除 / 次回をスキップ。
- 「次回をスキップ」= `NextRunAt` を次回へ進め、履歴に `Skipped` を記録。
- 週表示（7 列の時刻表 + ドラッグで移動）は **Phase 3 の追加候補**（未決事項 2）。
- ステータスバー: 「スケジューラ稼働中 / スリープ防止中 / 次回 07:00（あと 3 時間）」。

### 10.2 実行履歴タブ

```
┌ 実行履歴 ────────────────────────────────────────────────────────────────┐
│ 検索:[________] 状態:[すべて ▼] 期間:[直近 7 日 ▼]        [CSV] [レポート] │
│ 時刻             ジョブ名             種別    状態      所要   対象組織      │
│ 10/06 07:00:03  毎朝の案件 SOQL       SOQL    成功      0.8s  hks4sand1    │
│ 10/05 18:30:00  月次バックアップ      Backup  失敗      2.1s  hks4sand1    │
│ 10/05 07:00:00  毎朝の案件 SOQL       SOQL    見逃し    —     hks4sand1    │
│ ┌ 詳細 ──────────────────────────────────────────────────────────────┐   │
│ │ 結果サマリ / エラー / 出力全文（先頭 4,000 文字 + 全文ファイルを開く） │   │
│ └────────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
```

### 10.3 ジョブ編集ダイアログ

```
┌ ジョブの編集 ───────────────────────────────────────────────────────────┐
│ 名前: [毎朝の案件 SOQL                    ]  種別: [SOQL 実行 ▼]        │
│ 対象組織: [hks4sand1 (Sandbox) ▼]                         [□ 有効]      │
│ ── 実行内容 ────────────────────────────────────────────────────────── │
│ [ SOQL エディタ（既存のハイライト付き）                              ]   │
│ 最大行数: [2000]   □ 結果を CSV に保存 [参照…]                         │
│ ── スケジュール ────────────────────────────────────────────────────── │
│ (•) 1 回   ( ) 毎日   ( ) 毎週   ( ) 毎月   ( ) 間隔                    │
│   日付 [2026/10/07]  時刻 [07:00]                                       │
│ ── 通知 ────────────────────────────────────────────────────────────── │
│ [x] 通知する   事前通知 [3] 分 [0] 秒前   [x] 開始時  [x] 終了時        │
│ ── 詳細 ────────────────────────────────────────────────────────────── │
│ タイムアウト [30] 分   見逃し猶予 [60] 秒   □ 土日スキップ              │
│                                        [保存] [保存して今すぐ実行] [×]  │
└──────────────────────────────────────────────────────────────────────────┘
```

- 種別を変えると「実行内容」パネルが切り替わる（SOQL/Apex は既存の `AvaloniaEdit` / WPF エディタ + 補完を再利用）。
- 通知の「分 + 秒」は数値入力（0–59 分 / 0–59 秒）。0 分 0 秒は「実行時刻ちょうど」。
- 削除は確認ダイアログ（既存 `IDialogService`）。編集は `data\scheduler\jobs.json` に即時保存。

### 10.4 通知ポップアップ

```
                                  ┌─────────────────────────────────┐
                                  │ ⏰ まもなく実行                   │
                                  │ 毎朝の案件 SOQL                  │
                                  │ 10/07 07:00（あと 3 分）          │
                                  │            [今すぐ実行] [閉じる] │
                                  └─────────────────────────────────┘
```

### 10.5 メイン ウィンドウの入口

- 上部ツールバーに 🗓 ボタン（`AutomationId="OpenScheduler"`）を追加。ツールチップ「スケジュール」。
- ステータスバーに「次回: 07:00 毎朝の案件 SOQL」を常時表示（クリックでスケジューラを開く）。
- 見逃しがある場合は ⚠ バッジ。

---

## 11. ローカライズ

`UiText.{En,Ja,Zh,Ko}.cs` に `Sched_*` を追加（4 言語同一キー集合）。

| グループ | キー例 |
| --- | --- |
| 画面 | `Sched_Title`, `Sched_TabCalendar`, `Sched_TabHistory`, `Sched_NewJob`, `Sched_RunNow`, `Sched_Enable`, `Sched_Disable`, `Sched_Delete`, `Sched_Duplicate`, `Sched_SkipNext`, `Sched_Today`, `Sched_PrevMonth`, `Sched_NextMonth`, `Sched_StatusBarFmt` |
| 種別 | `Sched_TypeSoql`, `Sched_TypeApex`, `Sched_TypeBackup`, `Sched_TypeDataImport`, `Sched_TypeDataExport`, `Sched_TypeSfCommand`, `Sched_TypeRestApi`, `Sched_TypeShell`, `Sched_TypeEtl`, `Sched_TypeDeploy` |
| スケジュール | `Sched_KindOnce`, `Sched_KindDaily`, `Sched_KindWeekly`, `Sched_KindMonthly`, `Sched_KindInterval`, `Sched_WeekdayMon`…`Sun`, `Sched_LastDayOfMonth`, `Sched_IntervalEveryMinFmt`, `Sched_NextRunFmt`, `Sched_EndsAtFmt` |
| 状態 | `Sched_StatusRunning`, `Sched_StatusQueued`, `Sched_StatusSucceeded`, `Sched_StatusFailed`, `Sched_StatusMissed`, `Sched_StatusCanceled`, `Sched_StatusSkipped` |
| 通知 | `Sched_NotifySection`, `Sched_NotifyEnabled`, `Sched_NotifyOnStart`, `Sched_NotifyOnFinish`, `Sched_PreNoticeFmt`（`{0} 分 {1} 秒前`）, `Sched_ToastPreTitleFmt`, `Sched_ToastStartTitleFmt`, `Sched_ToastDoneTitleFmt`, `Sched_ToastFailedTitleFmt`, `Sched_ToastClose`, `Sched_ToastRunNow` |
| その他 | `Sched_ConfirmDeleteFmt`, `Sched_TimeoutFmt`, `Sched_MissedGraceFmt`, `Sched_SkipWeekend`, `Sched_MissedBannerFmt`, `Sched_KeepAwakeActive`, `Sched_LockHeldByOther`, `Sched_ShellWarning`, `Sched_SummarySoqlFmt`, `Sched_SummaryApexFmt`, `Sched_SummaryBackupFmt`, `Sched_SummaryImportFmt`, `Sched_SummaryCommandFmt`, `Sched_SummaryRestFmt` |

---

## 12. テスト計画

| レベル | 対象 | 内容 |
| --- | --- | --- |
| 単体（Core） | `JobScheduleCalculator` | Once/Daily/Weekly/Monthly/Interval、月末（31 日 → 2/28）、`EndAt`、`OccurrencesBetween`、位相の維持 |
| 単体（Core） | `SchedulerService`（`FakeTimeProvider`） | 予定時刻で 1 回だけ実行、二重起動なし、事前通知の時刻（秒単位）、見逃し → `Missed` 記録・**再実行しない**、手動実行、同時実行ガード、タイムアウト、キャンセル |
| 単体（Core） | `ScheduledJobStore` / `JobRunStore` | 往復、破損時 `.bak` 復旧、保持期間削除、`AtomicJsonFile` 経由 |
| 単体（Core） | Executors（フェイク サービス） | SOQL/Apex/Backup/DataImport/SfCommand/RestApi のパラメータ受け渡しと結果サマリ、失敗時の `Error` |
| 単体（Presentation） | `SchedulerViewModel` / `SchedulerJobEditorViewModel` / `NotificationCenterViewModel` | カレンダー生成、日別予定、コマンド、通知キュー（自動消去）、バリデーション |
| ローカライズ | 4 辞書 | キー集合一致・参照キー存在（既存テストが自動検出） |
| UIA E2E（WPF/Avalonia） | `SchedulerWindow` | 新規ジョブ作成（1 分後）→ 事前通知トースト → 実行 → 終了トースト → 履歴行、Missed 表示、「今すぐ実行」、AutomationId 検証 |

- 既存 1,212 テストに追加（目安 +60〜80 件）。
- E2E スクリプトは `C:\huqian\sfui-scheduler-e2e.ps1`（WPF）と `-avalonia` 版。実行待ちは `Start-Sleep` ではなく UIA ポーリング（既存の `Wait-Element` パターン）。

---

## 13. 実装フェーズと作業順

| Phase | スコープ | 目安 |
| --- | --- | --- |
| **1. 基盤** | Core モデル / `JobScheduleCalculator` / ストア / `SchedulerService` / キュー / 見逃し判定 / SOQL + SfCommand Executor / 実行履歴（読み書き）/ Presentation VM | 中 |
| **2. UI（WPF）** | スケジューラ ウィンドウ（カレンダー月 + 日別予定 + 履歴）/ ジョブ編集ダイアログ / 通知ウィンドウ / スリープ抑止 / メイン導線 | 中 |
| **3. 種別拡充** | Apex / Backup / DataImport / REST / （任意で Shell）Executor + 各エディタ UI | 中 |
| **4. Avalonia 反映** | 同名ウィンドウ・ダイアログ・トースト・`MacKeepAwakeService` / スモーク確認 | 中 |
| **5. 仕上げ** | 履歴のレポート出力 / 保持期間削除 / 4 言語辞書 / README・マニュアル（4 言語）/ E2E / CI | 小 |

- 各 Phase でビルド + 全テスト + smokes（WPF/Avalonia）を通す。Phase 4 完了時点で WPF と Avalonia の機能差ゼロ（`copilot-instructions.md` の非交渉事項 3）。
- 追加候補（Phase 6 以降）: データ エクスポート / デプロイ検証 / ETL ジョブ / 週表示・ドラッグ移動 / OS 標準トースト / メール・Slack 通知。

---

## 14. リスクと対策

| リスク | 対策 |
| --- | --- |
| 二重起動で同じジョブが 2 回走る | ロック ファイルで排他。取得できない側はスケジューラ UI を読み取り専用 + 警告表示 |
| 通知の遅延（UI スレッド多忙） | tick は 1 秒、通知は `IUiDispatcher` 経由で非同期。実行はバックグラウンド |
| スリープ復帰後に一斉実行される | 復帰時は**見逃し**扱い（要件 5）。自動実行は復帰後の次回予定のみ |
| 大量の見逃しで UI が埋まる | 履歴は 1 ジョブ 1 行に集約（`Missed` + 件数）、上限 500 件で要約 |
| `ShellCommand` の悪用・誤操作 | 設定で許可制（既定 OFF）+ 編集時の警告 + 危険アイコン |
| 長時間ジョブが次回予定と重なる | 同時実行ガードで `Skip`。設定で `Queue`（順次実行）も選択可 |
| 出力が巨大（SOQL 100 万行など） | 履歴には先頭 4,000 文字のみ、全文は別ファイル。Executors で上限を強制 |
| Avalonia の透過ウィンドウ差異 | 透過 `Window` + `Opacity` のみ使用（プラットフォーム API 依存なし）。E2E で実機確認 |

---

## 15. 未決事項（確認したい点）

1. **アプリ未起動時**: 実行せず「見逃し」記録のみで良いか（Windows タスク スケジューラ登録や常駐サービスは不要か）。
2. **カレンダー表示**: 月表示 + 日別予定で十分か。週表示（時刻表・ドラッグ移動）も必要か。
3. **繰り返し**: 毎日 / 毎週 / 毎月 / N 分間隔で十分か。Cron 式・営業日のみ・祝日除外は要るか。
4. **通知手段**: アプリ内の最前面フェード ポップアップで良いか（OS 標準トースト、メール・Slack 連携は要るか）。
5. **OS コマンドライン種別**: 実装して良いか（既定 OFF・警告付き）。
6. **履歴保持**: 既定 90 日 / 最大 500 件で良いか。

> 1〜4 は 1.0 の確定事項で解決済み。5（シェル種別）と 6（保持期間）は実装済みの既定値（シェル種別は未実装・保持は 90 日 / 500 件で設定変更可）で進めている。

---

## 16. 実装状況（Phase 1〜2）

### 16.1 追加・変更したファイル

| 層 | ファイル | 内容 |
| --- | --- | --- |
| Core | `Scheduling/SchedulerModels.cs` | `ScheduledJob` / `JobSchedule` / `JobRunRecord` / `JobNotification` / 種別・状態・契機の定数、種別ごとのペイロード型 |
| Core | `Scheduling/JobScheduleCalculator.cs` | 次回発火・範囲内発火の計算（純関数）、営業日フィルタ |
| Core | `Scheduling/JapanHolidayCalendar.cs` | 日本の祝日（2020 年以降。振替休日・国民の休日・オリンピック特例込み） |
| Core | `Scheduling/SchedulerService.cs` | 1 秒ティック、見逃し検出、実行キュー（同時実行制御）、タイムアウト、通知発火、スリープ抑止判定、二重起動ロック |
| Core | `Scheduling/IScheduledJobExecutor.cs` / `Executors/SoqlJobExecutor.cs` / `Executors/SfCommandJobExecutor.cs` | ジョブ種別の実行エンジン（Phase 1 は SOQL と sf コマンド） |
| Core | `Scheduling/IKeepAwakeService.cs` / `Scheduling/NotificationPlacement.cs` | スリープ抑止の抽象化（既定は Noop）と通知位置計算 |
| Core | `Storage/ScheduledJobStore.cs` / `Storage/JobRunStore.cs` | `data/scheduler/jobs.json` と `data/scheduler/runs/<yyyy-MM>/*.json`（`AtomicJsonFile`） |
| Core | `AppPaths` / `AppSettingsStore` / `HistoryStore` | `SchedulerRoot` / `SchedulerJobsFile` / `SchedulerRunsRoot` / `SchedulerLockFile`、`Scheduler*` 設定 10 項目、`HistoryTypes.Schedule` |
| Presentation | `ViewModels/SchedulerViewModel.cs` | カレンダー（42 セル）・日別予定・履歴・コマンド・通知センター連携 |
| Presentation | `ViewModels/SchedulerJobEditorViewModel.cs` | ジョブ編集（種別・スケジュール・通知・タイムアウト・見逃し猶予）と入力検証 |
| Presentation | `ViewModels/NotificationCenterViewModel.cs` | 最大 3 件のフェード ポップアップ キュー（自動消去） |
| Presentation | `UI/IAppWindowService.cs` / `ViewModels/MainViewModel.cs` | `OpenScheduler` とメイン上部バーの導線 |
| WPF | `Views/SchedulerWindow.xaml` / `Views/SchedulerJobEditorDialog.xaml` / `Views/NotificationWindow.xaml` | カレンダー + 履歴 UI、編集ダイアログ、右下の最前面トースト |
| WPF | `Services/WindowsKeepAwakeService.cs` / `Services/WpfToastPresenter.cs` / `Services/SchedulerWindowFactory.cs` / `Services/WpfSchedulerJobEditor.cs` / `Services/StringEqualsConverter.cs` | スリープ抑止、通知配信、ウィンドウ生成、編集ダイアログ、ラジオボタン用変換 |
| Avalonia | `Services/AvaloniaAppWindowService.cs` | `OpenScheduler` は未移植の案内（Phase 4 で実装） |
| 辞書 | `UiText.{En,Ja,Zh,Ko}.cs` | `Sched_*` 約 100 キー + `Type_Schedule`（4 言語同一キー集合） |
| テスト | `SchedulerScheduleTests` / `SchedulerStoreTests` / `SchedulerServiceTests` / `SchedulerViewModelTests` | 56 件（全 1,268 件グリーン） |

### 16.2 実装済みの挙動

- **カレンダー**: 月表示（日曜始まり 42 セル、今日・祝日・選択日を色分け、当日の予定時刻チップ）、◀ ▶ / 今日、選択日の予定一覧とジョブ詳細。
- **ジョブ編集**: 種別（SOQL / sf コマンド）、対象組織、スケジュール（1 回 / 毎日 / 毎週 / 毎月 / 間隔 + 土日・祝日スキップ）、通知（ON/OFF・事前通知の分/秒・開始時・終了時）、タイムアウト、見逃し猶予。過去日時や未入力は保存時に検証してエラー表示。
- **実行**: 1 秒ティックで予定時刻を判定し、バックグラウンド実行（同時実行数は設定 1〜4、超えた分は待機）。タイムアウトでキャンセル。
- **見逃し**: 起動時・スリープ復帰後（ティック遅延）に `Missed` として履歴記録し、**自動再実行しない**（1 ジョブあたり 1 回のスキャンで最大 20 件、超過分は読み飛ばし）。UI から「今すぐ実行」可能。
- **手動実行**: 選択ジョブの「今すぐ実行」、実行中の多重実行はスキップ。`Trigger = Manual` で履歴に残る。
- **通知**: 事前（分 + 秒で指定）/ 開始 / 終了 / 見逃しの 4 種。右下に最大 3 枚、フェードイン 0.25 秒 → 保持（通常 6 秒 / 事前 10 秒）→ フェードアウト 0.6 秒。ジョブごとに通知 ON/OFF・開始/終了の個別指定。
- **スリープ抑止**: 実行中、または次回実行が 10 分前以内のときだけ Windows の `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED)` を有効化し、それ以外は解除。ステータスバーに「スリープ防止中」を表示。
- **履歴**: 保持は既定 90 日 / 500 件（設定可）。履歴タブで一覧・詳細（出力抜粋）を表示。
- **二重起動**: `data/scheduler/scheduler.lock` を排他オープンし、取得できたプロセスだけがスケジューラを開始（取得できない場合は画面に警告を表示）。

### 16.3 検証

- `dotnet build SfUi.sln -c Debug` → 成功
- `dotnet test` → **1,268 件すべて成功**（+56 件）
- WPF / Avalonia の `--smoke` → 終了コード 0
- UIA E2E `C:\\huqian\\sfui-scheduler-e2e.ps1` → **13/13 PASS**（起動時の見逃し記録、事前/開始/終了トースト、履歴 2 行、カレンダー描画、編集ダイアログの表示）

### 16.4 残タスク（Phase 3〜5）

1. ジョブ種別の拡充（Apex / バックアップ / データ投入 / データエクスポート / REST API / シェル）
2. Avalonia への移植（カレンダー / 編集ダイアログ / トースト / `MacKeepAwakeService`）
3. 履歴のレポート出力（CSV / Excel / PDF）と週表示
4. マニュアル（`docs/manual/` 4 言語）とスクリーンショットの更新

