using Microsoft.SemanticKernel.ChatCompletion;

namespace ChatterBot.Abstractions;

/// <summary>
/// ChatHistory管理インターフェース（直近N日分のみ読み込み、SQLiteに永続化）
/// </summary>
public interface IChatHistoryManager
{
    /// <summary>
    /// 指定されたチャンネルのChatHistoryを取得または作成する
    /// </summary>
    ChatHistory GetOrCreateHistory(ulong? guildId, ulong channelId);

    /// <summary>
    /// ユーザーメッセージを追加する
    /// </summary>
    Task AddUserMessageAsync(ulong? guildId, ulong channelId, ulong userId, string userName, string content);

    /// <summary>
    /// アシスタントメッセージを追加する
    /// </summary>
    Task AddAssistantMessageAsync(ulong? guildId, ulong channelId, string content);

    /// <summary>
    /// 直近N日分の履歴を読み込む
    /// </summary>
    Task LoadRecentHistoryAsync(ulong? guildId, ulong channelId, int days);
}
