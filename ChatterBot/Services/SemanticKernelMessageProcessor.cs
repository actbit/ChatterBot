using ChatterBot.Abstractions;
using ChatterBot.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatterBot.Services;

/// <summary>
/// Semantic Kernelを使用したメッセージプロセッサ
/// </summary>
public class SemanticKernelMessageProcessor : IMessageProcessor
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletion;
    private readonly IChatHistoryManager _historyManager;
    private readonly IRagHistoryStore _ragStore;
    private readonly string _systemPrompt;
    private readonly int _defaultLoadDays;
    private readonly ILogger<SemanticKernelMessageProcessor> _logger;

    public SemanticKernelMessageProcessor(
        Kernel kernel,
        IChatHistoryManager historyManager,
        IRagHistoryStore ragStore,
        string systemPrompt,
        int defaultLoadDays,
        ILogger<SemanticKernelMessageProcessor> logger)
    {
        _kernel = kernel;
        _chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        _historyManager = historyManager;
        _ragStore = ragStore;
        _systemPrompt = systemPrompt;
        _defaultLoadDays = defaultLoadDays;
        _logger = logger;
    }

    public async Task<ProcessResult> ProcessAsync(string content, MessageContext context)
    {
        try
        {
            // 履歴を遅延読み込み（初回のみ）
            await _historyManager.LoadRecentHistoryAsync(context.GuildId, context.ChannelId, _defaultLoadDays);

            // ユーザーメッセージを履歴に追加
            await _historyManager.AddUserMessageAsync(
                context.GuildId, context.ChannelId, context.UserId, context.UserName, content);

            // ChatHistoryを取得
            var chatHistory = _historyManager.GetOrCreateHistory(context.GuildId, context.ChannelId);

            // 返信判断用のTaskCompletionSource
            var decisionSource = new TaskCompletionSource<ReplyDecision>();

            // プラグイン用のKernelを構築（元のKernelをベースに）
            var pluginKernel = _kernel;

            // プラグインをインスタンス化して登録
            var replyPlugin = new ReplyPlugin(decisionSource);
            var historySearchPlugin = new HistorySearchPlugin(_ragStore, context.GuildId, context.ChannelId);

            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(replyPlugin, "ReplyPlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(historySearchPlugin, "HistorySearchPlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(timePlugin, "TimePlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(urlReaderPlugin, "UrlReaderPlugin"));

            // ChatHistoryのコピーを作成してシステムプロンプトを追加
            var fullHistory = new ChatHistory();
            fullHistory.AddSystemMessage(_systemPrompt);

            foreach (var message in chatHistory)
            {
                fullHistory.Add(message);
            }

            // OpenAIPromptExecutionSettingsを設定（AutoでFunction Callingを有効化）
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            _logger.LogInformation("Processing message from {UserName}: {Content}", context.UserName, content);

            // AIに送信してFunction Callingを実行
            var result = await _chatCompletion.GetChatMessageContentAsync(
                fullHistory,
                executionSettings,
                pluginKernel);

            _logger.LogInformation("AI Response: {Result}", result.Content ?? "Function called");

            // 返信判断を待つ（タイムアウト付き）
            var completedTask = await Task.WhenAny(
                decisionSource.Task,
                Task.Delay(TimeSpan.FromSeconds(30))
            );

            if (completedTask != decisionSource.Task)
            {
                _logger.LogWarning("Timeout waiting for reply decision");
                return new ProcessResult(false, null);
            }

            var decision = await decisionSource.Task;

            if (decision.ShouldReply && decision.Content != null)
            {
                // アシスタントメッセージを履歴に追加
                await _historyManager.AddAssistantMessageAsync(context.GuildId, context.ChannelId, decision.Content);
            }

            return new ProcessResult(decision.ShouldReply, decision.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            return new ProcessResult(false, null);
        }
    }
}
