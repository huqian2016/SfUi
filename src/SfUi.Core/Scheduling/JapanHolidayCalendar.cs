namespace SfUi.Core;

/// <summary>祝日カレンダーの抽象化（営業日スキップ判定に使う）。</summary>
public interface IHolidayCalendar
{
    /// <summary>指定日が祝日か。</summary>
    bool IsHoliday(DateOnly date);

    /// <summary>祝日でない平日か（土日も除外する）。</summary>
    bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !IsHoliday(date);
}

/// <summary>
/// 日本の祝日カレンダー（2020 年以降）。固定日・ハッピーマンデー・春分/秋分・
/// 振替休日・国民の休日をルールから算出する（外部データファイル不要）。
/// 2020 / 2021 年の東京オリンピックに伴う移動も考慮する。
/// </summary>
public sealed class JapanHolidayCalendar : IHolidayCalendar
{
    private readonly Dictionary<int, HashSet<DateOnly>> _cache = new();
    private readonly object _gate = new();

    public static JapanHolidayCalendar Instance { get; } = new();

    public bool IsHoliday(DateOnly date) => GetHolidays(date.Year).Contains(date);

    /// <summary>指定年の祝日一覧（キャッシュ）。</summary>
    public IReadOnlySet<DateOnly> GetHolidays(int year)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(year, out var cached))
            {
                return cached;
            }

            var holidays = BuildHolidays(year);
            _cache[year] = holidays;
            return holidays;
        }
    }

    private static HashSet<DateOnly> BuildHolidays(int year)
    {
        var set = new HashSet<DateOnly>();
        if (year < 2020)
        {
            // 2020 年以前はルールが異なるため祝日判定しない（週末スキップのみ有効）。
            return set;
        }

        void Add(int month, int day) => set.Add(new DateOnly(year, month, day));

        Add(1, 1);                                  // 元日
        set.Add(NthMonday(year, 1, 2));             // 成人の日
        Add(2, 11);                                 // 建国記念の日
        Add(2, 23);                                 // 天皇誕生日
        Add(3, VernalEquinoxDay(year));              // 春分の日
        Add(4, 29);                                 // 昭和の日
        Add(5, 3);                                  // 憲法記念日
        Add(5, 4);                                  // みどりの日
        Add(5, 5);                                  // こどもの日

        if (year == 2020)
        {
            Add(7, 23);                             // 海の日（オリンピック特例）
            Add(7, 24);                             // スポーツの日（特例）
            Add(8, 10);                             // 山の日（特例）
        }
        else if (year == 2021)
        {
            Add(7, 22);                             // 海の日（特例）
            Add(7, 23);                             // スポーツの日（特例）
            Add(8, 8);                              // 山の日（特例）
        }
        else
        {
            set.Add(NthMonday(year, 7, 3));         // 海の日
            Add(8, 11);                             // 山の日
            set.Add(NthMonday(year, 10, 2));        // スポーツの日
        }

        set.Add(NthMonday(year, 9, 3));             // 敬老の日
        Add(9, AutumnalEquinoxDay(year));            // 秋分の日
        Add(11, 3);                                 // 文化の日
        Add(11, 23);                                // 勤労感謝の日

        // 国民の休日: 前日と翌日が祝日の平日（日曜を除く）
        foreach (var holiday in set.ToList())
        {
            var between = holiday.AddDays(1);
            if (between.DayOfWeek != DayOfWeek.Sunday
                && !set.Contains(between)
                && set.Contains(between.AddDays(1)))
            {
                set.Add(between);
            }
        }

        // 振替休日: 日曜の祝日の場合、翌日以降で最初の非祝日
        foreach (var holiday in set.OrderBy(d => d).ToList())
        {
            if (holiday.DayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }

            var substitute = holiday.AddDays(1);
            while (set.Contains(substitute))
            {
                substitute = substitute.AddDays(1);
            }

            set.Add(substitute);
        }

        return set;
    }

    /// <summary>その月の第 N 月曜日。</summary>
    private static DateOnly NthMonday(int year, int month, int nth)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (nth - 1) * 7);
    }

    /// <summary>春分の日（1980-2099 の近似式）。</summary>
    private static int VernalEquinoxDay(int year) =>
        (int)Math.Floor(20.8431 + 0.242194 * (year - 1980) - Math.Floor((year - 1980) / 4.0));

    /// <summary>秋分の日（1980-2099 の近似式）。</summary>
    private static int AutumnalEquinoxDay(int year) =>
        (int)Math.Floor(23.2488 + 0.242194 * (year - 1980) - Math.Floor((year - 1980) / 4.0));
}
