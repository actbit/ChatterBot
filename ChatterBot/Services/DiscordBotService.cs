using ChatterBot.Abstractions;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ChatterBot.Services;

/// <summary>
/// Discord Botサービス
/// </summary>
public class DiscordBotService : IDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly IMessageProcessor _messageProcessor;
    private readonly IChatHistoryManager _historyManager;
    private readonly IRagHistoryStore _ragStore;
    private readonly string _discordToken;
    private readonly ILogger<DiscordBotService> _logger;

    // チャンネルごとの直近のユーザー名→IDマッピング（メンション変換用）
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, ulong>> _channelUserCache = new();

    public DiscordBotService(
        IMessageProcessor messageProcessor,
        IChatHistoryManager historyManager,
        IRagHistoryStore ragStore,
        string discordToken,
        ILogger<DiscordBotService> logger)
    {
        _messageProcessor = messageProcessor;
        _historyManager = historyManager;
        _ragStore = ragStore;
        _discordToken = discordToken;
        _logger = logger;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.MessageContent |
                             GatewayIntents.GuildMessages |
                             GatewayIntents.Guilds |
                             GatewayIntents.DirectMessages |
                             GatewayIntents.DirectMessageReactions
        };

        _client = new DiscordSocketClient(config);
        _client.Log += LogAsync;
        _client.MessageReceived += MessageReceivedAsync;
        _client.MessageUpdated += MessageUpdatedAsync;
        _client.MessageDeleted += MessageDeletedAsync;
        _client.ChannelDestroyed += ChannelDestroyedAsync;
        _client.LeftGuild += LeftGuildAsync;
        _client.Ready += ReadyAsync;
    }

    public async Task StartAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();
    }

    public async Task StopAsync()
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task ReadyAsync()
    {
        _logger.LogInformation("Bot is connected and ready!");
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        // Bot自身のメッセージは無視
        if (message.Author.Id == _client.CurrentUser.Id)
            return;

        // システムメッセージは無視
        if (message is not SocketUserMessage userMessage)
            return;

        // チャンネルタイプを取得
        var channel = message.Channel;

        // Guild/DMの情報を取得
        ulong? guildId = null;
        bool isChannelPublic = true;
        IReadOnlyList<ulong> memberIds = MessageContext.EmptyMemberIds;

        if (channel is SocketGuildChannel guildChannel)
        {
            guildId = guildChannel.Guild.Id;

            // チャンネルの公開/非公開を判定
            if (channel is SocketTextChannel textChannel)
            {
                // @everyone ロールがReadMessages権限を持っていればPublic
                var everyonePermissions = textChannel.GetPermissionOverwrite(guildChannel.Guild.EveryoneRole);
                isChannelPublic = everyonePermissions == null ||
                                   everyonePermissions.Value.ViewChannel != PermValue.Deny;

                // メンバーIDのリストを取得
                memberIds = textChannel.Users.Select(u => u.Id).ToList();
            }

            // チャンネル情報をRAGストアに保存
            await _ragStore.UpdateChannelInfoAsync(guildId, channel.Id, isChannelPublic, memberIds);
        }
        else if (channel is SocketDMChannel dmChannel)
        {
            // DMの場合は自分と相手
            memberIds = new[] { _client.CurrentUser.Id, dmChannel.Recipient.Id };
        }

        // ユーザー名をキャッシュに追加（メンション変換用）
        var userCache = _channelUserCache.GetOrAdd(channel.Id, _ => new ConcurrentDictionary<string, ulong>(StringComparer.OrdinalIgnoreCase));

        // 各種名前をキャッシュ
        CacheUserName(userCache, message.Author);

        // 添付画像のURLを取得（画像のみ）
        var imageUrls = message.Attachments
            .Where(a => a.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            .Select(a => a.Url)
            .ToList();

        var context = new MessageContext(
            message.Id,
            message.Author.Id,
            message.Author.Username,
            channel.Id,
            guildId,
            isChannelPublic,
            memberIds,
            imageUrls.Count > 0 ? imageUrls : MessageContext.EmptyImageUrls
        );

        try
        {
            var result = await _messageProcessor.ProcessAsync(message.Content, context);

            if (result.ShouldReply && result.ReplyContent != null)
            {
                // @username や xxxさん をDiscordメンション形式に変換
                var replyContent = ConvertMentions(result.ReplyContent, userCache);
                var replyMessage = await message.Channel.SendMessageAsync(replyContent);

                // アシスタントメッセージを履歴に追加
                await _historyManager.AddAssistantMessageAsync(guildId, channel.Id, replyMessage.Id, result.ReplyContent);

                // RAGストアにも保存
                await _ragStore.StoreAsync(guildId, channel.Id, replyMessage.Id, _client.CurrentUser.Id, "ChatterBot", "assistant", result.ReplyContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Username}", message.Author.Username);
        }
    }

    private async Task MessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        // Bot自身のメッセージは無視
        if (after.Author.Id == _client.CurrentUser.Id)
            return;

        // システムメッセージは無視
        if (after is not SocketUserMessage)
            return;

        var beforeMessage = await before.GetOrDownloadAsync();
        if (beforeMessage == null)
            return;

        // 内容が変わっていない場合は無視
        if (beforeMessage.Content == after.Content)
            return;

        // Guild/DMの情報を取得
        ulong? guildId = null;
        if (channel is SocketGuildChannel guildChannel)
        {
            guildId = guildChannel.Guild.Id;
        }

        var userName = after.Author.GlobalName ?? after.Author.Username;

        try
        {
            // 履歴を更新（message_idで特定）
            await _historyManager.UpdateUserMessageAsync(
                after.Id,
                userName,
                after.Content);

            // RAGストアも更新
            await _ragStore.UpdateAsync(after.Id, after.Content);

            _logger.LogInformation("Message updated by {Username}: \"{Old}\" -> \"{New}\"",
                userName, beforeMessage.Content, after.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating message history for {Username}", userName);
        }
    }

    private async Task MessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        try
        {
            // 履歴から削除（message_idで特定）
            await _historyManager.DeleteUserMessageAsync(message.Id);
            await _ragStore.DeleteMessageAsync(message.Id);

            _logger.LogInformation("Message deleted: {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message history for {MessageId}", message.Id);
        }
    }

    private async Task ChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is not SocketTextChannel textChannel)
            return;

        try
        {
            ulong? guildId = textChannel.Guild?.Id;

            // 履歴からチャンネルの全データを削除
            await _historyManager.DeleteChannelAsync(guildId, channel.Id);
            await _ragStore.DeleteChannelAsync(channel.Id);

            // ユーザーキャッシュもクリア
            _channelUserCache.TryRemove(channel.Id, out _);

            _logger.LogInformation("Channel deleted: {ChannelName} ({ChannelId})", textChannel.Name, channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting channel history for {ChannelId}", channel.Id);
        }
    }

    private async Task LeftGuildAsync(SocketGuild guild)
    {
        try
        {
            // ギルドの全履歴を削除
            await _ragStore.DeleteGuildAsync(guild.Id);

            // このギルドのチャンネルのユーザーキャッシュもクリア
            foreach (var channel in guild.Channels)
            {
                _channelUserCache.TryRemove(channel.Id, out _);
            }

            _logger.LogInformation("Left guild: {GuildName} ({GuildId})", guild.Name, guild.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting guild history for {GuildId}", guild.Id);
        }
    }

    /// <summary>
    /// ユーザーの各種名前をキャッシュに登録
    /// </summary>
    private void CacheUserName(ConcurrentDictionary<string, ulong> cache, IUser user)
    {
        // Username
        if (!string.IsNullOrEmpty(user.Username))
        {
            cache[user.Username] = user.Id;
            cache[$"@{user.Username}"] = user.Id;
        }

        // GlobalName (表示名)
        if (!string.IsNullOrEmpty(user.GlobalName))
        {
            cache[user.GlobalName] = user.Id;
            cache[$"@{user.GlobalName}"] = user.Id;

            // 日本語・英語のみ抽出 + さん付け
            var cleanName = ExtractName(user.GlobalName);
            if (!string.IsNullOrEmpty(cleanName))
            {
                cache[$"{cleanName}さん"] = user.Id;
                cache[$"{cleanName}"] = user.Id;
            }
        }

        // Server Nickname (ギルド内の表示名)
        if (user is SocketGuildUser guildUser && !string.IsNullOrEmpty(guildUser.Nickname))
        {
            if (guildUser.Nickname != user.GlobalName)
            {
                cache[guildUser.Nickname] = user.Id;
                cache[$"@{guildUser.Nickname}"] = user.Id;

                // 日本語・英語のみ抽出 + さん付け
                var cleanName = ExtractName(guildUser.Nickname);
                if (!string.IsNullOrEmpty(cleanName))
                {
                    cache[$"{cleanName}さん"] = user.Id;
                    cache[$"{cleanName}"] = user.Id;
                }
            }
        }
    }

    /// <summary>
    /// 名前から日本語と英語のみを抽出
    /// </summary>
    private static string ExtractName(string name)
    {
        // 日本語（ひらがな、カタカナ、漢字）と英数字のみを抽出
        var result = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (IsJapanese(c) || IsEnglish(c))
            {
                result.Append(c);
            }
        }
        return result.ToString().Trim();
    }

    private static bool IsJapanese(char c)
    {
        // ひらがな、カタカナ、漢字
        return (c >= '\u3040' && c <= '\u309F') ||  // ひらがな
               (c >= '\u30A0' && c <= '\u30FF') ||  // カタカナ
               (c >= '\u4E00' && c <= '\u9FFF') ||  // CJK統合漢字
               (c >= '\u3400' && c <= '\u4DBF');    // CJK拡張A
    }

    private static bool IsEnglish(char c)
    {
        // 英数字とスペース
        return (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               (c >= '0' && c <= '9') ||
               c == ' ';
    }

    /// <summary>
    /// テキスト内の @username や xxxさん をDiscordのメンション形式に変換
    /// </summary>
    private string ConvertMentions(string content, ConcurrentDictionary<string, ulong> userCache)
    {
        // xxxさん パターンを先に処理
        var sanPattern = @"([一-龯ぁ-んァ-ンa-zA-Z0-9]+)さん";
        content = Regex.Replace(content, sanPattern, match =>
        {
            var key = match.Value;
            if (userCache.TryGetValue(key, out var userId))
            {
                return $"<@{userId}>";
            }
            return match.Value;
        });

        // @username パターンを処理
        var mentionPattern = @"@(\S+)";
        content = Regex.Replace(content, mentionPattern, match =>
        {
            var name = match.Groups[1].Value;
            if (userCache.TryGetValue(name, out var userId))
            {
                return $"<@{userId}>";
            }
            // 見つからない場合はそのまま
            return match.Value;
        });

        return content;
    }

    private Task LogAsync(LogMessage logMessage)
    {
        var severity = logMessage.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Debug => LogLevel.Debug,
            LogSeverity.Verbose => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(severity, logMessage.Exception, "[Discord] {Message}", logMessage.Message);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
