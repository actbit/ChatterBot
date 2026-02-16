using ChatterBot.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ChatterBot.Services;

/// <summary>
/// ChatHistory管理サービス（SQLite永続化付き）
/// </summary>
public class ChatHistoryManager : IChatHistoryManager, IDisposable
{
    private readonly string _connectionString;
    private readonly int _maxMessages;
    private readonly Dictionary<ulong, ChatHistory> _histories;
    private readonly Dictionary<ulong, bool> _loadedChannels;
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public ChatHistoryManager(string databasePath, int maxMessages = 30)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={databasePath}";
        _maxMessages = maxMessages;
        _histories = new Dictionary<ulong, ChatHistory>();
        _loadedChannels = new Dictionary<ulong, bool>();
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeDatabaseAsync()
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                guild_id INTEGER,
                channel_id INTEGER NOT NULL,
                user_id INTEGER NOT NULL,
                user_name TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                embedding BLOB,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_chat_messages_guild_channel
                ON chat_messages(guild_id, channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_channel
                ON chat_messages(channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_created
                ON chat_messages(created_at);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static ulong GetHistoryKey(ulong? guildId, ulong channelId)
    {
        // guildIdとchannelIdを組み合わせて一意のキーを作成
        // DMの場合はguildIdがnullなので、channelIdのみを使用
        return guildId.HasValue
            ? (guildId.Value << 32) | channelId
            : channelId;
    }

    public ChatHistory GetOrCreateHistory(ulong? guildId, ulong channelId)
    {
        var key = GetHistoryKey(guildId, channelId);

        lock (_lock)
        {
            if (!_histories.TryGetValue(key, out var history))
            {
                history = new ChatHistory();
                _histories[key] = history;
            }
            return history;
        }
    }

    public async Task AddUserMessageAsync(ulong? guildId, ulong channelId, ulong userId, string userName, string content)
    {
        var key = GetHistoryKey(guildId, channelId);

        lock (_lock)
        {
            if (_histories.TryGetValue(key, out var history))
            {
                TrimHistoryIfNeeded(history);
                history.Add(new ChatMessageContent(AuthorRole.User, content) { AuthorName = userName });

                // 同時にRAG Storeにも保存するために、DBに直接保存
                SaveToDatabaseAsync(guildId, channelId, userId, userName, "user", content).GetAwaiter().GetResult();
            }
        }

        await Task.CompletedTask;
    }

    public async Task AddAssistantMessageAsync(ulong? guildId, ulong channelId, string content)
    {
        var key = GetHistoryKey(guildId, channelId);

        lock (_lock)
        {
            if (_histories.TryGetValue(key, out var history))
            {
                TrimHistoryIfNeeded(history);
                history.AddAssistantMessage(content);

                // アシスタントメッセージも保存
                SaveToDatabaseAsync(guildId, channelId, 0, "ChatterBot", "assistant", content).GetAwaiter().GetResult();
            }
        }

        await Task.CompletedTask;
    }

    public async Task UpdateUserMessageAsync(ulong? guildId, ulong channelId, ulong userId, string userName, string newContent, string oldContent)
    {
        var key = GetHistoryKey(guildId, channelId);

        lock (_lock)
        {
            if (_histories.TryGetValue(key, out var history))
            {
                // メモリ上のChatHistoryで該当メッセージを探して更新
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    var msg = history[i];
                    if (msg.Role == AuthorRole.User &&
                        msg.AuthorName == userName &&
                        msg.Content == oldContent)
                    {
                        // 内容を更新
                        history[i] = new ChatMessageContent(AuthorRole.User, newContent) { AuthorName = userName };
                        break;
                    }
                }

                // DBも更新
                UpdateInDatabaseAsync(guildId, channelId, userId, oldContent, newContent).GetAwaiter().GetResult();
            }
        }

        await Task.CompletedTask;
    }

    private void TrimHistoryIfNeeded(ChatHistory history)
    {
        while (history.Count >= _maxMessages)
        {
            history.RemoveAt(0);
        }
    }

    public async Task LoadRecentHistoryAsync(ulong? guildId, ulong channelId, int days)
    {
        var key = GetHistoryKey(guildId, channelId);

        lock (_lock)
        {
            if (_loadedChannels.ContainsKey(key))
            {
                return; // 既に読み込み済み
            }
            _loadedChannels[key] = true;
        }

        var history = GetOrCreateHistory(guildId, channelId);
        history.Clear();

        var cutoffDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss");

        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT user_name, role, content
            FROM chat_messages
            WHERE channel_id = $channelId
              AND ($guildId IS NULL AND guild_id IS NULL OR guild_id = $guildId)
              AND created_at >= $cutoffDate
            ORDER BY created_at ASC
            LIMIT $limit
            """;

        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$cutoffDate", cutoffDate);
        command.Parameters.AddWithValue("$limit", _maxMessages);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var userName = reader.GetString(0);
            var role = reader.GetString(1);
            var content = reader.GetString(2);

            if (role == "user")
            {
                history.Add(new ChatMessageContent(AuthorRole.User, content) { AuthorName = userName });
            }
            else
            {
                history.AddAssistantMessage(content);
            }
        }
    }

    private async Task SaveToDatabaseAsync(ulong? guildId, ulong channelId, ulong userId, string userName, string role, string content)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_messages (guild_id, channel_id, user_id, user_name, role, content)
            VALUES ($guildId, $channelId, $userId, $userName, $role, $content)
            """;

        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$userId", (long)userId);
        command.Parameters.AddWithValue("$userName", userName);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);

        await command.ExecuteNonQueryAsync();
    }

    private async Task UpdateInDatabaseAsync(ulong? guildId, ulong channelId, ulong userId, string oldContent, string newContent)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE chat_messages
            SET content = $newContent
            WHERE channel_id = $channelId
              AND ($guildId IS NULL AND guild_id IS NULL OR guild_id = $guildId)
              AND user_id = $userId
              AND content = $oldContent
            """;

        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$userId", (long)userId);
        command.Parameters.AddWithValue("$oldContent", oldContent);
        command.Parameters.AddWithValue("$newContent", newContent);

        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
