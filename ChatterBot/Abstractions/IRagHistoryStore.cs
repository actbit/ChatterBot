using ChatterBot.Abstractions;

namespace ChatterBot.Abstractions;

/// <summary>
/// RAG履歴ストアインターフェース
/// </summary>
public interface IRagHistoryStore
{
    /// <summary>
    /// 初期化処理
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// メッセージを保存する
    /// </summary>
    Task StoreAsync(ulong? guildId, ulong channelId, ulong messageId, ulong userId, string userName, string role, string content);

    /// <summary>
    /// メッセージを更新する（編集時）
    /// </summary>
    Task UpdateAsync(ulong messageId, string newContent);

    /// <summary>
    /// メッセージを削除する
    /// </summary>
    Task DeleteMessageAsync(ulong messageId);

    /// <summary>
    /// チャンネルの全履歴を削除する
    /// </summary>
    Task DeleteChannelAsync(ulong channelId);

    /// <summary>
    /// ギルドの全履歴を削除する
    /// </summary>
    Task DeleteGuildAsync(ulong guildId);

    /// <summary>
    /// チャンネル情報とメンバーを更新する
    /// </summary>
    Task UpdateChannelInfoAsync(ulong? guildId, ulong channelId, bool isPublic, IReadOnlyList<ulong> memberIds);

    /// <summary>
    /// 過去の会話を検索する
    /// </summary>
    Task<IReadOnlyList<HistoryRecord>> SearchAsync(
        string query,
        ulong? currentGuildId,
        ulong? currentChannelId,
        bool isCurrentChannelPublic,
        IReadOnlyList<ulong> currentMemberIds,
        int? days,
        int limit);

    /// <summary>
    /// 直近の履歴を取得する
    /// </summary>
    Task<IReadOnlyList<HistoryRecord>> GetRecentAsync(ulong? guildId, ulong channelId, int days);

    /// <summary>
    /// アクティブなチャンネル一覧を取得する
    /// </summary>
    Task<IReadOnlyList<ulong>> GetActiveChannelsAsync(ulong? guildId, int inactiveDaysThreshold);
}
