using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// 時間関連の機能を提供するプラグイン
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
}
