using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// 返信制御プラグイン
/// </summary>
public class ReplyPlugin
{
    private readonly TaskCompletionSource<ReplyDecision> _decisionSource;

    public ReplyPlugin(TaskCompletionSource<ReplyDecision> decisionSource)
    {
        _decisionSource = decisionSource;
    }

    [KernelFunction("reply")]
    [Description("ユーザーに返信します。返信内容が決まっている場合に使用してください。")]
    public async Task<string> ReplyAsync(
        [Description("返信内容")] string content)
    {
        _decisionSource.SetResult(new ReplyDecision(true, content));
        return $"返信を送信しました: {content}";
    }

    [KernelFunction("do_not_reply")]
    [Description("返信しないことを決定します。メッセージに対して返信が不要な場合に使用してください。")]
    public async Task<string> DoNotReplyAsync()
    {
        _decisionSource.SetResult(new ReplyDecision(false, null));
        return "返信しないことを決定しました。";
    }
}

/// <summary>
/// 返信判断結果
/// </summary>
public record ReplyDecision(bool ShouldReply, string? Content);
