using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>スケジュール計算（次回発火・営業日スキップ・日本の祝日）と通知位置のテスト。</summary>
[Collection("Localization")]
public class SchedulerScheduleTests
{
    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0) =>
        JobScheduleCalculator.AtLocal(new DateOnly(year, month, day), new TimeOnly(hour, minute));

    [Fact]
    public void Once_ReturnsScheduledTime_WhenInFuture()
    {
        var now = Local(2026, 10, 6, 9);
        var schedule = new JobSchedule { Kind = ScheduleKinds.Once, OnceAt = Local(2026, 10, 6, 10) };

        var next = JobScheduleCalculator.NextOccurrence(schedule, now, now);

        Assert.Equal(schedule.OnceAt, next);
    }

    [Fact]
    public void Once_ReturnsNull_WhenAlreadyPassed()
    {
        var now = Local(2026, 10, 6, 9);
        var schedule = new JobSchedule { Kind = ScheduleKinds.Once, OnceAt = Local(2026, 10, 6, 8) };

        Assert.Null(JobScheduleCalculator.NextOccurrence(schedule, now, now));
    }

    [Fact]
    public void Daily_UsesTimeOfDay_AndSkipsWhenPassed()
    {
        var now = Local(2026, 10, 6, 10);
        var schedule = new JobSchedule { Kind = ScheduleKinds.Daily, TimeOfDay = "07:00" };

        Assert.Equal(Local(2026, 10, 7, 7), JobScheduleCalculator.NextOccurrence(schedule, now, now));
        Assert.Equal(Local(2026, 10, 6, 7), JobScheduleCalculator.NextOccurrence(schedule, Local(2026, 10, 6, 6), now));
    }

    [Fact]
    public void Weekly_OnlySelectedWeekdays()
    {
        var now = Local(2026, 10, 6, 10); // 火曜
        var schedule = new JobSchedule
        {
            Kind = ScheduleKinds.Weekly,
            TimeOfDay = "09:00",
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday },
        };

        Assert.Equal(Local(2026, 10, 7, 9), JobScheduleCalculator.NextOccurrence(schedule, now, now));
    }

    [Fact]
    public void Monthly_Day31_FallsBackToLastDayOfMonth()
    {
        var now = Local(2026, 2, 1, 0);
        var schedule = new JobSchedule
        {
            Kind = ScheduleKinds.Monthly,
            TimeOfDay = "09:00",
            DaysOfMonth = new List<int> { 31 },
        };

        Assert.Equal(Local(2026, 2, 28, 9), JobScheduleCalculator.NextOccurrence(schedule, now, now));
    }

    [Fact]
    public void Interval_KeepsPhaseAcrossCalls()
    {
        var start = Local(2026, 10, 6, 9);
        var schedule = new JobSchedule
        {
            Kind = ScheduleKinds.Interval,
            IntervalMinutes = 30,
            StartAt = start,
        };

        Assert.Equal(Local(2026, 10, 6, 9, 30), JobScheduleCalculator.NextOccurrence(schedule, start, start));
        Assert.Equal(Local(2026, 10, 6, 10), JobScheduleCalculator.NextOccurrence(schedule, Local(2026, 10, 6, 9, 45), start));
    }

    [Fact]
    public void EndAt_StopsRecurrence()
    {
        var now = Local(2026, 10, 6, 10);
        var schedule = new JobSchedule
        {
            Kind = ScheduleKinds.Daily,
            TimeOfDay = "07:00",
            EndAt = Local(2026, 10, 6, 23),
        };

        Assert.Null(JobScheduleCalculator.NextOccurrence(schedule, now, now));
    }

    [Fact]
    public void SkipWeekends_SkipsSaturdayAndSunday()
    {
        var now = Local(2026, 10, 9, 10); // 金曜 10:00
        var schedule = new JobSchedule
        {
            Kind = ScheduleKinds.Daily,
            TimeOfDay = "09:00",
            SkipWeekends = true,
        };

        Assert.Equal(Local(2026, 10, 12, 9), JobScheduleCalculator.NextOccurrence(schedule, now, now));
    }

    [Fact]
    public void SkipHolidays_SkipsJapaneseGoldenWeek()
    {
        var now = Local(2026, 5, 2, 10);
        var schedule = new JobSchedule
        {
            Kind = ScheduleKinds.Daily,
            TimeOfDay = "09:00",
            SkipHolidays = true,
        };

        // 5/3(日) 憲法記念日 / 5/4 みどりの日 / 5/5 こどもの日 / 5/6 振替休日
        Assert.Equal(Local(2026, 5, 7, 9), JobScheduleCalculator.NextOccurrence(schedule, now, now));
    }

    [Fact]
    public void OccurrencesBetween_ReturnsEveryDailyOccurrence()
    {
        var schedule = new JobSchedule { Kind = ScheduleKinds.Daily, TimeOfDay = "07:00" };
        var now = Local(2026, 10, 1, 0);

        var occurrences = JobScheduleCalculator.OccurrencesBetween(
            schedule,
            Local(2026, 10, 1, 0),
            Local(2026, 10, 3, 23, 59),
            now);

        Assert.Equal(
            new[] { Local(2026, 10, 1, 7), Local(2026, 10, 2, 7), Local(2026, 10, 3, 7) },
            occurrences);
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 1, 12)]
    [InlineData(2026, 2, 23)]
    [InlineData(2026, 3, 20)]
    [InlineData(2026, 5, 6)]
    [InlineData(2026, 9, 21)]
    [InlineData(2026, 9, 22)]
    [InlineData(2026, 9, 23)]
    [InlineData(2026, 10, 12)]
    [InlineData(2025, 2, 24)]
    public void JapanHolidayCalendar_RecognizesHolidays(int year, int month, int day)
    {
        Assert.True(JapanHolidayCalendar.Instance.IsHoliday(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(2026, 10, 6)]
    [InlineData(2026, 10, 7)]
    [InlineData(2026, 4, 30)]
    public void JapanHolidayCalendar_RegularDaysAreNotHolidays(int year, int month, int day)
    {
        Assert.False(JapanHolidayCalendar.Instance.IsHoliday(new DateOnly(year, month, day)));
    }

    [Fact]
    public void NotificationPlacement_UsesBottomRightOfWorkArea()
    {
        var (left, top) = NotificationPlacement.BottomRight(0, 0, 1920, 1040, 360, 300);

        Assert.Equal(1548, left);
        Assert.Equal(728, top);
    }

    [Fact]
    public void NotificationPlacement_ClampsToWorkArea()
    {
        var (left, top) = NotificationPlacement.BottomRight(0, 0, 300, 200, 360, 300);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }
}
