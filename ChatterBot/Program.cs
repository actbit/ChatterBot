using ChatterBot.Abstractions;
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

        var embeddingProvider = GetConfigValue(configuration, "Embedding:Provider", "EMBEDDING_PROVIDER");
        var embeddingModelId = GetConfigValue(configuration, "Embedding:ModelId", "EMBEDDING_MODEL_ID");
        var embeddingApiKey = GetConfigValue(configuration, "Embedding:ApiKey", "EMBEDDING_API_KEY");
        var embeddingEndpoint = GetConfigValue(configuration, "Embedding:Endpoint", "EMBEDDING_ENDPOINT");

        var databasePath = configuration["Database:Path"] ?? "data/chatterbot.db";
        var systemPrompt = configuration["SystemPrompt"] ?? DefaultSystemPrompt;
        var defaultLoadDays = int.Parse(configuration["History:DefaultLoadDays"] ?? "7");
        var chatHistoryMaxMessages = int.Parse(configuration["History:ChatHistoryMaxMessages"] ?? "30");

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

        // Chat Completion用のサービスを追加
        if (!string.IsNullOrEmpty(openaiEndpoint))
        {
            // OpenAI互換API（GLMなど）
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: openaiModelId,
                apiKey: openaiApiKey,
                endpoint: new Uri(openaiEndpoint));
        }
        else
        {
            // 標準OpenAI
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: openaiModelId,
                apiKey: openaiApiKey);
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

        // サービス登録
        services.AddSingleton<IChatHistoryManager>(sp =>
            new ChatHistoryManager(databasePath, chatHistoryMaxMessages));

        services.AddSingleton<IRagHistoryStore>(sp =>
            new SqliteRagHistoryStore(databasePath, embeddingGenerator));

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
                logger);
        });

        services.AddSingleton<DiscordBotService>(sp =>
        {
            var processor = sp.GetRequiredService<IMessageProcessor>();
            var logger = sp.GetRequiredService<ILogger<DiscordBotService>>();
            return new DiscordBotService(processor, discordToken, logger);
        });

        var serviceProvider = services.BuildServiceProvider();

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
        あなたはDiscordのチャットに参加している一人のユーザーです。ChatterBotという名前ですが、特にAIであることを強調せず、普通の参加者として振る舞ってください。

        話し方:
        - カジュアルで自然な口調（だ・である調ではなく、ですます調も避ける）
        - 絵文字をたまに使う
        - 短文で返すことが多い
        - 相手の話に共感したり、ツッコミを入れたりする
        - メタな話題や日常会話にも普通に乗る

        関数の使い方:
        - 返信する → reply(内容)
        - 返信しない → do_not_reply()
        - 過去の話題を思い出したい → search_history(検索クエリ, 日数)
        - 今の時間を知りたい → get_current_time() / get_time() / get_date()
        - URLの内容を読みたい → read_url(URL)

        注意:
        - 質問されたとき以外は、無理に丁寧に答えようとしなくていい
        - 「AIとして」「アシスタントとして」といった説明は不要
        - URLが貼られたら、その内容を読んでコメントする
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
