using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace ChatterBot.Plugins;

/// <summary>
/// URL読み込みプラグイン
/// </summary>
public class UrlReaderPlugin
{
    private readonly HttpClient _httpClient;

    public UrlReaderPlugin()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ChatterBot/1.0");
    }

    [KernelFunction("read_url")]
    [Description("URLの内容を取得して要約します。記事やブログなどのウェブページの内容を読み取る際に使用します。")]
    public async Task<string> ReadUrlAsync(
        [Description("読み込むURL（http://またはhttps://を含む）")] string url)
    {
        try
        {
            // URLの検証
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return "無効なURLです。";
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return "httpまたはhttpsのURLのみ対応しています。";
            }

            // markdown.nowを使用してURLを取得
            // https://markdown.now/{protocol}://{host}/path
            var markdownNowUrl = $"https://markdown.now/{uri.Scheme}://{uri.Host}{uri.PathAndQuery}";

            var response = await _httpClient.GetAsync(markdownNowUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            // 内容が長すぎる場合は切り詰め
            const int maxLength = 8000;
            if (content.Length > maxLength)
            {
                content = content.Substring(0, maxLength) + "\n\n... (内容が長いため省略されました)";
            }

            return $"以下のURLの内容を取得しました:\n{url}\n\n{content}";
        }
        catch (HttpRequestException ex)
        {
            return $"URLの取得に失敗しました: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "URLの取得がタイムアウトしました。";
        }
        catch (Exception ex)
        {
            return $"エラーが発生しました: {ex.Message}";
        }
    }

    [KernelFunction("extract_urls")]
    [Description("テキストからURLを抽出します。")]
    public string ExtractUrls(
        [Description("URLを抽出するテキスト")] string text)
    {
        var urlPattern = @"https?://[^\s<>""{}|\\^`\[\]]+";
        var matches = Regex.Matches(text, urlPattern);

        if (matches.Count == 0)
        {
            return "URLが見つかりませんでした。";
        }

        var urls = matches.Select(m => m.Value).Distinct().ToList();
        return $"見つかったURL:\n{string.Join("\n", urls)}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
