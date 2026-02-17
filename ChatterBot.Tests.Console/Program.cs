using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

namespace ChatterBot.Tests.Console;

/// <summary>
/// 会話コンテキスト認識テスト
/// 3つのトピックの会話履歴を含めた状態で、LLMが直近のトピックを正しく認識して応答できるか確認する
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // 設定読み込み
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        var openAiSection = config.GetSection("OpenAI");
        var modelId = openAiSection["ModelId"] ?? throw new Exception("ModelId not set");
        var apiKey = openAiSection["ApiKey"] ?? throw new Exception("ApiKey not set");
        var endpoint = openAiSection["Endpoint"];

        var systemPrompt = config["SystemPrompt"] ?? "";
        var maxMessagesStr = config["History:MaxMessages"] ?? "30";
        var maxMessages = int.TryParse(maxMessagesStr, out var parsed) ? parsed : 30;

        // サンプル履歴読み込み
        var sampleHistory = await LoadSampleHistoryAsync();

        // Kernel構築
        var kernelBuilder = Kernel.CreateBuilder();
#pragma warning disable SKEXP0010 // OpenAI non-Azure endpoints
        if (!string.IsNullOrEmpty(endpoint))
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: apiKey,
                endpoint: new Uri(endpoint));
        }
        else
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: apiKey);
        }
#pragma warning restore SKEXP0010
        var kernel = kernelBuilder.Build();

        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

        System.Console.WriteLine("=== 会話コンテキスト認識テスト ===");
        System.Console.WriteLine($"モデル: {modelId}");
        System.Console.WriteLine($"最大履歴件数: {maxMessages}");
        System.Console.WriteLine();

        // メニュー表示
        while (true)
        {
            System.Console.WriteLine("--- メニュー ---");
            System.Console.WriteLine("1: トピックAのみ（ゲームの話）");
            System.Console.WriteLine("2: トピックA+B（ゲーム→料理）");
            System.Console.WriteLine("3: トピックA+B+C（ゲーム→料理→旅行）★推奨");
            System.Console.WriteLine("4: カスタム選択");
            System.Console.WriteLine("q: 終了");
            System.Console.WriteLine();

            System.Console.Write("選択: ");
            var input = System.Console.ReadLine();

            if (input == "q") break;

            int topicCount = input switch
            {
                "1" => 1,
                "2" => 2,
                "3" => 3,
                "4" => -1,
                _ => 3
            };

            if (topicCount == -1)
            {
                // カスタム選択
                System.Console.WriteLine("含めるトピックをスペース区切りで入力 (例: A B C): ");
                var topics = System.Console.ReadLine()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
                await RunTestAsync(chatCompletion, systemPrompt, sampleHistory, topics, maxMessages);
            }
            else
            {
                var topics = Enumerable.Range(0, topicCount).Select(i => ((char)('A' + i)).ToString()).ToArray();
                await RunTestAsync(chatCompletion, systemPrompt, sampleHistory, topics, maxMessages);
            }

            System.Console.WriteLine("\n--- Enter で続行 ---");
            System.Console.ReadLine();
            System.Console.Clear();
        }
    }

    static async Task RunTestAsync(
        IChatCompletionService chatCompletion,
        string systemPrompt,
        List<TopicHistory> sampleHistory,
        string[] selectedTopics,
        int maxMessages)
    {
        // ChatHistory構築
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);

        var allMessages = new List<(string Topic, SampleMessage Msg)>();

        // 選択されたトピックのメッセージを収集
        foreach (var topicChar in selectedTopics)
        {
            var topicIndex = topicChar.ToUpperInvariant()[0] - 'A';
            if (topicIndex >= 0 && topicIndex < sampleHistory.Count)
            {
                var topic = sampleHistory[topicIndex];
                foreach (var msg in topic.Messages)
                {
                    allMessages.Add((topic.Topic, msg));
                }
            }
        }

        // メッセージ数制限（後ろから取得 = 直近優先）
        if (allMessages.Count > maxMessages)
        {
            allMessages = allMessages.TakeLast(maxMessages).ToList();
        }

        // ChatHistoryに追加
        System.Console.WriteLine($"\n=== 使用する履歴 ({allMessages.Count}件) ===\n");

        foreach (var (topic, msg) in allMessages)
        {
            if (msg.Role == "user")
            {
                chatHistory.AddUserMessage($"[{msg.Author}]: {msg.Content}");
                System.Console.WriteLine($"[{msg.Author}]: {msg.Content}");
            }
            else
            {
                chatHistory.AddAssistantMessage(msg.Content);
                System.Console.WriteLine($"[ChatterBot]: {msg.Content}");
            }
        }

        System.Console.WriteLine("\n===============================\n");

        // テストプロンプト
        System.Console.WriteLine("テスト用プロンプトを入力 (何も入力しないとデフォルト使用): ");
        var customPrompt = System.Console.ReadLine();

        var testPrompts = string.IsNullOrEmpty(customPrompt)
            ? new[]
            {
                "で、どう思う？",
                "続きは？",
                "それで？"
            }
            : new[] { customPrompt };

        foreach (var prompt in testPrompts)
        {
            System.Console.WriteLine($"\n>>> ユーザー入力: \"{prompt}\"");
            System.Console.WriteLine();

            var testHistory = new ChatHistory();
            foreach (var msg in chatHistory)
            {
                testHistory.Add(msg);
            }
            testHistory.AddUserMessage(prompt);

            // 応答生成（Function Callingなし、シンプルに）
            var result = await chatCompletion.GetChatMessageContentAsync(testHistory);

            System.Console.WriteLine($"<<< 応答: {result.Content}");
            System.Console.WriteLine();
        }
    }

    static async Task<List<TopicHistory>> LoadSampleHistoryAsync()
    {
        var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "SampleHistory.json");
        var json = await File.ReadAllTextAsync(jsonPath);
        return JsonSerializer.Deserialize<List<TopicHistory>>(json) ?? [];
    }
}

/// <summary>
/// トピック履歴
/// </summary>
class TopicHistory
{
    public string Topic { get; set; } = "";
    public List<SampleMessage> Messages { get; set; } = [];
}

/// <summary>
/// サンプルメッセージ
/// </summary>
class SampleMessage
{
    public string Role { get; set; } = "";
    public string Author { get; set; } = "";
    public string Content { get; set; } = "";
}
