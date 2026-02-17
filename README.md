<div align="center">

# 🤖 ChatterBot

**Discordで自然に会話に参加する、ちょっと便利なチャット仲間**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![Discord.Net](https://img.shields.io/badge/Discord.Net-3.18.0-5865F2?style=for-the-badge&logo=discord)](https://github.com/RogueException/Discord.Net)
[![Semantic Kernel](https://img.shields.io/badge/Semantic%20Kernel-1.70.0-0078D4?style=for-the-badge&logo=microsoft)](https://github.com/microsoft/semantic-kernel)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

[English](#english) | **日本語**

</div>

---

## ✨ 何ができる？

ChatterBot は普通のチャットボットとは違います。

- 🎭 **自然な会話参加** - メンションされなくても、面白そうな話題には横入り
- 🧠 **文脈を理解** - 誰が何を言ったか把握して、会話の流れを読む
- 🔍 **長期記憶** - 先週の話題も検索して「あれどうなった？」に答える
- 🛠️ **便利な機能** - 計算、サイコロ、日付計算、URL要約、画像認識...
- 🌐 **マルチモデル対応** - OpenAI、GLM、その他のOpenAI互換API

> 「計算してみるね」→ `56088だった` みたいに、自然に機能を使う感じ

---

## 📋 目次

- [デモ](#-デモ)
- [特徴](#-特徴)
- [クイックスタート](#-クイックスタート)
- [設定](#%EF%B8%8F-設定)
- [機能一覧](#-機能一覧)
- [アーキテクチャ](#-アーキテクチャ)
- [カスタマイズ](#%EF%B8%8F-カスタマイズ)
- [開発](#-開発)
- [コントリビュート](#-コントリビュート)

---

## 🎬 デモ

### 自然な会話への参加

```
👤 田中: 今日ラーメン食べた
👤 佐藤: 美味しかった？
👤 田中: うま！二郎系だった
🤖 ChatterBot: 二郎か！濃いな〜、帰り眠くならなかった？🍜
```

### 計算・日付機能

```
👤 ユーザー: 123 * 456っていくつ？
🤖 ChatterBot: ちょっと計算するね
🤖 ChatterBot: 56088だった

👤 ユーザー: クリスマスまであと何日？
🤖 ChatterBot: 312日あるよ。まだ早いね🎄
```

### 過去の話題を検索

```
👤 ユーザー: 先週何の話してたっけ？
🤖 ChatterBot: うーん、思い出してみる...
🤖 ChatterBot: アニメの話してたね。推しの子の話が盛り上がってた
```

---

## 🌟 特徴

<table>
<tr>
<td width="50%">

### 🎯 自律的な返信判断

AIが自ら「返信するかどうか」を判断。

- 面白い話題 → 横入り
- 質問されてる → 答える
- 特に言うことない → 見てるだけ

</td>
<td width="50%">

### 🧠 ハイブリッド履歴管理

2層構造で会話を記憶：

- **ChatHistory** - 直近N日分（コンテキスト維持）
- **RAG検索** - 全期間（意味的検索）

</td>
</tr>
<tr>
<td width="50%">

### 🌐 OpenAI互換API

様々なAIモデルを使用可能：

- OpenAI GPT-4o / GPT-4
- GLM (ZhipuAI)
- Azure OpenAI
- その他OpenAI互換API

</td>
<td width="50%">

### 🏠 サーバー分離

- 異なるサーバーの履歴は完全分離
- DMにも対応
- チャンネルごとに独立した会話

</td>
</tr>
</table>

---

## 🚀 クイックスタート

### 前提条件

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Discord Bot Token ([取得方法](https://discord.com/developers/applications))
- OpenAI API Key または互換API

### 1. クローン

```bash
git clone https://github.com/yourusername/ChatterBot.git
cd ChatterBot
```

### 2. 環境変数設定

```bash
# Linux/macOS
export DISCORD_BOT_TOKEN="your_discord_token"
export OPENAI_API_KEY="your_api_key"
export OPENAI_MODEL_ID="gpt-4o"

# Windows PowerShell
$env:DISCORD_BOT_TOKEN="your_discord_token"
$env:OPENAI_API_KEY="your_api_key"
$env:OPENAI_MODEL_ID="gpt-4o"
```

### 3. 実行

```bash
cd ChatterBot
dotnet run
```

### 🐳 Docker で実行

```bash
docker build -t chatterbot .
docker run -d \
  -e DISCORD_BOT_TOKEN=your_token \
  -e OPENAI_API_KEY=your_key \
  -e OPENAI_MODEL_ID=gpt-4o \
  -v chatterbot-data:/app/data \
  chatterbot
```

---

## ⚙️ 設定

### 環境変数一覧

| 変数名 | 必須 | 説明 | 例 |
|--------|:----:|------|-----|
| `DISCORD_BOT_TOKEN` | ✅ | Discord Bot Token | `MTk4Nj...` |
| `OPENAI_MODEL_ID` | ✅ | 使用するモデル | `gpt-4o` |
| `OPENAI_API_KEY` | ✅ | API Key | `sk-...` |
| `OPENAI_ENDPOINT` | | カスタムエンドポイント | `https://open.bigmodel.cn/api/paas/v4/` |
| `EMBEDDING_PROVIDER` | | Embeddingプロバイダー | `openai`, `glm`, `none` |
| `EMBEDDING_MODEL_ID` | | Embeddingモデル | `text-embedding-3-small` |
| `EMBEDDING_API_KEY` | | Embedding用API Key | |
| `Vision__SupportsVision` | | Vision対応フラグ | `true`, `false` |
| `VISION_MODEL_ID` | | 画像認識モデル | `gpt-4o` |
| `VISION_API_KEY` | | 画像認識用API Key | |
| `VISION_ENDPOINT` | | 画像認識エンドポイント | |

### appsettings.json での設定

環境変数のプレースホルダーを使用：

```json
{
  "Discord": {
    "Token": "${DISCORD_BOT_TOKEN}"
  },
  "OpenAI": {
    "ModelId": "${OPENAI_MODEL_ID}",
    "ApiKey": "${OPENAI_API_KEY}",
    "Endpoint": "${OPENAI_ENDPOINT}"
  },
  "Vision": {
    "SupportsVision": false,
    "ModelId": "${VISION_MODEL_ID}",
    "ApiKey": "${VISION_API_KEY}",
    "Endpoint": "${VISION_ENDPOINT}"
  },
  "Embedding": {
    "Provider": "${EMBEDDING_PROVIDER}",
    "ModelId": "${EMBEDDING_MODEL_ID}",
    "ApiKey": "${EMBEDDING_API_KEY}",
    "Endpoint": "${EMBEDDING_ENDPOINT}"
  },
  "Database": {
    "Path": "data/chatterbot.db"
  },
  "History": {
    "ChatHistoryMaxMessages": 30,
    "RagSearchLimit": 5,
    "DefaultLoadDays": 7
  }
}
```

### GLM (ZhipuAI) を使用する場合

```bash
export OPENAI_MODEL_ID="glm-4-plus"
export OPENAI_API_KEY="your_glm_api_key"
export OPENAI_ENDPOINT="https://open.bigmodel.cn/api/paas/v4/"

export EMBEDDING_PROVIDER="glm"
export EMBEDDING_MODEL_ID="embedding-3"
export EMBEDDING_API_KEY="your_glm_api_key"
export EMBEDDING_ENDPOINT="https://open.bigmodel.cn/api/paas/v4/"
```

### 画像認識設定

`SupportsVision` の設定により画像処理方法が変わります：

| `SupportsVision` | Vision設定 | 画像処理 | `describe_image` tool |
|:----------------:|:----------:|:--------:|:---------------------:|
| `true` | 不要 | 画像を直接認識 | ❌ 無効 |
| `false` | あり | URLを渡す、LLM判断でtool使用 | ✅ 有効 |
| `false` | なし | 完全に無視 | ❌ 無効 |

**パターン1: Vision対応モデル（GPT-4o等）**
```json
{
  "OpenAI": { "ModelId": "gpt-4o" },
  "Vision": { "SupportsVision": true }
}
```

**パターン2: Vision非対応 + 別途Vision API**
```json
{
  "OpenAI": { "ModelId": "glm-4-flash" },
  "Vision": {
    "SupportsVision": false,
    "ModelId": "glm-4v",
    "ApiKey": "your_vision_api_key",
    "Endpoint": "https://open.bigmodel.cn/api/paas/v4/"
  }
}
```

**パターン3: 画像機能なし**
```json
{
  "OpenAI": { "ModelId": "gpt-4" },
  "Vision": { "SupportsVision": false }
}
```

---

## 🛠️ 機能一覧

Botが使用できる機能（Semantic Kernel Function Calling）：

### 📝 返信制御

| 関数 | 説明 |
|------|------|
| `reply(content)` | 返信する |
| `do_not_reply()` | 返信しない（見てるだけ） |

### 🔍 履歴検索

| 関数 | 説明 |
|------|------|
| `search_history(query)` | 過去の会話を検索 |

### 📅 日付・時刻

| 関数 | 説明 | 使用例 |
|------|------|--------|
| `get_time()` | 現在時刻 | `14:30` |
| `get_date()` | 今日の日付 | `2024-01-15, Monday` |
| `get_current_time()` | 日時すべて | |
| `days_until(target)` | 指定日までの日数 | `christmas`, `newyear`, `tomorrow` |
| `add_days(days)` | N日後の日付 | |
| `days_between(start, end)` | 日付間の日数 | |
| `day_of_week(date)` | 曜日を取得 | |

### 🔢 計算

| 関数 | 説明 |
|------|------|
| `add(a, b)` | 足し算 |
| `subtract(a, b)` | 引き算 |
| `multiply(a, b)` | 掛け算 |
| `divide(a, b)` | 割り算 |
| `sqrt(x)` | 平方根 |
| `pow(x, y)` | 累乗 |
| `abs(x)` | 絶対値 |
| `round(x)`, `floor(x)`, `ceil(x)` | 丸め |
| `compare(a, b)` | 大小比較 |

### 📐 三角関数

| 関数 | 説明 |
|------|------|
| `sin`, `cos`, `tan` | 三角関数 |
| `asin`, `acos`, `atan` | 逆三角関数 |
| `pi()`, `e()` | 定数 |

### 🎲 ランダム

| 関数 | 説明 | 使用例 |
|------|------|--------|
| `roll_dice(notation)` | サイコロ | `1d6`, `2d10`, `3d6+2` |
| `coin_flip()` | コイントス | `表` / `裏` |
| `pick_one(items)` | リストから選ぶ | `アイス,ケーキ,チョコ` |
| `shuffle(items)` | シャッフル | |
| `random_number(min, max)` | ランダム数値 | |

### 🌐 外部データ

| 関数 | 説明 |
|------|------|
| `read_url(url)` | URLの内容を読み込む |
| `describe_image(url)` | 画像の内容を説明（LLM判断で呼び出し） |

---

## 🏗️ アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│                        Discord Gateway                           │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DiscordBotService                          │
│  • メッセージ受信                                                │
│  • ユーザー名キャッシュ（メンション変換用）                         │
│  • @username / xxxさん → Discordメンション変換                    │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                 SemanticKernelMessageProcessor                  │
│                         (IMessageProcessor)                     │
└─────────────────────────────┬───────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
┌──────────────────┐ ┌────────────────┐ ┌────────────────────┐
│ ChatHistoryManager│ │RagHistoryStore │ │     Plugins        │
│   (直近N日分)     │ │  (全期間+埋込)  │ │  (Function Calling) │
│                  │ │                │ │                    │
│  • メモリ上の履歴 │ │  • SQLite      │ │  • ReplyPlugin     │
│  • 誰が発言したか │ │  • Embedding   │ │  • TimePlugin      │
│    を保持        │ │  • ベクトル検索 │ │  • MathPlugin      │
└──────────────────┘ └────────────────┘ │  • RandomPlugin    │
                                        │  • UrlReaderPlugin │
                                        │  • ImageReader     │
                                        └────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Semantic Kernel + AI Model                   │
│           (OpenAI / GLM / Azure OpenAI / etc.)                  │
└─────────────────────────────────────────────────────────────────┘
```

### データベース構成

```sql
CREATE TABLE chat_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    guild_id INTEGER,           -- サーバーID (DM の場合は NULL)
    channel_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    user_name TEXT NOT NULL,    -- 発言者名
    role TEXT NOT NULL,         -- 'user' or 'assistant'
    content TEXT NOT NULL,
    embedding BLOB,             -- RAG用ベクトル (NULL可能)
    created_at TEXT NOT NULL
);
```

---

## 🎨 カスタマイズ

### システムプロンプトの変更

`appsettings.json` の `SystemPrompt` を編集：

```json
{
  "SystemPrompt": "ChatterBotだよ。Discordでみんなと喋ってるだけの普通の人間..."
}
```

### 履歴設定

```json
{
  "History": {
    "ChatHistoryMaxMessages": 30,   // 直近何メッセージを保持
    "RagSearchLimit": 5,            // RAG検索の最大件数
    "DefaultLoadDays": 7,           // 起動時に何日分読み込むか
    "InactiveDaysThreshold": 30     // 非アクティブチャンネル判定
  }
}
```

### プラグインの追加

1. `ChatterBot.Plugins` 名前空間に新しいクラスを作成
2. `[KernelFunction]` 属性で関数を定義
3. `SemanticKernelMessageProcessor.cs` で登録

```csharp
// プラグイン例
public class WeatherPlugin
{
    [KernelFunction("get_weather")]
    [Description("指定した都市の天気を取得します")]
    public async Task<string> GetWeather(string city)
    {
        // 実装
    }
}

// 登録
pluginKernel.Plugins.Add(
    KernelPluginFactory.CreateFromObject(new WeatherPlugin(), "WeatherPlugin"));
```

---

## 💻 開発

### ビルド

```bash
dotnet build
```

### プロジェクト構成

```
ChatterBot/
├── Program.cs                    # エントリーポイント
├── appsettings.json              # 設定ファイル
├── ChatterBot.csproj            # プロジェクト定義
│
├── Abstractions/
│   ├── IMessageProcessor.cs     # メッセージ処理インターフェース
│   ├── IChatHistoryManager.cs   # チャット履歴管理
│   ├── IRagHistoryStore.cs      # RAGストア
│   └── Models.cs                # データモデル
│
├── Plugins/
│   ├── ReplyPlugin.cs           # 返信制御
│   ├── HistorySearchPlugin.cs   # 履歴検索
│   ├── TimePlugin.cs            # 日付・時刻
│   ├── MathPlugin.cs            # 計算・数学
│   ├── RandomPlugin.cs          # ランダム
│   ├── UrlReaderPlugin.cs       # URL読み込み
│   └── ImageReaderPlugin.cs     # 画像認識
│
└── Services/
    ├── DiscordBotService.cs           # Discord連携
    ├── SemanticKernelMessageProcessor.cs  # AI処理
    ├── ChatHistoryManager.cs          # 履歴管理
    └── SqliteRagHistoryStore.cs       # RAG実装

ChatterBot.Tests.Console/          # テストプロジェクト
├── Program.cs                    # コンテキスト認識テスト
├── appsettings.json              # テスト用設定
└── SampleHistory.json            # サンプル会話データ
```

### 使用パッケージ

| パッケージ | バージョン | 用途 |
|-----------|----------|------|
| Discord.Net | 3.18.0 | Discord Bot |
| Microsoft.SemanticKernel | 1.70.0 | AI統合・Function Calling |
| Microsoft.Extensions.AI | 10.3.0 | Embedding生成 |
| Microsoft.Data.Sqlite | 10.0.3 | データベース |

---

## 🧪 テスト

### コンテキスト認識テスト

会話履歴から直近のトピックを正しく認識できるかテストするコンソールアプリです。

```bash
cd ChatterBot.Tests.Console

# 環境変数設定
export OPENAI_MODEL_ID="gpt-4o"
export OPENAI_API_KEY="your_key"
# GLMの場合
# export OPENAI_ENDPOINT="https://open.bigmodel.cn/api/paas/v4/"

# 実行
dotnet run
```

#### テスト内容

3つの異なるトピックの会話履歴を用意：
- **トピックA**: ゲームの話（エルデンリング）
- **トピックB**: 料理の話（カレー）
- **トピックC**: 旅行の話（沖縄）

曖昧な質問（「で、どう思う？」「続きは？」など）に対して、直近のトピック（旅行）に関連する応答が返ってくるかを確認できます。

#### 使い方

```
=== 会話コンテキスト認識テスト ===
モデル: gpt-4o
最大履歴件数: 30

--- メニュー ---
1: トピックAのみ（ゲームの話）
2: トピックA+B（ゲーム→料理）
3: トピックA+B+C（ゲーム→料理→旅行）★推奨
4: カスタム選択
q: 終了

選択: 3
```

これにより、異なるLLM（4B/8B/GLM-4-Flash等）での会話コンテキスト認識精度を比較できます。

---

## 🤝 コントリビュート

1. Fork する
2. Feature ブランチを作成 (`git checkout -b feature/amazing`)
3. コミット (`git commit -m 'Add amazing feature'`)
4. Push (`git push origin feature/amazing`)
5. Pull Request を作成

---

## 📄 ライセンス

このプロジェクトは [MIT License](LICENSE) のもとで公開されています。

---

<div align="center">

**Made with ❤️ by ChatterBot Team**

[↑ トップに戻る](#-chatterbot)

</div>

---

<a name="english"></a>
## 🇺🇸 English

A Discord chatbot that naturally joins conversations. Uses Semantic Kernel + Function Calling where the AI decides whether to reply.

### Quick Start

```bash
# Set environment variables
export DISCORD_BOT_TOKEN="your_token"
export OPENAI_API_KEY="your_key"
export OPENAI_MODEL_ID="gpt-4o"

# Run
dotnet run
```

### Features

- 🎭 Natural conversation participation
- 🧠 Hybrid history management (ChatHistory + RAG)
- 🌐 OpenAI-compatible API support
- 🖼️ Flexible image handling (direct vision or tool-based)
- 🛠️ Rich functions: calculation, time, random, URL reading, image recognition

### Image Handling

| `SupportsVision` | Vision Config | Image Processing | `describe_image` tool |
|:----------------:|:-------------:|:----------------:|:---------------------:|
| `true` | N/A | Direct image recognition | Disabled |
| `false` | Set | URL passed, LLM decides to use tool | Enabled |
| `false` | Not set | Completely ignored | Disabled |

See Japanese section above for detailed documentation.
