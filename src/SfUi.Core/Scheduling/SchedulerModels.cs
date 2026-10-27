using System.Text.Json;

namespace SfUi.Core;

/// <summary>スケジュールの繰り返し種別。</summary>
public static class ScheduleKinds
{
    /// <summary>1 回だけ。</summary>
    public const string Once = "once";

    /// <summary>毎日。</summary>
    public const string Daily = "daily";

    /// <summary>毎週（曜日指定）。</summary>
    public const string Weekly = "weekly";

    /// <summary>毎月（日付指定）。</summary>
    public const string Monthly = "monthly";

    /// <summary>N 分 / N 時間ごと。</summary>
    public const string Interval = "interval";

    public static string ToLabel(string kind) => kind switch
    {
        Once => UiText.T("Sched_KindOnce"),
        Daily => UiText.T("Sched_KindDaily"),
        Weekly => UiText.T("Sched_KindWeekly"),
        Monthly => UiText.T("Sched_KindMonthly"),
        Interval => UiText.T("Sched_KindInterval"),
        _ => kind,
    };
}

/// <summary>スケジュール ジョブの種別。</summary>
public static class ScheduledJobTypes
{
    public const string Soql = "soql";
    public const string SfCommand = "sfcommand";

    public static string ToLabel(string type) => type switch
    {
        Soql => UiText.T("Sched_TypeSoql"),
        SfCommand => UiText.T("Sched_TypeSfCommand"),
        _ => type,
    };
}

/// <summary>ジョブ実行の状態。</summary>
public static class JobRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Missed = "missed";
    public const string Canceled = "canceled";
    public const string Skipped = "skipped";

    public static string ToLabel(string status) => status switch
    {
        Queued => UiText.T("Sched_StatusQueued"),
        Running => UiText.T("Sched_StatusRunning"),
        Succeeded => UiText.T("Sched_StatusSucceeded"),
        Failed => UiText.T("Sched_StatusFailed"),
        Missed => UiText.T("Sched_StatusMissed"),
        Canceled => UiText.T("Sched_StatusCanceled"),
        Skipped => UiText.T("Sched_StatusSkipped"),
        _ => status,
    };

    /// <summary>完了状態（履歴として確定している）か。</summary>
    public static bool IsFinished(string status) =>
        status is Succeeded or Failed or Missed or Canceled or Skipped;
}

/// <summary>ジョブの実行契機。</summary>
public static class JobRunTriggers
{
    public const string Schedule = "schedule";
    public const string Manual = "manual";

    public static string ToLabel(string trigger) => trigger switch
    {
        Manual => UiText.T("Sched_TriggerManual"),
        _ => UiText.T("Sched_TriggerSchedule"),
    };
}

/// <summary>通知の種類。</summary>
public static class JobNotificationKinds
{
    /// <summary>実行前の予告。</summary>
    public const string Pre = "pre";

    /// <summary>実行開始。</summary>
    public const string Start = "start";

    /// <summary>実行終了（成功 / 失敗）。</summary>
    public const string Finish = "finish";

    /// <summary>見逃し。</summary>
    public const string Missed = "missed";
}

/// <summary>繰り返しスケジュールの定義。時刻はローカル時刻として扱う。</summary>
public sealed class JobSchedule
{
    /// <summary>繰り返し種別（<see cref="ScheduleKinds"/>）。</summary>
    public string Kind { get; set; } = ScheduleKinds.Once;

    /// <summary>1 回だけ実行する日時。</summary>
    public DateTimeOffset? OnceAt { get; set; }

    /// <summary>毎日 / 毎週 / 毎月の実行時刻（"HH:mm" または "HH:mm:ss"）。</summary>
    public string TimeOfDay { get; set; } = "09:00";

    /// <summary>毎週の曜日（空 = 起点日と同じ曜日）。</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>毎月の日付（1-31・31 は月末扱い。空 = 起点日と同じ日）。</summary>
    public List<int> DaysOfMonth { get; set; } = new();

    /// <summary>間隔実行の間隔（分）。</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>間隔実行 / 繰り返しの起点（null = 作成日時）。</summary>
    public DateTimeOffset? StartAt { get; set; }

    /// <summary>繰り返しの終了日時（null = 無期限）。</summary>
    public DateTimeOffset? EndAt { get; set; }

    /// <summary>土日をスキップする。</summary>
    public bool SkipWeekends { get; set; }

    /// <summary>日本の祝日をスキップする。</summary>
    public bool SkipHolidays { get; set; }

    /// <summary>実行時刻（時分秒）を返す。解析できない場合は 09:00。</summary>
    public TimeOnly GetTimeOfDay() => TryParseTimeOfDay(TimeOfDay, out var time) ? time : new TimeOnly(9, 0);

    /// <summary>"HH:mm" / "HH:mm:ss" を解析する。</summary>
    public static bool TryParseTimeOfDay(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return TimeOnly.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);
    }

    /// <summary>表示用のスケジュール文字列。</summary>
    public string Describe()
    {
        var time = GetTimeOfDay().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return Kind switch
        {
            ScheduleKinds.Daily => UiText.T("Sched_DescribeDailyFmt", time),
            ScheduleKinds.Weekly => UiText.T("Sched_DescribeWeeklyFmt", string.Join(UiText.T("Sched_WeekdaySeparator"), DaysOfWeek.Select(WeekdayLabel)), time),
            ScheduleKinds.Monthly => UiText.T("Sched_DescribeMonthlyFmt", string.Join(UiText.T("Sched_DaySeparator"), DaysOfMonth.OrderBy(d => d).Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture))), time),
            ScheduleKinds.Interval => IntervalMinutes % 60 == 0 && IntervalMinutes >= 60
                ? UiText.T("Sched_DescribeIntervalHourFmt", IntervalMinutes / 60)
                : UiText.T("Sched_DescribeIntervalMinuteFmt", IntervalMinutes),
            _ => UiText.T("Sched_DescribeOnceFmt", (OnceAt ?? DateTimeOffset.Now).LocalDateTime.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.CurrentCulture)),
        };
    }

    /// <summary>曜日の短縮ラベル。</summary>
    public static string WeekdayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => UiText.T("Sched_WeekdayMon"),
        DayOfWeek.Tuesday => UiText.T("Sched_WeekdayTue"),
        DayOfWeek.Wednesday => UiText.T("Sched_WeekdayWed"),
        DayOfWeek.Thursday => UiText.T("Sched_WeekdayThu"),
        DayOfWeek.Friday => UiText.T("Sched_WeekdayFri"),
        DayOfWeek.Saturday => UiText.T("Sched_WeekdaySat"),
        _ => UiText.T("Sched_WeekdaySun"),
    };
}

/// <summary>ジョブごとの通知設定。</summary>
public sealed class JobNotificationSettings
{
    /// <summary>通知するか。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>実行何秒前に予告するか（0 = 予告しない）。</summary>
    public int PreNoticeSeconds { get; set; } = 60;

    /// <summary>実行開始時に通知するか。</summary>
    public bool NotifyOnStart { get; set; } = true;

    /// <summary>実行終了時に通知するか。</summary>
    public bool NotifyOnFinish { get; set; } = true;
}

/// <summary>スケジュールされた 1 ジョブの定義。</summary>
public sealed class ScheduledJob
{
    public string Id { get; set; } = "";

    /// <summary>表示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>有効 / 無効。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ジョブ種別（<see cref="ScheduledJobTypes"/>）。</summary>
    public string JobType { get; set; } = ScheduledJobTypes.Soql;

    /// <summary>対象組織（sf の username）。</summary>
    public string? TargetOrg { get; set; }

    /// <summary>種別ごとのパラメータ（JSON）。</summary>
    public string PayloadJson { get; set; } = "{}";

    public JobSchedule Schedule { get; set; } = new();

    public JobNotificationSettings Notification { get; set; } = new();

    /// <summary>タイムアウト（秒）。0 = 無制限。</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>見逃し判定の猶予（秒）。</summary>
    public int MissedGraceSeconds { get; set; } = 60;

    /// <summary>次回実行日時（スケジュール計算のキャッシュ）。</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public string? LastRunStatus { get; set; }

    public string? LastRunSummary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>新しいジョブ ID を発行する。</summary>
    public static string NewId() => "j_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>種別ごとのパラメータを取り出す（未設定なら既定値）。</summary>
    public T GetPayload<T>()
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(PayloadJson))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(PayloadJson, AtomicJsonFile.Options) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    /// <summary>種別ごとのパラメータを JSON として設定する。</summary>
    public void SetPayload<T>(T payload)
        where T : class => PayloadJson = JsonSerializer.Serialize(payload, AtomicJsonFile.Options);

    /// <summary>表示用の名前（未入力なら種別名）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? ScheduledJobTypes.ToLabel(JobType) : Name;
}

/// <summary>jobs.json のルート要素。</summary>
public sealed class ScheduledJobDocument
{
    public int Version { get; set; } = 1;

    public List<ScheduledJob> Jobs { get; set; } = new();
}

/// <summary>ジョブ 1 回分の実行履歴。</summary>
public sealed class JobRunRecord
{
    public string RunId { get; set; } = "";

    public string JobId { get; set; } = "";

    public string JobName { get; set; } = "";

    public string JobType { get; set; } = "";

    public string? TargetOrg { get; set; }

    /// <summary>実行契機（<see cref="JobRunTriggers"/>）。</summary>
    public string Trigger { get; set; } = JobRunTriggers.Schedule;

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>状態（<see cref="JobRunStatuses"/>）。</summary>
    public string Status { get; set; } = JobRunStatuses.Queued;

    /// <summary>1 行サマリ。</summary>
    public string Summary { get; set; } = "";

    /// <summary>失敗理由。</summary>
    public string? Error { get; set; }

    /// <summary>出力の抜粋（先頭 4,000 文字）。</summary>
    public string? OutputPreview { get; set; }

    /// <summary>出力全文のファイル パス（大きい場合のみ）。</summary>
    public string? OutputPath { get; set; }

    public double DurationSeconds { get; set; }

    /// <summary>状態の表示ラベル。</summary>
    public string StatusLabel => JobRunStatuses.ToLabel(Status);

    /// <summary>種別の表示ラベル。</summary>
    public string TypeLabel => ScheduledJobTypes.ToLabel(JobType);

    /// <summary>実行契機の表示ラベル。</summary>
    public string TriggerLabel => JobRunTriggers.ToLabel(Trigger);

    /// <summary>実行日時の表示文字列。</summary>
    public string StartedAtText => StartedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>所要時間の表示文字列。</summary>
    public string DurationText => FinishedAt is null && Status == JobRunStatuses.Running
        ? UiText.T("Sched_RunningLabel")
        : (DurationSeconds < 60
            ? UiText.T("Sched_DurationSecondsFmt", DurationSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture))
            : UiText.T("Sched_DurationMinutesFmt", (DurationSeconds / 60).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)));
}

/// <summary>Executor の実行結果。</summary>
public sealed record JobRunResult(bool Success, string Summary, string? Output = null, string? Error = null)
{
    public static JobRunResult Ok(string summary, string? output = null) => new(true, summary, output);

    public static JobRunResult Fail(string summary, string? error = null, string? output = null) => new(false, summary, output, error);
}

/// <summary>UI へ通知する内容。</summary>
public sealed class JobNotification
{
    /// <summary>通知の種類（<see cref="JobNotificationKinds"/>）。</summary>
    public string Kind { get; set; } = JobNotificationKinds.Finish;

    public string JobId { get; set; } = "";

    public string JobName { get; set; } = "";

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public DateTimeOffset At { get; set; }

    /// <summary>失敗通知か（UI の色分け用）。</summary>
    public bool IsError { get; set; }
}

/// <summary>SOQL ジョブのパラメータ。</summary>
public sealed class SoqlJobPayload
{
    public string Query { get; set; } = "";

    /// <summary>結果を CSV に保存するパス（null = 保存しない）。</summary>
    public string? CsvPath { get; set; }
}

/// <summary>Salesforce CLI コマンド ジョブのパラメータ。</summary>
public sealed class SfCommandJobPayload
{
    /// <summary>`sf` に続く引数（例: "org list --json"）。</summary>
    public string Arguments { get; set; } = "";

    /// <summary>作業ディレクトリ（null = 既定）。</summary>
    public string? WorkingDirectory { get; set; }
}
