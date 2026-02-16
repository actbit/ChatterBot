using ChatterBot.Abstractions;

namespace ChatterBot.Abstractions;

/// <summary>
/// RAG履歴ストアインターフェース（永続化・履歴は削除しない）
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
    Task StoreAsync(ulong? guildId, ulong channelId, ulong userId, string userName, string role, string content);

    /// <summary>
    /// 過去の会話を検索する
    /// </summary>
    Task<IReadOnlyList<HistoryRecord>> SearchAsync(
        string query,
        ulong? guildId,
        ulong? channelId,
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
