# ChatterBot

Discordで自然に会話に参加するチャットボット。Semantic Kernel + Function Callingで、AIが自ら「返信するかどうか」を判断します。

## 特徴

- **自然な会話参加** - メンションされなくても、面白そうな話題には横入りする
- **ハイブリッド履歴管理** - 直近の会話(ChatHistory) + 長期記憶(RAG検索)
- **OpenAI互換API対応** - OpenAI、GLM(ZhipuAI)など様々なAIモデルを使用可能
- **サーバー分離** - 異なるDiscordサーバーの履歴が混ざらない
- **多彩な機能** - 計算、日付、ランダム、URL読み込み、画像認識など

## セットアップ

### 必要条件

- .NET 10.0 SDK
- Discord Bot Token
- OpenAI API Key (または互換API)

### 環境変数

```bash
# Discord
DISCORD_BOT_TOKEN=your_discord_bot_token

# Chat Completion
OPENAI_MODEL_ID=gpt-4o
OPENAI_API_KEY=your_api_key
OPENAI_ENDPOINT=  # 省略可。GLMの場合: https://open.bigmodel.cn/api/paas/v4/

# Vision (画像認識用、オプション)
VISION_MODEL_ID=gpt-4o
VISION_API_KEY=your_vision_api_key
VISION_ENDPOINT=

# Embedding (RAG検索用)
EMBEDDING_PROVIDER=openai  # openai, glm, または none
EMBEDDING_MODEL_ID=text-embedding-3-small
EMBEDDING_API_KEY=your_embedding_key
EMBEDDING_ENDPOINT=
```

### 設定ファイル

`appsettings.json` で環境変数を参照：

```json
{
  "Discord": {
    "Token": "${DISCORD_BOT_TOKEN}"
  },
  "OpenAI": {
    "ModelId": "${OPENAI_MODEL_ID}",
    "ApiKey": "${OPENAI_API_KEY}",
    "Endpoint": "${OPENAI_ENDPOINT}"
  }
}
```

### 実行

```bash
cd ChatterBot
dotnet run
```

## 機能一覧

Botが使用できる機能（Function Calling）：

| 機能 | 説明 |
|-----|------|
| `reply(content)` | 返信する |
| `do_not_reply()` | 返信しない（見てるだけ） |
| `search_history(query)` | 過去の会話を検索 |
| `get_time()` | 現在時刻 |
| `get_date()` | 今日の日付 |
| `days_until(target)` | 指定日までの日数（christmas, newyear など） |
| `add`, `subtract`, `multiply`, `divide` | 四則演算 |
| `sqrt`, `pow`, `abs` | 数学関数 |
| `sin`, `cos`, `tan`, `asin`, `acos`, `atan` | 三角関数 |
| `roll_dice(notation)` | サイコロ（例: 1d6, 2d10） |
| `coin_flip()` | コイントス |
| `pick_one(list)` | リストから1つ選ぶ |
| `shuffle(list)` | リストをシャッフル |
| `read_url(url)` | URLの内容を読み込む |
| `describe_image(url)` | 画像の内容を説明 |

## メンション変換

Botが返信する際、自動的にメンション形式に変換：

- `@username` → Discordメンション
- `xxxさん` → Discordメンション（表示名から日本語・英語のみ抽出）

## アーキテクチャ

```
Discord Bot (Discord.Net)
      ↓
DiscordBotService
      ↓
IMessageProcessor
      ↓
SemanticKernelMessageProcessor
      ├── ChatHistoryManager (直近N日分)
      ├── SqliteRagHistoryStore (全期間 + Embedding)
      └── Plugins (Function Calling)
            ├── ReplyPlugin
            ├── HistorySearchPlugin
            ├── TimePlugin
            ├── MathPlugin
            ├── RandomPlugin
            ├── UrlReaderPlugin
            └── ImageReaderPlugin
```

## データベース

SQLiteで永続化：

```sql
CREATE TABLE chat_messages (
    id INTEGER PRIMARY KEY,
    guild_id INTEGER,      -- サーバーID
    channel_id INTEGER,    -- チャンネルID
    user_id INTEGER,
    user_name TEXT,
    role TEXT,             -- user/assistant
    content TEXT,
    embedding BLOB,        -- RAG用
    created_at TEXT
);
```

## Docker

```bash
docker build -t chatterbot .
docker run -e DISCORD_BOT_TOKEN=xxx -e OPENAI_API_KEY=xxx chatterbot
```

## ライセンス

MIT
