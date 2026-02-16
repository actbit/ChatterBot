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
    Task AddUserMessageAsync(ulong? guildId, ulong channelId, ulong messageId, ulong userId, string userName, string content);

    /// <summary>
    /// アシスタントメッセージを追加する
    /// </summary>
    Task AddAssistantMessageAsync(ulong? guildId, ulong channelId, ulong messageId, string content);

    /// <summary>
    /// ユーザーメッセージを更新する（編集時）
    /// </summary>
    Task UpdateUserMessageAsync(ulong messageId, string userName, string newContent);

    /// <summary>
    /// ユーザーメッセージを削除する
    /// </summary>
    Task DeleteUserMessageAsync(ulong messageId);

    /// <summary>
    /// チャンネルの全履歴を削除する
    /// </summary>
    Task DeleteChannelAsync(ulong? guildId, ulong channelId);

    /// <summary>
    /// 直近N日分の履歴を読み込む
    /// </summary>
    Task LoadRecentHistoryAsync(ulong? guildId, ulong channelId, int days);
}
