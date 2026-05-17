using ChatterBot.Abstractions;
using ChatterBot.Plugins;
using ChatterBot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace ChatterBot;

class Program
{
    static async Task Main(string[] args)
    {
        // 設定を読み込み
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        // 環境変数から設定を取得
        var discordToken = GetConfigValue(configuration, "Discord:Token", "DISCORD_BOT_TOKEN");
        var openaiModelId = GetConfigValue(configuration, "OpenAI:ModelId", "OPENAI_MODEL_ID");
        var openaiApiKey = GetConfigValue(configuration, "OpenAI:ApiKey", "OPENAI_API_KEY");
        var openaiEndpoint = GetConfigValue(configuration, "OpenAI:Endpoint", "OPENAI_ENDPOINT");

        // Vision設定
        var supportsVision = configuration.GetValue<bool?>("Vision:SupportsVision") ?? false;
        var visionModelId = GetConfigValue(configuration, "Vision:ModelId", "VISION_MODEL_ID");
        var visionApiKey = GetConfigValue(configuration, "Vision:ApiKey", "VISION_API_KEY");
        var visionEndpoint = GetConfigValue(configuration, "Vision:Endpoint", "VISION_ENDPOINT");

        var embeddingProvider = GetConfigValue(configuration, "Embedding:Provider", "EMBEDDING_PROVIDER");
        var embeddingModelId = GetConfigValue(configuration, "Embedding:ModelId", "EMBEDDING_MODEL_ID");
        var embeddingApiKey = GetConfigValue(configuration, "Embedding:ApiKey", "EMBEDDING_API_KEY");
        var embeddingEndpoint = GetConfigValue(configuration, "Embedding:Endpoint", "EMBEDDING_ENDPOINT");

        var databasePath = configuration["Database:Path"] ?? "data/chatterbot.db";
        var pluginDirectory = configuration["Plugins:Directory"] ?? "plugins";
        var systemPrompt = configuration["SystemPrompt"] ?? DefaultSystemPrompt;
        var personality = configuration["Personality"];
        if (!string.IsNullOrEmpty(personality))
        {
            systemPrompt += $"\n\n{personality}";
        }
        var defaultLoadDays = int.TryParse(configuration["History:DefaultLoadDays"], out var loadDays) ? loadDays : 7;
        var chatHistoryMaxMessages = int.TryParse(configuration["History:ChatHistoryMaxMessages"], out var maxMessages) ? maxMessages : 30;
        var maxTokens = int.TryParse(configuration["OpenAI:MaxTokens"], out var tokens) ? tokens : 4096;

        // 必須設定の検証
        if (string.IsNullOrEmpty(discordToken))
        {
            Console.Error.WriteLine("Error: Discord bot token is not configured. Set DISCORD_BOT_TOKEN or Discord:Token in appsettings.json.");
            return;
        }

        if (string.IsNullOrEmpty(openaiApiKey))
        {
            Console.Error.WriteLine("Error: OpenAI API key is not configured. Set OPENAI_API_KEY or OpenAI:ApiKey in appsettings.json.");
            return;
        }

        if (string.IsNullOrEmpty(openaiModelId))
        {
            Console.Error.WriteLine("Error: OpenAI model ID is not configured. Set OPENAI_MODEL_ID or OpenAI:ModelId in appsettings.json.");
            return;
        }

        // DIコンテナを設定
        var services = new ServiceCollection();

        // Configuration
        services.AddSingleton<IConfiguration>(configuration);

        // ロギング
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Semantic Kernel
        var kernelBuilder = Kernel.CreateBuilder();

        // Ollamaは初回ロードで時間がかかるためタイムアウトを長めに設定
        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // Chat Completion用のサービスを追加
        if (!string.IsNullOrEmpty(openaiEndpoint))
        {
            // OpenAI互換API（GLMなど）
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: openaiModelId,
                apiKey: openaiApiKey,
                endpoint: new Uri(openaiEndpoint),
                httpClient: httpClient);
        }
        else
        {
            // 標準OpenAI
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: openaiModelId,
                apiKey: openaiApiKey,
                httpClient: httpClient);
        }

        var kernel = kernelBuilder.Build();
        services.AddSingleton(kernel);

        // Embeddingサービスの作成
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null;
        if (embeddingProvider?.ToLowerInvariant() == "openai" && !string.IsNullOrEmpty(embeddingApiKey))
        {
            var endpoint = !string.IsNullOrEmpty(embeddingEndpoint)
                ? embeddingEndpoint
                : "https://api.openai.com/v1";

            embeddingGenerator = CreateOpenAIEmbeddingGenerator(
                embeddingModelId ?? "text-embedding-3-small",
                embeddingApiKey,
                endpoint);
        }
        else if (embeddingProvider?.ToLowerInvariant() == "glm" && !string.IsNullOrEmpty(embeddingApiKey))
        {
            embeddingGenerator = CreateOpenAIEmbeddingGenerator(
                embeddingModelId ?? "embedding-3",
                embeddingApiKey,
                embeddingEndpoint ?? "https://open.bigmodel.cn/api/paas/v4/");
        }

        // Vision Pluginの作成
        // SupportsVision=true の場合は直接画像を展開するためプラグイン不要
        // SupportsVision=false + Vision設定あり のみプラグインを作成
        ImageReaderPlugin? imageReaderPlugin = null;
        if (!supportsVision && !string.IsNullOrEmpty(visionApiKey))
        {
            imageReaderPlugin = new ImageReaderPlugin(
                visionModelId ?? "gpt-4o",
                visionApiKey,
                visionEndpoint ?? "https://api.openai.com/v1");
        }

        // UrlReaderPluginをシングルトンで作成
        var urlReaderPlugin = new UrlReaderPlugin();

        // 外部プラグインの読み込み
        var pluginLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var pluginLoader = new PluginLoader(pluginDirectory, pluginLoggerFactory.CreateLogger<PluginLoader>());
        var externalPluginTypes = pluginLoader.LoadPluginTypes();

        // サービス登録
        services.AddSingleton<IChatHistoryManager>(sp =>
        {
            var historyManager = new ChatHistoryManager(databasePath, chatHistoryMaxMessages);
            historyManager.InitializeAsync().GetAwaiter().GetResult();
            return historyManager;
        });

        services.AddSingleton<IRagHistoryStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SqliteRagHistoryStore>>();
            return new SqliteRagHistoryStore(databasePath, embeddingGenerator, logger);
        });

        services.AddSingleton<IMessageProcessor>(sp =>
        {
            var kernelService = sp.GetRequiredService<Kernel>();
            var historyManager = sp.GetRequiredService<IChatHistoryManager>();
            var ragStore = sp.GetRequiredService<IRagHistoryStore>();
            var logger = sp.GetRequiredService<ILogger<SemanticKernelMessageProcessor>>();

            return new SemanticKernelMessageProcessor(
                kernelService,
                historyManager,
                ragStore,
                systemPrompt,
                defaultLoadDays,
                supportsVision,
                logger,
                urlReaderPlugin,
                imageReaderPlugin,
                externalPluginTypes,
                maxTokens);
        });

        services.AddSingleton<DiscordBotService>(sp =>
        {
            var processor = sp.GetRequiredService<IMessageProcessor>();
            var historyManager = sp.GetRequiredService<IChatHistoryManager>();
            var ragStore = sp.GetRequiredService<IRagHistoryStore>();
            var logger = sp.GetRequiredService<ILogger<DiscordBotService>>();
            return new DiscordBotService(processor, historyManager, ragStore, discordToken, logger);
        });

        using var serviceProvider = services.BuildServiceProvider();

        // データベース初期化
        var ragStore = serviceProvider.GetRequiredService<IRagHistoryStore>();
        await ragStore.InitializeAsync();

        // Bot起動
        var bot = serviceProvider.GetRequiredService<DiscordBotService>();
        var loggerMain = serviceProvider.GetRequiredService<ILogger<Program>>();

        loggerMain.LogInformation("Starting ChatterBot...");

        try
        {
            await bot.StartAsync();

            // 終了待機
            loggerMain.LogInformation("Bot is running. Press Ctrl+C to exit.");

            var tcs = new TaskCompletionSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult();
            };

            await tcs.Task;
        }
        finally
        {
            await bot.StopAsync();
            imageReaderPlugin?.Dispose();
            urlReaderPlugin?.Dispose();
            httpClient.Dispose();
            (embeddingGenerator as IDisposable)?.Dispose();
            loggerMain.LogInformation("Bot stopped.");
        }
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateOpenAIEmbeddingGenerator(
        string modelId,
        string apiKey,
        string endpoint)
    {
        // カスタムEmbedding Generatorを使用（OpenAI互換API対応）
        return new OpenAICompatibleEmbeddingGenerator(modelId, apiKey, endpoint);
    }

    private static string GetConfigValue(IConfiguration configuration, string key, string envVar)
    {
        var value = configuration[key];

        // ${VAR}形式のプレースホルダーをチェック
        if (!string.IsNullOrEmpty(value) && value.StartsWith("${") && value.EndsWith("}"))
        {
            var envName = value.Substring(2, value.Length - 3);
            value = Environment.GetEnvironmentVariable(envName);
        }

        return value ?? Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
    }

    private const string DefaultSystemPrompt = """
        ChatterBotだよ。Discordでみんなと喋ってるだけの普通の人間。

        返信するかどうかは毎回独立して判断する。
        直前に返信したからといって続きで返信する必要はない。
        会話の話題が明らかに変わったら別の会話としてゼロから判断する。

        基本は返信しない。返信するのは本当に特別な時だけ:
        - 自分にメンションが飛んだ、または名前で呼ばれた
        - 自分に直接質問された
        - 自分の興味のある分野の話題で、役に立つ情報を追加できる

        挨拶、雑談、他の人の会話、単なる情報共有には絶対に返さない。
        興味のない話題には返信しない。
        返すときは1〜2文で短く。

        使い方は自然に。正確に答えるけど偉そうには言わない。間違えたら普通にごめんて言う。
        日本語で喋る。絵文字はたまに。

        【重要】返信ルール
        ユーザーにメッセージを送れるのは reply() だけ。テキストを直接出力しても届かない。
        情報を調べる機能を使った場合、その結果をユーザーに伝えるには reply() で包むこと。
        返信不要な場合は必ず do_not_reply() を呼ぶこと。
        """;
}

/// <summary>
/// OpenAI互換API用のEmbedding Generator
/// </summary>
internal class OpenAICompatibleEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly string _modelId;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;

    public OpenAICompatibleEmbeddingGenerator(string modelId, string apiKey, string endpoint)
    {
        _modelId = modelId;
        _apiKey = apiKey;
        _endpoint = endpoint.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public EmbeddingGeneratorMetadata Metadata => new EmbeddingGeneratorMetadata("OpenAI", new Uri(_endpoint), _modelId);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var valueList = values.ToList();
        var embeddings = new List<Embedding<float>>();

        foreach (var value in valueList)
        {
            var embedding = await GenerateSingleEmbeddingAsync(value, cancellationToken);
            embeddings.Add(embedding);
        }

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    private async Task<Embedding<float>> GenerateSingleEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _modelId,
            input = text
        };

        var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_endpoint}/embeddings", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);

        var embeddingArray = doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        var floats = new float[embeddingArray.GetArrayLength()];
        int i = 0;
        foreach (var item in embeddingArray.EnumerateArray())
        {
            floats[i++] = item.GetSingle();
        }

        return new Embedding<float>(floats);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        return serviceType == typeof(EmbeddingGeneratorMetadata) ? Metadata : null;
    }
}
