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

            CREATE TABLE IF NOT EXISTS channel_info (
                channel_id INTEGER PRIMARY KEY,
                guild_id INTEGER,
                is_public INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS channel_members (
                channel_id INTEGER NOT NULL,
                user_id INTEGER NOT NULL,
                PRIMARY KEY (channel_id, user_id)
            );

            CREATE INDEX IF NOT EXISTS idx_chat_messages_guild_channel
                ON chat_messages(guild_id, channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_channel
                ON chat_messages(channel_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_created
                ON chat_messages(created_at);
            CREATE INDEX IF NOT EXISTS idx_channel_members_channel
                ON channel_members(channel_id);
            """;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// チャンネル情報とメンバーを更新
    /// </summary>
    public async Task UpdateChannelInfoAsync(ulong? guildId, ulong channelId, bool isPublic, IReadOnlyList<ulong> memberIds)
    {
        // チャンネル情報を更新
        var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO channel_info (channel_id, guild_id, is_public)
            VALUES ($channelId, $guildId, $isPublic)
            ON CONFLICT(channel_id) DO UPDATE SET
                guild_id = excluded.guild_id,
                is_public = excluded.is_public
            """;

        command.Parameters.AddWithValue("$channelId", (long)channelId);
        command.Parameters.AddWithValue("$guildId", guildId.HasValue ? (long)guildId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$isPublic", isPublic ? 1 : 0);

        await command.ExecuteNonQueryAsync();

        // 既存のメンバーを削除
        var deleteCommand = _connection.CreateCommand();
        deleteCommand.CommandText = "DELETE FROM channel_members WHERE channel_id = $channelId";
        deleteCommand.Parameters.AddWithValue("$channelId", (long)channelId);
        await deleteCommand.ExecuteNonQueryAsync();

        // 新しいメンバーを追加
        foreach (var userId in memberIds)
        {
            var insertCommand = _connection.CreateCommand();
            insertCommand.CommandText = """
                INSERT OR IGNORE INTO channel_members (channel_id, user_id)
                VALUES ($channelId, $userId)
                """;
            insertCommand.Parameters.AddWithValue("$channelId", (long)channelId);
            insertCommand.Parameters.AddWithValue("$userId", (long)userId);
            await insertCommand.ExecuteNonQueryAsync();
        }
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

    public async Task UpdateAsync(ulong? guildId, ulong channelId, ulong userId, string oldContent, string newContent)
    {
        byte[]? embeddingBytes = null;

        if (_embeddingEnabled && _embeddingGenerator != null)
        {
            try
            {
                var result = await _embeddingGenerator.GenerateAsync(newContent);
                embeddingBytes = EmbeddingToBytes(result.Vector);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Embedding generation failed: {ex.Message}");
            }
        }

        var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE chat_messages
            SET content = $newContent, embedding = $embedding
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
        command.Parameters.AddWithValue("$embedding", embeddingBytes != null ? embeddingBytes : DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<HistoryRecord>> SearchAsync(
        string query,
        ulong? currentGuildId,
        ulong? currentChannelId,
        bool isCurrentChannelPublic,
        IReadOnlyList<ulong> currentMemberIds,
        int? days,
        int limit)
    {
        var results = new List<HistoryRecord>();

        // 事前に現在のチャンネルのメンバーを取得（DMなどの場合は空）
        var currentMembers = new HashSet<ulong>(currentMemberIds);

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

                // チャンネル情報とJOIN
                var command = _connection.CreateCommand();
                var sql = """
                    SELECT m.id, m.guild_id, m.channel_id, m.user_id, m.user_name, m.role, m.content, m.embedding, m.created_at,
                           COALESCE(c.is_public, 1) as is_public
                    FROM chat_messages m
                    LEFT JOIN channel_info c ON m.channel_id = c.channel_id
                    WHERE m.embedding IS NOT NULL
                    """;

                if (cutoffDate != null)
                {
                    sql += " AND m.created_at >= $cutoffDate";
                    command.Parameters.AddWithValue("$cutoffDate", cutoffDate);
                }

                sql += " ORDER BY m.created_at DESC LIMIT 200";
                command.CommandText = sql;

                var candidates = new List<(HistoryRecord Record, float Similarity)>();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var recordGuildId = reader.IsDBNull(1) ? null : (ulong?)reader.GetInt64(1);
                        var recordChannelId = (ulong)reader.GetInt64(2);
                        var recordIsPublic = reader.GetInt32(9) == 1;

                        // 可視性チェック
                        if (!await IsChannelVisibleAsync(recordIsPublic, recordGuildId, recordChannelId, currentGuildId, currentChannelId, currentMembers))
                        {
                            continue;
                        }

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
                return await TextSearchAsync(query, currentGuildId, currentChannelId, currentMembers, days, limit);
            }
        }
        else
        {
            return await TextSearchAsync(query, currentGuildId, currentChannelId, currentMembers, days, limit);
        }

        return results;
    }

    /// <summary>
    /// チャンネルの可視性を判定
    /// </summary>
    private async Task<bool> IsChannelVisibleAsync(
        bool recordIsPublic,
        ulong? recordGuildId,
        ulong recordChannelId,
        ulong? currentGuildId,
        ulong? currentChannelId,
        HashSet<ulong> currentMembers)
    {
        // 同じチャンネルなら常に見える
        if (currentChannelId.HasValue && recordChannelId == currentChannelId.Value)
        {
            return true;
        }

        // Publicチャンネルは同じギルド内でのみ見える
        if (recordIsPublic)
        {
            return recordGuildId.HasValue &&
                   currentGuildId.HasValue &&
                   recordGuildId.Value == currentGuildId.Value;
        }

        // Privateチャンネルは、現在のチャンネルのメンバー全員が
        // そのチャンネルのメンバーに含まれている場合のみ見える
        return await IsAllCurrentMembersInChannelAsync(recordChannelId, currentMembers);
    }

    /// <summary>
    /// 現在のチャンネルのメンバー全員が、指定されたチャンネルのメンバーに含まれているか
    /// </summary>
    private async Task<bool> IsAllCurrentMembersInChannelAsync(ulong channelId, HashSet<ulong> currentMembers)
    {
        if (currentMembers.Count == 0)
        {
            // メンバー情報がない場合は見せない（安全側）
            return false;
        }

        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT user_id FROM channel_members WHERE channel_id = $channelId
            """;
        command.Parameters.AddWithValue("$channelId", (long)channelId);

        var channelMembers = new HashSet<ulong>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            channelMembers.Add((ulong)reader.GetInt64(0));
        }

        // チャンネルにメンバーが登録されていない場合は見せない
        if (channelMembers.Count == 0)
        {
            return false;
        }

        // 現在のメンバー全員が、チャンネルのメンバーに含まれているか
        foreach (var member in currentMembers)
        {
            if (!channelMembers.Contains(member))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<HistoryRecord>> TextSearchAsync(
        string query,
        ulong? currentGuildId,
        ulong? currentChannelId,
        HashSet<ulong> currentMembers,
        int? days,
        int limit)
    {
        var results = new List<HistoryRecord>();

        var cutoffDate = days.HasValue
            ? DateTime.UtcNow.AddDays(-days.Value).ToString("yyyy-MM-dd HH:mm:ss")
            : null;

        var command = _connection.CreateCommand();
        var sql = """
            SELECT m.guild_id, m.channel_id, m.user_id, m.user_name, m.role, m.content, m.created_at,
                   COALESCE(c.is_public, 1) as is_public
            FROM chat_messages m
            LEFT JOIN channel_info c ON m.channel_id = c.channel_id
            WHERE m.content LIKE $query
            """;

        command.Parameters.AddWithValue("$query", $"%{query}%");

        if (cutoffDate != null)
        {
            sql += " AND m.created_at >= $cutoffDate";
            command.Parameters.AddWithValue("$cutoffDate", cutoffDate);
        }

        sql += " ORDER BY m.created_at DESC LIMIT 200";
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var recordGuildId = reader.IsDBNull(0) ? null : (ulong?)reader.GetInt64(0);
            var recordChannelId = (ulong)reader.GetInt64(1);
            var recordIsPublic = reader.GetInt32(7) == 1;

            // 可視性チェック
            if (!await IsChannelVisibleAsync(recordIsPublic, recordGuildId, recordChannelId, currentGuildId, currentChannelId, currentMembers))
            {
                continue;
            }

            var recordUserId = (ulong)reader.GetInt64(2);
            var recordUserName = reader.GetString(3);
            var recordRole = reader.GetString(4);
            var recordContent = reader.GetString(5);
            var createdAt = DateTime.Parse(reader.GetString(6));

            results.Add(new HistoryRecord(recordGuildId, recordChannelId, recordUserId, recordUserName, recordRole, recordContent, createdAt, null));

            if (results.Count >= limit)
                break;
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
