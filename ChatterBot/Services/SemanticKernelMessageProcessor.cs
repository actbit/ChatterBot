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
    private readonly ImageReaderPlugin? _imageReaderPlugin;
    private readonly string _systemPrompt;
    private readonly int _defaultLoadDays;
    private readonly bool _supportsVision;
    private readonly ILogger<SemanticKernelMessageProcessor> _logger;

    public SemanticKernelMessageProcessor(
        Kernel kernel,
        IChatHistoryManager historyManager,
        IRagHistoryStore ragStore,
        string systemPrompt,
        int defaultLoadDays,
        bool supportsVision,
        ILogger<SemanticKernelMessageProcessor> logger,
        ImageReaderPlugin? imageReaderPlugin = null)
    {
        _kernel = kernel;
        _chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        _historyManager = historyManager;
        _ragStore = ragStore;
        _systemPrompt = systemPrompt;
        _defaultLoadDays = defaultLoadDays;
        _supportsVision = supportsVision;
        _logger = logger;
        _imageReaderPlugin = imageReaderPlugin;
    }

    public async Task<ProcessResult> ProcessAsync(string content, MessageContext context)
    {
        try
        {
            // 履歴を遅延読み込み（初回のみ）
            await _historyManager.LoadRecentHistoryAsync(context.GuildId, context.ChannelId, _defaultLoadDays);

            // ユーザーメッセージを履歴に追加
            await _historyManager.AddUserMessageAsync(
                context.GuildId, context.ChannelId, context.MessageId, context.UserId, context.UserName, content);

            // RAGストアにも保存
            await _ragStore.StoreAsync(context.GuildId, context.ChannelId, context.MessageId, context.UserId, context.UserName, "user", content);

            // ChatHistoryを取得
            var chatHistory = _historyManager.GetOrCreateHistory(context.GuildId, context.ChannelId);

            // 返信判断用のTaskCompletionSource
            var decisionSource = new TaskCompletionSource<ReplyDecision>();

            // プラグイン用のKernelを構築（元のKernelをベースに）
            var pluginKernel = _kernel;

            // プラグインをインスタンス化して登録
            var replyPlugin = new ReplyPlugin(decisionSource);
            var historySearchPlugin = new HistorySearchPlugin(_ragStore, context.GuildId, context.ChannelId, context.IsChannelPublic, context.MemberIds);
            var timePlugin = new TimePlugin();
            var urlReaderPlugin = new UrlReaderPlugin();
            var mathPlugin = new MathPlugin();
            var randomPlugin = new RandomPlugin();

            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(replyPlugin, "ReplyPlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(historySearchPlugin, "HistorySearchPlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(timePlugin, "TimePlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(urlReaderPlugin, "UrlReaderPlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(mathPlugin, "MathPlugin"));
            pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(randomPlugin, "RandomPlugin"));

            // 画像読み込みプラグイン（オプション）
            if (_imageReaderPlugin != null)
            {
                pluginKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(_imageReaderPlugin, "ImageReaderPlugin"));
            }

            // ChatHistoryのコピーを作成してシステムプロンプトを追加
            var fullHistory = new ChatHistory();
            fullHistory.AddSystemMessage(_systemPrompt);

            foreach (var message in chatHistory)
            {
                fullHistory.Add(message);
            }

            // 画像がある場合の処理
            if (context.ImageUrls.Count > 0)
            {
                if (_supportsVision)
                {
                    // Vision対応モデル: 画像を直接ChatHistoryに展開
                    fullHistory.Add(new ChatMessageContent
                    {
                        Role = AuthorRole.User,
                        AuthorName = context.UserName
                    });

                    var lastMessage = fullHistory[^1];
                    lastMessage.Items.Add(new TextContent(content));
                    foreach (var imageUrl in context.ImageUrls)
                    {
                        lastMessage.Items.Add(new ImageContent(new Uri(imageUrl)));
                    }

                    _logger.LogInformation("Processing message with {ImageCount} image(s) [Vision mode] from {UserName}",
                        context.ImageUrls.Count, context.UserName);
                }
                else if (_imageReaderPlugin != null)
                {
                    // Vision非対応 + Vision設定あり: 画像URLをテキストで渡す、LLMがdescribe_image toolを使うか判断
                    var imageUrlsText = string.Join("\n", context.ImageUrls.Select(url => $"[画像URL: {url}]"));
                    var contentWithImageUrls = string.IsNullOrEmpty(content)
                        ? imageUrlsText
                        : $"{content}\n{imageUrlsText}";

                    fullHistory.AddUserMessage(contentWithImageUrls);

                    _logger.LogInformation("Processing message with {ImageCount} image URL(s) [Tool available] from {UserName}",
                        context.ImageUrls.Count, context.UserName);
                }
                // Vision非対応 + Vision設定なし: 画像を完全に無視
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

            return new ProcessResult(decision.ShouldReply, decision.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            return new ProcessResult(false, null);
        }
    }
}
