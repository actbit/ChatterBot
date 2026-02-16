using ChatterBot.Abstractions;

namespace ChatterBot.Abstractions;

/// <summary>
/// メッセージ処理インターフェース
/// </summary>
public interface IMessageProcessor
{
    /// <summary>
    /// メッセージを処理し、返信するかどうかを判断する
    /// </summary>
    Task<ProcessResult> ProcessAsync(string content, MessageContext context);
}
