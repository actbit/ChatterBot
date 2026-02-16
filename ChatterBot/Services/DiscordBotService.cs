using ChatterBot.Abstractions;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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
                await message.Channel.SendMessageAsync(result.ReplyContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Username}", message.Author.Username);
        }
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
