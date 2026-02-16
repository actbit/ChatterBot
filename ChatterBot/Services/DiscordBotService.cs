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
    private readonly string _discordToken;
    private readonly ILogger<DiscordBotService> _logger;

    // チャンネルごとの直近のユーザー名→IDマッピング（メンション変換用）
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, ulong>> _channelUserCache = new();

    public DiscordBotService(
        IMessageProcessor messageProcessor,
        string discordToken,
        ILogger<DiscordBotService> logger)
    {
        _messageProcessor = messageProcessor;
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
        if (channel is SocketGuildChannel guildChannel)
        {
            guildId = guildChannel.Guild.Id;
        }

        // ユーザー名をキャッシュに追加（メンション変換用）
        var userCache = _channelUserCache.GetOrAdd(channel.Id, _ => new ConcurrentDictionary<string, ulong>(StringComparer.OrdinalIgnoreCase));

        // 各種名前をキャッシュ
        CacheUserName(userCache, message.Author);

        var context = new MessageContext(
            message.Author.Id,
            message.Author.Username,
            channel.Id,
            guildId
        );

        try
        {
            var result = await _messageProcessor.ProcessAsync(message.Content, context);

            if (result.ShouldReply && result.ReplyContent != null)
            {
                // @username や xxxさん をDiscordメンション形式に変換
                var replyContent = ConvertMentions(result.ReplyContent, userCache);
                await message.Channel.SendMessageAsync(replyContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Username}", message.Author.Username);
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
