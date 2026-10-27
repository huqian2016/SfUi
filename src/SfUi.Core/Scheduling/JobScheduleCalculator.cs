namespace SfUi.Core;

/// <summary>
/// 繰り返しスケジュールから次回 / 範囲内の発火時刻を計算する純粋関数群。
/// 時刻はローカル時刻（壁時計）で扱い、夏時間の不正時刻は 1 時間後ろへ補正する。
/// </summary>
public static class JobScheduleCalculator
{
    /// <summary>繰り返し探索の上限日数（約 5 年）。</summary>
    private const int MaxSearchDays = 366 * 5;

    /// <summary>営業日スキップで候補を捨て続けた場合の上限回数。</summary>
    private const int MaxSkipIterations = 600;

    /// <summary>
    /// <paramref name="after"/> より後に来る直近の発火時刻を返す（無ければ null）。
    /// </summary>
    public static DateTimeOffset? NextOccurrence(
        JobSchedule schedule,
        DateTimeOffset after,
        DateTimeOffset now,
        IHolidayCalendar? holidays = null)
    {
        var cursor = after;
        var horizon = after.AddDays(MaxSearchDays);

        for (var i = 0; i < MaxSkipIterations; i++)
        {
            if (RawNext(schedule, cursor, now) is not { } candidate || candidate > horizon)
            {
                return null;
            }

            if (schedule.StartAt is { } start && candidate < start)
            {
                cursor = start.AddTicks(-1);
                continue;
            }

            if (schedule.EndAt is { } end && candidate > end)
            {
                return null;
            }

            if (schedule.Kind != ScheduleKinds.Once && !IsAllowed(candidate, schedule, holidays))
            {
                cursor = candidate;
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// (<paramref name="fromExclusive"/>, <paramref name="toInclusive"/>] の範囲の発火時刻を列挙する。
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> OccurrencesBetween(
        JobSchedule schedule,
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        DateTimeOffset now,
        IHolidayCalendar? holidays = null,
        int maxCount = 500)
    {
        var results = new List<DateTimeOffset>();
        if (maxCount <= 0 || toInclusive <= fromExclusive)
        {
            return results;
        }

        var cursor = fromExclusive;
        while (results.Count < maxCount)
        {
            var next = NextOccurrence(schedule, cursor, now, holidays);
            if (next is null || next > toInclusive)
            {
                break;
            }

            results.Add(next.Value);
            cursor = next.Value;
        }

        return results;
    }

    /// <summary>営業日フィルタ（土日スキップ / 祝日スキップ）を通るか。</summary>
    public static bool IsAllowed(DateTimeOffset candidate, JobSchedule schedule, IHolidayCalendar? holidays)
    {
        if (!schedule.SkipWeekends && !schedule.SkipHolidays)
        {
            return true;
        }

        var date = DateOnly.FromDateTime(candidate.LocalDateTime);
        if (schedule.SkipWeekends && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        if (schedule.SkipHolidays)
        {
            var calendar = holidays ?? JapanHolidayCalendar.Instance;
            if (calendar.IsHoliday(date))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>営業日か（土日・祝日を除く）。カレンダー表示の色分けにも使う。</summary>
    public static bool IsBusinessDay(DateOnly date, IHolidayCalendar? holidays = null) =>
        (holidays ?? JapanHolidayCalendar.Instance).IsBusinessDay(date);

    private static DateTimeOffset? RawNext(JobSchedule schedule, DateTimeOffset after, DateTimeOffset now) =>
        schedule.Kind switch
        {
            ScheduleKinds.Once => OnceNext(schedule, after),
            ScheduleKinds.Interval => IntervalNext(schedule, after, now),
            _ => CalendarNext(schedule, after, now),
        };

    private static bool MatchesDate(JobSchedule schedule, DateOnly date, DateTimeOffset now)
    {
        var start = (schedule.StartAt ?? now).LocalDateTime;
        return schedule.Kind switch
        {
            ScheduleKinds.Daily => true,
            ScheduleKinds.Weekly => MatchesWeekly(schedule, date, start),
            ScheduleKinds.Monthly => MatchesMonthly(schedule, date, start),
            _ => false,
        };
    }

    private static bool MatchesWeekly(JobSchedule schedule, DateOnly date, DateTime start) =>
        schedule.DaysOfWeek.Count == 0
            ? date.DayOfWeek == start.DayOfWeek
            : schedule.DaysOfWeek.Contains(date.DayOfWeek);

    private static bool MatchesMonthly(JobSchedule schedule, DateOnly date, DateTime start)
    {
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        if (schedule.DaysOfMonth.Count == 0)
        {
            return date.Day == Math.Min(start.Day, daysInMonth);
        }

        foreach (var day in schedule.DaysOfMonth)
        {
            if (day is < 1 or > 31)
            {
                continue;
            }

            if (date.Day == Math.Min(day, daysInMonth))
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset? OnceNext(JobSchedule schedule, DateTimeOffset after) =>
        schedule.OnceAt is { } at && at > after ? at : null;

    private static DateTimeOffset? IntervalNext(JobSchedule schedule, DateTimeOffset after, DateTimeOffset now)
    {
        var start = schedule.StartAt ?? now;
        var intervalMinutes = schedule.IntervalMinutes > 0 ? schedule.IntervalMinutes : 60;
        var step = TimeSpan.FromMinutes(intervalMinutes);

        if (after < start)
        {
            return start;
        }

        var elapsed = after - start;
        var steps = (long)Math.Floor(elapsed.Ticks / (double)step.Ticks) + 1;
        return start + TimeSpan.FromTicks(step.Ticks * steps);
    }

    private static DateTimeOffset? CalendarNext(JobSchedule schedule, DateTimeOffset after, DateTimeOffset now)
    {
        var time = schedule.GetTimeOfDay();
        var date = DateOnly.FromDateTime(after.LocalDateTime);
        var startDate = DateOnly.FromDateTime((schedule.StartAt ?? now).LocalDateTime);
        if (date < startDate)
        {
            date = startDate;
        }

        for (var i = 0; i < MaxSearchDays; i++)
        {
            var candidateDate = date.AddDays(i);
            if (!MatchesDate(schedule, candidateDate, now))
            {
                continue;
            }

            var candidate = AtLocal(candidateDate, time);
            if (candidate > after)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>ローカル日時を DateTimeOffset に変換する（夏時間の不正時刻は +1 時間）。</summary>
    public static DateTimeOffset AtLocal(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        var zone = TimeZoneInfo.Local;
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
