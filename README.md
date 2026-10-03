# ld-agent-usage

Loupedeck のキーに Claude Code / Codex のレート制限残量と、実行中の Claude Code セッションの状態を表示するプラグイン。

構成は 2 つに分かれている。

| ディレクトリ | 役割 |
| --- | --- |
| [AgentUsagePlugin/](AgentUsagePlugin/) | Loupedeck（Logi Plugin Service）用プラグイン本体（C# / .NET 10） |
| [claude-mod/agent-usage/](claude-mod/agent-usage/) | Claude Code の mod。残量とセッション状態を `~/.agent-usage/` に書き出す |

Codex の残量は `~/.codex/sessions` のセッションログをプラグインが直接読むので、Codex 側に追加の設定は要らない。

## アクション

### Agent Usage（残量）

| アクション | 表示内容 |
| --- | --- |
| Claude 5h / Claude W | Claude の 5 時間枠 / 週枠の残量 |
| Codex 5h / Codex W | Codex の 5 時間枠 / 週枠の残量 |

- 残量（%）とバー、下段にリセット時刻を表示する。週枠は曜日付き
- 残量が 20% を切ると赤で表示する
- キーを押すと、4 つのキーの下段がそろってリセット時刻と残り時間（例: `2h05m`）の表示に切り替わる
- 最後に記録された値がリセット時刻を過ぎていれば 100% とみなす

### Agent Sessions（セッション状態）

「セッション 1」〜「セッション 12」のスロットに、Claude Code のセッションを次の順で並べて表示する。

1. 回答待ち（`Wait`）: `AskUserQuestion` / `ExitPlanMode` の応答待ち、または権限ダイアログ表示中。背景が赤くなり、待っているツール名を下段に出す
2. 実行中（`Run`）
3. 待機中

同じ状態の中では最後に状態が変わったものが先に来る。背景にはプロジェクト名（作業ディレクトリ名）を大きく敷く。キーを押すと再読み込みする。

mod は 60 秒ごとに heartbeat を書き、3 分以上途絶えたセッションと終了したセッションはプラグイン側でファイルごと削除する。

## セットアップ

### 1. Loupedeck プラグインをビルドする

前提: Logi Plugin Service（Loupedeck 6.0 以降）、.NET 10 SDK。対応デバイスは Loupedeck CT / Live / Live S と Razer Stream Controller 系。

```sh
cd AgentUsagePlugin
dotnet build -c Debug
```

ビルド後に Logi Plugin Service のプラグインディレクトリへ `.link` ファイルが作られ、プラグインの再読み込みが走るので、そのまま Loupedeck の設定画面から「Agent Usage」「Agent Sessions」のアクションをキーに割り当てられる。再読み込みに失敗した場合は Logi Plugin Service を再起動する。

#### パッケージ化

```sh
cd AgentUsagePlugin
dotnet build -c Release
logiplugintool pack ./bin/Release ./AgentUsage.lplug4
logiplugintool install ./AgentUsage.lplug4
```

VS Code では [.vscode/tasks.json](AgentUsagePlugin/.vscode/tasks.json) に同じ手順をタスクとして登録してある。

### 2. Claude Code の mod を入れる

[claude-mod/agent-usage/](claude-mod/agent-usage/) を `--plugin-dir` で Claude Code に読み込ませる。

```sh
claude --plugin-dir path/to/ld-agent-usage/claude-mod/agent-usage
```

読み込まれると、セッション開始時とレート制限の更新時に次のファイルを書き出す。

```
~/.agent-usage/
├── claude.json             # five_hour / seven_day の percentUsed と resetsAt
└── sessions/<sessionId>.json  # state, detail, cwd, updatedAt, heartbeatAt
```

## ライセンス

MIT
