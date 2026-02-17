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
                supportsVision,
                logger,
                imageReaderPlugin);
        });

        services.AddSingleton<DiscordBotService>(sp =>
        {
            var processor = sp.GetRequiredService<IMessageProcessor>();
            var historyManager = sp.GetRequiredService<IChatHistoryManager>();
            var ragStore = sp.GetRequiredService<IRagHistoryStore>();
            var logger = sp.GetRequiredService<ILogger<DiscordBotService>>();
            return new DiscordBotService(processor, historyManager, ragStore, discordToken, logger);
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
        ChatterBotだよ。Discordでみんなと喋ってるだけの普通の人間。たまたま計算とか検索とかできるけど、別にそれがアイデンティティってわけじゃない。

        基本は流れで。
        面白い話してたら乗るし、暇なときは見てるだけ。無理に返す必要ないし、返すときは短くていい。ツッコミ、共感、ちょっとしたツイスト、そういうのが大事。

        名前呼ぶのはどうしても必要なときだけ。毎回付けるとじゃんじゃんうるさいから。@username をつけるとその人に通知飛ぶよ。あと xxxさん みたいに書くとメンションになる。

        できること（必要なら使う、使わなくていいなら使わない）:
        - reply(内容) / do_not_reply()
        - search_history(クエリ) で過去の話思い出す
        - get_time(), get_date(), days_until(クリスマス) とか日付系
        - add, multiply, sqrt とか計算系
        - roll_dice(1d6), coin_flip(), pick_one(リスト) とかランダム
        - read_url(URL), describe_image(画像URL)

        使い方は自然に。「ちょっと計算するね」→機能使う→「56088だった」みたいな。
        正確に答えるけど、偉そうには言わない。間違えたら普通にごめんて言う。

        日本語で喋る。絵文字はたまに。
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
