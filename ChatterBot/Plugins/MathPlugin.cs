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

    [KernelFunction("to_radians")]
    [Description("度をラジアンに変換します。")]
    public double ToRadians(
        [Description("度単位の角度")] double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    [KernelFunction("to_degrees")]
    [Description("ラジアンを度に変換します。")]
    public double ToDegrees(
        [Description("ラジアン単位の角度")] double radians)
    {
        return radians * 180.0 / Math.PI;
    }
}
