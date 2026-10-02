namespace Loupedeck.AgentUsagePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;

    internal record UsageWindow(Double UsedPercent, DateTimeOffset? ResetsAt)
    {
        // ログは最後に使った時点の値なので、リセット時刻を過ぎていれば満タンとみなす
        public Double RemainingPercent(DateTimeOffset now) =>
            this.ResetsAt is { } r && r <= now ? 100 : Math.Clamp(100 - this.UsedPercent, 0, 100);
    }

    internal record UsageSnapshot(UsageWindow ClaudeFiveHour, UsageWindow ClaudeSevenDay, UsageWindow CodexFiveHour, UsageWindow CodexSevenDay)
    {
        public static readonly UsageSnapshot Empty = new(null, null, null, null);
    }

    // Claude は Claude Code mod (agent-usage) が書き出す ~/.agent-usage/claude.json、
    // Codex は ~/.codex/sessions の jsonl を直接読む
    internal sealed class UsageStore : IDisposable
    {
        private const Int32 TailChunkBytes = 256 * 1024;
        private const Int32 MaxCodexFilesToScan = 5;

        private readonly String _claudePath;
        private readonly String _codexSessionsDir;
        private readonly Object _refreshLock = new();
        private FileSystemWatcher _watcher;
        private Timer _timer;

        public UsageSnapshot Current { get; private set; } = UsageSnapshot.Empty;

        public event Action Changed;

        public UsageStore()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            this._claudePath = Path.Combine(home, ".agent-usage", "claude.json");
            this._codexSessionsDir = Path.Combine(home, ".codex", "sessions");
        }

        public void Start()
        {
            var dir = Path.GetDirectoryName(this._claudePath);
            Directory.CreateDirectory(dir);
            this._watcher = new FileSystemWatcher(dir, Path.GetFileName(this._claudePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            this._watcher.Changed += (_, _) => this.Refresh();
            this._watcher.Created += (_, _) => this.Refresh();
            this._watcher.Renamed += (_, _) => this.Refresh();

            // Codex の更新検知とリセット時刻経過の再描画を兼ねる
            this._timer = new Timer(_ => this.Refresh(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public void Refresh()
        {
            lock (this._refreshLock)
            {
                var (claude5h, claude7d) = this.ReadClaude();
                var (codex5h, codex7d) = this.ReadCodex();
                this.Current = new UsageSnapshot(claude5h, claude7d, codex5h, codex7d);
                PluginLog.Verbose($"Usage refreshed: {this.Current}");
            }
            this.Changed?.Invoke();
        }

        private (UsageWindow, UsageWindow) ReadClaude()
        {
            try
            {
                using var doc = JsonDocument.Parse(ReadShared(this._claudePath));
                return (ParseClaudeWindow(doc.RootElement, "five_hour"), ParseClaudeWindow(doc.RootElement, "seven_day"));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                PluginLog.Verbose(ex, "Failed to read Claude usage");
                return (null, null);
            }
        }

        private static UsageWindow ParseClaudeWindow(JsonElement root, String kind)
        {
            if (!root.TryGetProperty(kind, out var w) || w.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            DateTimeOffset? resetsAt = w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(r.GetString())
                : null;
            return new UsageWindow(w.GetProperty("percentUsed").GetDouble(), resetsAt);
        }

        private (UsageWindow, UsageWindow) ReadCodex()
        {
            try
            {
                var files = new DirectoryInfo(this._codexSessionsDir)
                    .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(MaxCodexFilesToScan);
                foreach (var file in files)
                {
                    if (FindLastRateLimits(file.FullName) is { } limits)
                    {
                        return limits;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PluginLog.Verbose(ex, "Failed to read Codex usage");
            }
            return (null, null);
        }

        // セッションログは数 MB になるので末尾から読む
        private static (UsageWindow, UsageWindow)? FindLastRateLimits(String path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var end = stream.Length;
            var carry = Array.Empty<Byte>();
            while (end > 0)
            {
                var start = Math.Max(0, end - TailChunkBytes);
                var buffer = new Byte[end - start + carry.Length];
                stream.Position = start;
                stream.ReadExactly(buffer, 0, (Int32)(end - start));
                carry.CopyTo(buffer, end - start);

                // 先頭の行は途中から始まっている可能性があるので、次のチャンクに持ち越す
                var firstNewline = start == 0 ? -1 : Array.IndexOf(buffer, (Byte)'\n');
                if (start > 0 && firstNewline < 0)
                {
                    carry = buffer;
                    end = start;
                    continue;
                }
                var text = Encoding.UTF8.GetString(buffer, firstNewline + 1, buffer.Length - firstNewline - 1);
                foreach (var line in Enumerable.Reverse(text.Split('\n')))
                {
                    if (line.Contains("\"rate_limits\"") && ParseCodexLine(line) is { } limits)
                    {
                        return limits;
                    }
                }

                carry = firstNewline < 0 ? Array.Empty<Byte>() : buffer[..(firstNewline + 1)];
                end = start;
            }
            return null;
        }

        private static (UsageWindow, UsageWindow)? ParseCodexLine(String line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("payload", out var payload)
                    || !payload.TryGetProperty("rate_limits", out var limits)
                    || limits.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                var primary = ParseCodexWindow(limits, "primary");
                var secondary = ParseCodexWindow(limits, "secondary");
                return primary is null && secondary is null ? null : (primary, secondary);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static UsageWindow ParseCodexWindow(JsonElement limits, String name)
        {
            if (!limits.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            DateTimeOffset? resetsAt = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64())
                : null;
            return new UsageWindow(w.GetProperty("used_percent").GetDouble(), resetsAt);
        }

        private static String ReadShared(String path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public void Dispose()
        {
            this._watcher?.Dispose();
            this._timer?.Dispose();
        }
    }
}
