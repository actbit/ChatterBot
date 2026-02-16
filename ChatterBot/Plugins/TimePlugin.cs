using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// 時間・日付関連の機能を提供するプラグイン
/// </summary>
public class TimePlugin
{
    [KernelFunction("get_current_time")]
    [Description("現在の日時を取得します。")]
    public string GetCurrentTime()
    {
        var now = DateTime.Now;
        return $"date: {now:yyyy-MM-dd}, day: {now.DayOfWeek}, time: {now:HH:mm}";
    }

    [KernelFunction("get_date")]
    [Description("今日の日付を取得します。")]
    public string GetDate()
    {
        var now = DateTime.Now;
        return $"date: {now:yyyy-MM-dd}, day: {now.DayOfWeek}";
    }

    [KernelFunction("get_time")]
    [Description("現在の時刻を取得します。")]
    public string GetTime()
    {
        var now = DateTime.Now;
        return $"time: {now:HH:mm}";
    }

    [KernelFunction("days_until")]
    [Description("指定した日付までの日数を計算します。")]
    public string DaysUntil(
        [Description("目標日付（yyyy-MM-dd形式、または 'tomorrow', 'next week', 'christmas', 'newyear' など）")] string target)
    {
        var today = DateTime.Today;
        DateTime targetDate;

        switch (target.ToLowerInvariant())
        {
            case "tomorrow":
                targetDate = today.AddDays(1);
                break;
            case "next week":
                targetDate = today.AddDays(7);
                break;
            case "christmas":
                targetDate = new DateTime(today.Year, 12, 25);
                if (targetDate < today)
                    targetDate = targetDate.AddYears(1);
                break;
            case "newyear":
            case "new year":
                targetDate = new DateTime(today.Year + 1, 1, 1);
                break;
            case "valentine":
                targetDate = new DateTime(today.Year, 2, 14);
                if (targetDate < today)
                    targetDate = targetDate.AddYears(1);
                break;
            case "halloween":
                targetDate = new DateTime(today.Year, 10, 31);
                if (targetDate < today)
                    targetDate = targetDate.AddYears(1);
                break;
            default:
                if (!DateTime.TryParse(target, out targetDate))
                    return "error: invalid date format. Use yyyy-MM-dd or keywords like 'tomorrow', 'christmas'";
                break;
        }

        var days = (targetDate - today).Days;
        return $"date: {targetDate:yyyy-MM-dd}, days_until: {days}";
    }

    [KernelFunction("add_days")]
    [Description("今日から指定日数後の日付を計算します。")]
    public string AddDays(
        [Description("加算する日数（負の値で過去）")] int days)
    {
        var result = DateTime.Today.AddDays(days);
        return $"date: {result:yyyy-MM-dd}, day: {result.DayOfWeek}";
    }

    [KernelFunction("days_between")]
    [Description("2つの日付間の日数を計算します。")]
    public string DaysBetween(
        [Description("開始日（yyyy-MM-dd形式）")] string startDate,
        [Description("終了日（yyyy-MM-dd形式）")] string endDate)
    {
        if (!DateTime.TryParse(startDate, out var start))
            return "error: invalid start date format";

        if (!DateTime.TryParse(endDate, out var end))
            return "error: invalid end date format";

        var days = (end - start).Days;
        return $"days: {days}";
    }

    [KernelFunction("day_of_week")]
    [Description("指定した日付の曜日を取得します。")]
    public string DayOfWeek(
        [Description("日付（yyyy-MM-dd形式、または 'today', 'tomorrow'）")] string date)
    {
        DateTime targetDate;

        switch (date.ToLowerInvariant())
        {
            case "today":
                targetDate = DateTime.Today;
                break;
            case "tomorrow":
                targetDate = DateTime.Today.AddDays(1);
                break;
            case "yesterday":
                targetDate = DateTime.Today.AddDays(-1);
                break;
            default:
                if (!DateTime.TryParse(date, out targetDate))
                    return "error: invalid date format";
                break;
        }

        return $"date: {targetDate:yyyy-MM-dd}, day: {targetDate.DayOfWeek}";
    }
}
