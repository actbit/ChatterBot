using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// 画像読み込みプラグイン
/// </summary>
public class ImageReaderPlugin
{
    private readonly string _modelId;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;

    public ImageReaderPlugin(string modelId, string apiKey, string endpoint)
    {
        _modelId = modelId;
        _apiKey = apiKey;
        _endpoint = endpoint.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    [KernelFunction("describe_image")]
    [Description("画像の内容を説明します。画像URLが送信された場合に使用します。")]
    public async Task<string> DescribeImageAsync(
        [Description("画像のURL（http://またはhttps://で始まる）")] string imageUrl,
        [Description("画像についての質問や指示（オプション）")] string? prompt = null)
    {
        try
        {
            // URLの検証
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                return "error: invalid URL";
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return "error: only http/https URLs are supported";
            }

            // OpenAI Vision API形式のリクエストを作成
            var requestBody = new
            {
                model = _modelId,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = prompt ?? "この画像について簡潔に説明してください。何が写っているか、雰囲気はどうかなどを教えてください。"
                            },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = imageUrl
                                }
                            }
                        }
                    }
                },
                max_tokens = 500
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_endpoint}/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return $"error: API request failed ({response.StatusCode})";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var description = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return description ?? "error: no description returned";
        }
        catch (HttpRequestException ex)
        {
            return $"error: failed to fetch image ({ex.Message})";
        }
        catch (TaskCanceledException)
        {
            return "error: request timeout";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
