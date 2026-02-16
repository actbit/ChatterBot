using System.ComponentModel;
using Microsoft.SemanticKernel;
using ChatterBot.Abstractions;

namespace ChatterBot.Plugins;

/// <summary>
/// 履歴検索プラグイン
/// </summary>
public class HistorySearchPlugin
{
    private readonly IRagHistoryStore _ragStore;
    private readonly ulong? _guildId;
    private readonly ulong _channelId;
    private readonly bool _isChannelPublic;
    private readonly IReadOnlyList<ulong> _memberIds;

    public HistorySearchPlugin(IRagHistoryStore ragStore, ulong? guildId, ulong channelId, bool isChannelPublic, IReadOnlyList<ulong> memberIds)
    {
        _ragStore = ragStore;
        _guildId = guildId;
        _channelId = channelId;
        _isChannelPublic = isChannelPublic;
        _memberIds = memberIds ?? MessageContext.EmptyMemberIds;
    }

    [KernelFunction("search_history")]
    [Description("過去の会話履歴から関連するメッセージを検索します。以前の話題や議論内容を思い出す必要がある場合に使用してください。")]
    public async Task<string> SearchHistoryAsync(
        [Description("検索クエリ。検索したい内容や話題を入力")] string query,
        [Description("検索期間（日数）。指定なしで全期間検索")] int? days = null,
        [Description("最大取得件数")] int limit = 5)
    {
        var results = await _ragStore.SearchAsync(query, _guildId, _channelId, _isChannelPublic, _memberIds, days, limit);

        if (results.Count == 0)
        {
            return "関連する履歴が見つかりませんでした。";
        }

        var formattedResults = results.Select((r, i) =>
            $"[{i + 1}] {r.CreatedAt:yyyy-MM-dd HH:mm} - {r.UserName}({r.Role}): {r.Content}" +
            (r.RelevanceScore.HasValue ? $" (関連度: {r.RelevanceScore.Value:F2})" : ""));

        return $"過去の会話から{results.Count}件の関連メッセージが見つかりました:\n\n" +
               string.Join("\n\n", formattedResults);
    }
}
