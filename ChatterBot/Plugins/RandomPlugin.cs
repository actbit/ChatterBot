using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// ランダム・ゲーム用プラグイン
/// </summary>
public class RandomPlugin
{
    private readonly Random _random = new();

    [KernelFunction("random")]
    [Description("指定範囲のランダムな整数を生成します。")]
    public int Random(
        [Description("最小値（含む）")] int min,
        [Description("最大値（含む）")] int max)
    {
        return _random.Next(min, max + 1);
    }

    [KernelFunction("random_float")]
    [Description("0.0以上1.0未満のランダムな浮動小数点数を生成します。")]
    public double RandomFloat()
    {
        return _random.NextDouble();
    }

    [KernelFunction("roll_dice")]
    [Description("サイコロを振ります。TRPG形式（例: 2d6, 1d100）または単純な形式で指定できます。")]
    public string RollDice(
        [Description("サイコロの数")] int count = 1,
        [Description("サイコロの面数")] int sides = 6)
    {
        if (count < 1 || count > 100)
            return "error: dice count must be between 1 and 100";

        if (sides < 2 || sides > 1000)
            return "error: sides must be between 2 and 1000";

        var rolls = new int[count];
        var total = 0;

        for (int i = 0; i < count; i++)
        {
            rolls[i] = _random.Next(1, sides + 1);
            total += rolls[i];
        }

        if (count == 1)
        {
            return $"rolled d{sides}: {rolls[0]}";
        }

        var rollsStr = string.Join(", ", rolls);
        return $"rolled {count}d{sides}: [{rollsStr}] = {total}";
    }

    [KernelFunction("coin_flip")]
    [Description("コインを投げます。")]
    public string CoinFlip()
    {
        return _random.Next(2) == 0 ? "heads" : "tails";
    }

    [KernelFunction("pick_one")]
    [Description("リストから1つをランダムに選びます。")]
    public string PickOne(
        [Description("選択肢（カンマ区切り）")] string options)
    {
        var items = options.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();

        if (items.Length == 0)
            return "error: no options provided";

        return items[_random.Next(items.Length)];
    }

    [KernelFunction("shuffle")]
    [Description("リストをシャッフルします。")]
    public string Shuffle(
        [Description("アイテム（カンマ区切り）")] string items)
    {
        var list = items.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        if (list.Count == 0)
            return "error: no items provided";

        // Fisher-Yates shuffle
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return string.Join(", ", list);
    }
}
