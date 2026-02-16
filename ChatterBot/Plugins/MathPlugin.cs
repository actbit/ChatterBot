using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// 数学計算プラグイン
/// </summary>
public class MathPlugin
{
    [KernelFunction("add")]
    [Description("2つの数値を足し算します。")]
    public double Add(
        [Description("1つ目の数値")] double a,
        [Description("2つ目の数値")] double b)
    {
        return a + b;
    }

    [KernelFunction("subtract")]
    [Description("2つの数値を引き算します。")]
    public double Subtract(
        [Description("引かれる数値")] double a,
        [Description("引く数値")] double b)
    {
        return a - b;
    }

    [KernelFunction("multiply")]
    [Description("2つの数値を掛け算します。")]
    public double Multiply(
        [Description("1つ目の数値")] double a,
        [Description("2つ目の数値")] double b)
    {
        return a * b;
    }

    [KernelFunction("divide")]
    [Description("2つの数値を割り算します。")]
    public string Divide(
        [Description("割られる数値")] double a,
        [Description("割る数値")] double b)
    {
        if (b == 0)
            return "error: division by zero";
        return (a / b).ToString();
    }

    [KernelFunction("compare")]
    [Description("2つの数値を比較します。")]
    public string Compare(
        [Description("1つ目の数値")] double a,
        [Description("2つ目の数値")] double b)
    {
        if (a > b) return $"{a} > {b}";
        if (a < b) return $"{a} < {b}";
        return $"{a} = {b}";
    }

    [KernelFunction("sin")]
    [Description("サイン（正弦）を計算します。引数はラジアンです。")]
    public double Sin(
        [Description("ラジアン単位の角度")] double radians)
    {
        return Math.Sin(radians);
    }

    [KernelFunction("cos")]
    [Description("コサイン（余弦）を計算します。引数はラジアンです。")]
    public double Cos(
        [Description("ラジアン単位の角度")] double radians)
    {
        return Math.Cos(radians);
    }

    [KernelFunction("tan")]
    [Description("タンジェント（正接）を計算します。引数はラジアンです。")]
    public string Tan(
        [Description("ラジアン単位の角度")] double radians)
    {
        var result = Math.Tan(radians);
        if (double.IsInfinity(result) || double.IsNaN(result))
            return "undefined";
        return result.ToString();
    }

    [KernelFunction("asin")]
    [Description("アークサイン（逆正弦）を計算します。結果はラジアンです。")]
    public string Asin(
        [Description("-1から1の範囲の値")] double value)
    {
        if (value < -1 || value > 1)
            return "error: value must be between -1 and 1";
        return Math.Asin(value).ToString();
    }

    [KernelFunction("acos")]
    [Description("アークコサイン（逆余弦）を計算します。結果はラジアンです。")]
    public string Acos(
        [Description("-1から1の範囲の値")] double value)
    {
        if (value < -1 || value > 1)
            return "error: value must be between -1 and 1";
        return Math.Acos(value).ToString();
    }

    [KernelFunction("atan")]
    [Description("アークタンジェント（逆正接）を計算します。結果はラジアンです。")]
    public double Atan(
        [Description("任意の実数値")] double value)
    {
        return Math.Atan(value);
    }

    [KernelFunction("atan2")]
    [Description("y/xのアークタンジェントを計算します。象限も考慮されます。結果はラジアンです。")]
    public double Atan2(
        [Description("y座標")] double y,
        [Description("x座標")] double x)
    {
        return Math.Atan2(y, x);
    }

    [KernelFunction("sqrt")]
    [Description("平方根を計算します。")]
    public string Sqrt(
        [Description("非負の数値")] double value)
    {
        if (value < 0)
            return "error: cannot calculate square root of negative number";
        return Math.Sqrt(value).ToString();
    }

    [KernelFunction("pow")]
    [Description("べき乗を計算します。aのb乗を返します。")]
    public double Pow(
        [Description("底")] double a,
        [Description("指数")] double b)
    {
        return Math.Pow(a, b);
    }

    [KernelFunction("log")]
    [Description("対数を計算します。デフォルトは自然対数です。")]
    public string Log(
        [Description("真数（正の値）")] double value,
        [Description("底（省略時は自然対数）")] double? baseValue = null)
    {
        if (value <= 0)
            return "error: value must be positive";

        if (baseValue == null)
            return Math.Log(value).ToString();

        if (baseValue <= 0 || baseValue == 1)
            return "error: base must be positive and not equal to 1";

        return Math.Log(value, baseValue.Value).ToString();
    }

    [KernelFunction("abs")]
    [Description("絶対値を計算します。")]
    public double Abs(
        [Description("数値")] double value)
    {
        return Math.Abs(value);
    }

    [KernelFunction("round")]
    [Description("四捨五入します。")]
    public double Round(
        [Description("数値")] double value,
        [Description("小数点以下の桁数（デフォルト0）")] int decimals = 0)
    {
        return Math.Round(value, decimals);
    }

    [KernelFunction("floor")]
    [Description("切り捨てます。")]
    public double Floor(
        [Description("数値")] double value)
    {
        return Math.Floor(value);
    }

    [KernelFunction("ceil")]
    [Description("切り上げます。")]
    public double Ceil(
        [Description("数値")] double value)
    {
        return Math.Ceiling(value);
    }

    [KernelFunction("pi")]
    [Description("円周率を返します。")]
    public double Pi()
    {
        return Math.PI;
    }

    [KernelFunction("e")]
    [Description("ネイピア数（自然対数の底）を返します。")]
    public double E()
    {
        return Math.E;
    }
}
