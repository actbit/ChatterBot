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
    IReadOnlyList<ulong> MemberIds = null!,
    IReadOnlyList<string> ImageUrls = null!
)
{
    /// <summary>
    /// デフォルトの空メンバーリスト
    /// </summary>
    public static readonly IReadOnlyList<ulong> EmptyMemberIds = Array.Empty<ulong>();

    /// <summary>
    /// デフォルトの空画像URLリスト
    /// </summary>
    public static readonly IReadOnlyList<string> EmptyImageUrls = Array.Empty<string>();
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
