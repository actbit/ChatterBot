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
    private readonly Dictionary<(ulong? GuildId, ulong ChannelId), ChatHistory> _histories;
    private readonly Dictionary<(ulong? GuildId, ulong ChannelId), bool> _loadedChannels;
    private readonly Dictionary<ulong, ChatMessageContent> _messageToContent = new();
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _asyncLock = new(1, 1);

    public ChatHistoryManager(string databasePath, int maxMessages = 30)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={databasePath}";
        _maxMessages = maxMessages;
        _histories = new Dictionary<(ulong? GuildId, ulong ChannelId), ChatHistory>();
        _loadedChannels = new Dictionary<(ulong? GuildId, ulong ChannelId), bool>();
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
    }

    public async Task InitializeAsync()
    {
        await InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id INTEGER NOT NULL,
                guild_id INTEGER,
                channel_id INTEGER NOT NULL,
                user_id INTEGER NOT NULL,
                user_name TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                embedding BLOB,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_chat_messages_message_id
                ON chat_messages(message_id);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_guild_channel
                ON chat_messages(guild_id, channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_channel
                ON chat_messages(channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_created
                ON chat_messages(created_at);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public ChatHistory GetOrCreateHistory(ulong? guildId, ulong channelId)
    {
        var key = (guildId, channelId);

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

    public async Task AddUserMessageAsync(ulong? guildId, ulong channelId, ulong messageId, ulong userId, string userName, string content)
    {
        var key = (guildId, channelId);

        await _asyncLock.WaitAsync();
        try
        {
            if (_histories.TryGetValue(key, out var history))
            {
                TrimHistoryIfNeeded(history);
                var msg = new ChatMessageContent(AuthorRole.User, content) { AuthorName = userName };
                history.Add(msg);
                _messageToContent[messageId] = msg;

                await SaveToDatabaseAsync(guildId, channelId, messageId, userId, userName, "user", content);
            }
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public async Task AddAssistantMessageAsync(ulong? guildId, ulong channelId, ulong messageId, string content)
    {
        var key = (guildId, channelId);

        await _asyncLock.WaitAsync();
        try
        {
            if (_histories.TryGetValue(key, out var history))
            {
                TrimHistoryIfNeeded(history);
                var msg = new ChatMessageContent(AuthorRole.Assistant, content);
                history.Add(msg);
                _messageToContent[messageId] = msg;

                await SaveToDatabaseAsync(guildId, channelId, messageId, 0, "ChatterBot", "assistant", content);
            }
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public async Task UpdateUserMessageAsync(ulong messageId, string userName, string newContent)
    {
        await UpdateInDatabaseAsync(messageId, newContent);

        if (_messageToContent.TryGetValue(messageId, out var existingMsg))
        {
            existingMsg.Content = newContent;
            existingMsg.AuthorName = userName;
        }
    }

    public async Task DeleteUserMessageAsync(ulong messageId)
    {
        await DeleteFromDatabaseAsync(messageId);

        if (_messageToContent.TryGetValue(messageId, out var msgToRemove))
        {
            _messageToContent.Remove(messageId);

            lock (_lock)
            {
                foreach (var kvp in _histories)
                {
                    if (kvp.Value.Remove(msgToRemove))
                        break;
                }
            }
        }
    }

    public async Task DeleteChannelAsync(ulong? guildId, ulong channelId)
    {
        var key = (guildId, channelId);

        lock (_lock)
        {
            _histories.Remove(key);
            _loadedChannels.Remove(key);
        }

        // メモリ上のメッセージ参照も削除
        lock (_lock)
        {
            var messageIdsToRemove = _messageToContent
                .Where(kvp => _histories.ContainsKey((guildId, channelId)))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in messageIdsToRemove)
            {
                _messageToContent.Remove(id);
            }
        }

        await DeleteChannelFromDatabaseAsync(channelId);
    }

    public async Task DeleteGuildAsync(ulong guildId)
    {
        var keysToRemove = _histories.Keys.Where(k => k.GuildId == guildId).ToList();

        lock (_lock)
        {
            foreach (var key in keysToRemove)
            {
                _histories.Remove(key);
                _loadedChannels.Remove(key);
            }
        }

        // ギルドに属するメッセージ参照を削除
        var messageIdsToRemove = _messageToContent.Keys.ToList();
        foreach (var key in keysToRemove)
        {
            foreach (var msgId in messageIdsToRemove)
            {
                if (_messageToContent.TryGetValue(msgId, out var msg) && !_histories.Values.Any(h => h.Contains(msg)))
                {
                    _messageToContent.Remove(msgId);
                }
            }
        }

        await DeleteGuildFromDatabaseAsync(guildId);
    }

    private void TrimHistoryIfNeeded(ChatHistory history)
    {
        while (history.Count >= _maxMessages)
        {
            var removed = history[0];
            history.RemoveAt(0);

            // トリムされたメッセージの参照を削除
            var messageIdToRemove = _messageToContent.FirstOrDefault(kvp => kvp.Value == removed).Key;
            if (messageIdToRemove != 0)
            {
                _messageToContent.Remove(messageIdToRemove);
            }
        }
    }

    public async Task LoadRecentHistoryAsync(ulong? guildId, ulong channelId, int days)
    {
        var key = (guildId, channelId);

        lock (_lock)
        {
            if (_loadedChannels.ContainsKey(key))
            {
                return;
            }
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

        lock (_lock)
        {
            _loadedChannels[key] = true;
        }
    }

    private async Task SaveToDatabaseAsync(ulong? guildId, ulong channelId, ulong messageId, ulong userId, string userName, string role, string content)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_messages (message_id, guild_id, channel_id, user_id, user_name, role, content)
            VALUES ($messageId, $guildId, $channelId, $userId, $userName, $role, $content)
            """;

        command.Parameters.AddWithValue("$messageId", (long)messageId);
        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$userId", (long)userId);
        command.Parameters.AddWithValue("$userName", userName);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);

        await command.ExecuteNonQueryAsync();
    }

    private async Task UpdateInDatabaseAsync(ulong messageId, string newContent)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE chat_messages
            SET content = $newContent
            WHERE message_id = $messageId
            """;

        command.Parameters.AddWithValue("$messageId", (long)messageId);
        command.Parameters.AddWithValue("$newContent", newContent);

        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteFromDatabaseAsync(ulong messageId)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chat_messages WHERE message_id = $messageId";
        command.Parameters.AddWithValue("$messageId", (long)messageId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteChannelFromDatabaseAsync(ulong channelId)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chat_messages WHERE channel_id = $channelId";
        command.Parameters.AddWithValue("$channelId", (long)channelId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteGuildFromDatabaseAsync(ulong guildId)
    {
        var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chat_messages WHERE guild_id = $guildId";
        command.Parameters.AddWithValue("$guildId", (long)guildId);
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        _asyncLock.Dispose();
        _connection.Dispose();
    }
}
