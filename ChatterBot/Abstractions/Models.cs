namespace ChatterBot.Abstractions;

/// <summary>
/// メッセージ処理の結果
/// </summary>
public record ProcessResult(
    bool ShouldReply,
    string? ReplyContent
);

/// <summary>
/// メッセージのコンテキスト情報
/// </summary>
public record MessageContext(
    ulong MessageId,
    ulong UserId,
    string UserName,
    ulong ChannelId,
    ulong? GuildId,
    bool IsChannelPublic = true,
    IReadOnlyList<ulong> MemberIds = null!
)
{
    /// <summary>
    /// デフォルトの空メンバーリスト
    /// </summary>
    public static readonly IReadOnlyList<ulong> EmptyMemberIds = Array.Empty<ulong>();
}

/// <summary>
/// 履歴レコード
/// </summary>
public record HistoryRecord(
    ulong? GuildId,
    ulong ChannelId,
    ulong UserId,
    string UserName,
    string Role,
    string Content,
    DateTime CreatedAt,
    float? RelevanceScore
);
