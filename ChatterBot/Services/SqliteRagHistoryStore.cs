using ChatterBot.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace ChatterBot.Services;

/// <summary>
/// SQLite + Embeddingを使用したRAG履歴ストア
/// </summary>
public class SqliteRagHistoryStore : IRagHistoryStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly bool _embeddingEnabled;

    public SqliteRagHistoryStore(string databasePath, IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={databasePath}";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        _embeddingGenerator = embeddingGenerator;
        _embeddingEnabled = embeddingGenerator != null;
    }

    public async Task InitializeAsync()
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

    public async Task StoreAsync(ulong? guildId, ulong channelId, ulong userId, string userName, string role, string content)
    {
        byte[]? embeddingBytes = null;

        if (_embeddingEnabled && _embeddingGenerator != null)
        {
            try
            {
                var result = await _embeddingGenerator.GenerateAsync(content);
                embeddingBytes = EmbeddingToBytes(result.Vector);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Embedding generation failed: {ex.Message}");
            }
        }

        var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_messages (guild_id, channel_id, user_id, user_name, role, content, embedding)
            VALUES ($guildId, $channelId, $userId, $userName, $role, $content, $embedding)
            """;

        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$userId", (long)userId);
        command.Parameters.AddWithValue("$userName", userName);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$embedding", embeddingBytes != null ? embeddingBytes : DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<HistoryRecord>> SearchAsync(
        string query,
        ulong? guildId,
        ulong? channelId,
        int? days,
        int limit)
    {
        var results = new List<HistoryRecord>();

        if (_embeddingEnabled && _embeddingGenerator != null)
        {
            // Embedding検索
            try
            {
                var queryResult = await _embeddingGenerator.GenerateAsync(query);
                var queryEmbedding = queryResult.Vector.ToArray();

                var cutoffDate = days.HasValue
                    ? DateTime.UtcNow.AddDays(-days.Value).ToString("yyyy-MM-dd HH:mm:ss")
                    : null;

                var command = _connection.CreateCommand();
                var sql = """
                    SELECT id, guild_id, channel_id, user_id, user_name, role, content, embedding, created_at
                    FROM chat_messages
                    WHERE ($guildId IS NULL OR guild_id = $guildId OR guild_id IS NULL)
                      AND ($channelId IS NULL OR channel_id = $channelId)
                      AND embedding IS NOT NULL
                    """;

                if (cutoffDate != null)
                {
                    sql += " AND created_at >= $cutoffDate";
                }

                sql += " ORDER BY created_at DESC LIMIT 100";

                command.CommandText = sql;
                command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
                command.Parameters.AddWithValue("$channelId", channelId.HasValue ? (long)channelId.Value : DBNull.Value);

                if (cutoffDate != null)
                {
                    command.Parameters.AddWithValue("$cutoffDate", cutoffDate);
                }

                var candidates = new List<(HistoryRecord Record, float Similarity)>();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var recordGuildId = reader.IsDBNull(1) ? null : (ulong?)reader.GetInt64(1);
                        var recordChannelId = (ulong)reader.GetInt64(2);
                        var recordUserId = (ulong)reader.GetInt64(3);
                        var recordUserName = reader.GetString(4);
                        var recordRole = reader.GetString(5);
                        var recordContent = reader.GetString(6);

                        // BLOBを読み込み
                        float[]? storedEmbedding = null;
                        if (!reader.IsDBNull(7))
                        {
                            using var stream = reader.GetStream(7);
                            using var memoryStream = new MemoryStream();
                            await stream.CopyToAsync(memoryStream);
                            var embeddingBytes = memoryStream.ToArray();
                            if (embeddingBytes.Length > 0)
                            {
                                storedEmbedding = BytesToEmbedding(embeddingBytes);
                            }
                        }

                        var createdAt = DateTime.Parse(reader.GetString(8));

                        if (storedEmbedding != null)
                        {
                            var similarity = CosineSimilarity(queryEmbedding, storedEmbedding);

                            candidates.Add((new HistoryRecord(
                                recordGuildId, recordChannelId, recordUserId, recordUserName, recordRole, recordContent, createdAt, null
                            ), similarity));
                        }
                    }
                }

                // 類似度でソートして上位N件を取得
                results = candidates
                    .OrderByDescending(c => c.Similarity)
                    .Take(limit)
                    .Select(c => c.Record with { RelevanceScore = c.Similarity })
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Embedding search failed: {ex.Message}");
                // Fallback to text search
                return await TextSearchAsync(query, guildId, channelId, days, limit);
            }
        }
        else
        {
            // テキスト検索
            return await TextSearchAsync(query, guildId, channelId, days, limit);
        }

        return results;
    }

    private async Task<IReadOnlyList<HistoryRecord>> TextSearchAsync(
        string query,
        ulong? guildId,
        ulong? channelId,
        int? days,
        int limit)
    {
        var results = new List<HistoryRecord>();

        var cutoffDate = days.HasValue
            ? DateTime.UtcNow.AddDays(-days.Value).ToString("yyyy-MM-dd HH:mm:ss")
            : null;

        var command = _connection.CreateCommand();
        var sql = """
            SELECT guild_id, channel_id, user_id, user_name, role, content, created_at
            FROM chat_messages
            WHERE content LIKE $query
              AND ($guildId IS NULL OR guild_id = $guildId OR guild_id IS NULL)
              AND ($channelId IS NULL OR channel_id = $channelId)
            """;

        if (cutoffDate != null)
        {
            sql += " AND created_at >= $cutoffDate";
        }

        sql += " ORDER BY created_at DESC LIMIT $limit";

        command.CommandText = sql;
        command.Parameters.AddWithValue("$query", $"%{query}%");
        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$channelId", channelId.HasValue ? (long)channelId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        if (cutoffDate != null)
        {
            command.Parameters.AddWithValue("$cutoffDate", cutoffDate);
        }

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var recordGuildId = reader.IsDBNull(0) ? null : (ulong?)reader.GetInt64(0);
            var recordChannelId = (ulong)reader.GetInt64(1);
            var recordUserId = (ulong)reader.GetInt64(2);
            var recordUserName = reader.GetString(3);
            var recordRole = reader.GetString(4);
            var recordContent = reader.GetString(5);
            var createdAt = DateTime.Parse(reader.GetString(6));

            results.Add(new HistoryRecord(recordGuildId, recordChannelId, recordUserId, recordUserName, recordRole, recordContent, createdAt, null));
        }

        return results;
    }

    public async Task<IReadOnlyList<HistoryRecord>> GetRecentAsync(ulong? guildId, ulong channelId, int days)
    {
        var results = new List<HistoryRecord>();
        var cutoffDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss");

        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT guild_id, channel_id, user_id, user_name, role, content, created_at
            FROM chat_messages
            WHERE channel_id = $channelId
              AND ($guildId IS NULL AND guild_id IS NULL OR guild_id = $guildId)
              AND created_at >= $cutoffDate
            ORDER BY created_at DESC
            """;

        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$cutoffDate", cutoffDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var recordGuildId = reader.IsDBNull(0) ? null : (ulong?)reader.GetInt64(0);
            var recordChannelId = (ulong)reader.GetInt64(1);
            var recordUserId = (ulong)reader.GetInt64(2);
            var recordUserName = reader.GetString(3);
            var recordRole = reader.GetString(4);
            var recordContent = reader.GetString(5);
            var createdAt = DateTime.Parse(reader.GetString(6));

            results.Add(new HistoryRecord(recordGuildId, recordChannelId, recordUserId, recordUserName, recordRole, recordContent, createdAt, null));
        }

        return results;
    }

    public async Task<IReadOnlyList<ulong>> GetActiveChannelsAsync(ulong? guildId, int inactiveDaysThreshold)
    {
        var results = new List<ulong>();
        var cutoffDate = DateTime.UtcNow.AddDays(-inactiveDaysThreshold).ToString("yyyy-MM-dd HH:mm:ss");

        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT channel_id
            FROM chat_messages
            WHERE ($guildId IS NULL OR guild_id = $guildId OR guild_id IS NULL)
              AND created_at >= $cutoffDate
            """;

        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$cutoffDate", cutoffDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((ulong)reader.GetInt64(0));
        }

        return results;
    }

    private static byte[] EmbeddingToBytes(ReadOnlyMemory<float> embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        for (int i = 0; i < embedding.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float)), embedding.Span[i]);
        }
        return bytes;
    }

    private static float[] BytesToEmbedding(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        for (int i = 0; i < floats.Length; i++)
        {
            floats[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
        }
        return floats;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        float dotProduct = 0f;
        float magnitudeA = 0f;
        float magnitudeB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = MathF.Sqrt(magnitudeA);
        magnitudeB = MathF.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0f;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
