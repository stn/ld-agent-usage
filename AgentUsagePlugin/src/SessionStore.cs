namespace Loupedeck.AgentUsagePlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;

    internal enum SessionState
    {
        Waiting,
        Running,
        Idle,
    }

    internal record SessionInfo(String SessionId, String Project, SessionState State, String Detail, DateTimeOffset UpdatedAt);

    // Claude Code mod (agent-usage) が書き出す ~/.agent-usage/sessions/<id>.json を読む
    internal sealed class SessionStore : IDisposable
    {
        // mod は 60 秒ごとに heartbeatAt を更新するので、それより十分長く途絶えたら落ちたとみなす
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);

        private readonly String _dir;
        private readonly Object _refreshLock = new();
        private FileSystemWatcher _watcher;
        private Timer _timer;

        public IReadOnlyList<SessionInfo> Current { get; private set; } = Array.Empty<SessionInfo>();

        public event Action Changed;

        public SessionStore()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            this._dir = Path.Combine(home, ".agent-usage", "sessions");
        }

        public void Start()
        {
            Directory.CreateDirectory(this._dir);
            this._watcher = new FileSystemWatcher(this._dir, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            this._watcher.Changed += (_, _) => this.Refresh();
            this._watcher.Created += (_, _) => this.Refresh();
            this._watcher.Deleted += (_, _) => this.Refresh();
            this._watcher.Renamed += (_, _) => this.Refresh();

            // heartbeat の途絶検知用
            this._timer = new Timer(_ => this.Refresh(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public void Refresh()
        {
            lock (this._refreshLock)
            {
                var now = DateTimeOffset.UtcNow;
                var sessions = new List<SessionInfo>();
                foreach (var path in Directory.EnumerateFiles(this._dir, "*.json"))
                {
                    if (this.ReadSession(path, now) is { } s)
                    {
                        sessions.Add(s);
                    }
                }
                this.Current = sessions
                    .OrderBy(s => s.State)
                    .ThenByDescending(s => s.UpdatedAt)
                    .ToList();
                PluginLog.Verbose($"Sessions refreshed: {this.Current.Count}");
            }
            this.Changed?.Invoke();
        }

        // 終了済み・途絶したセッションのファイルは mod 側で消せないので、ここで掃除する
        private SessionInfo ReadSession(String path, DateTimeOffset now)
        {
            try
            {
                using var doc = JsonDocument.Parse(ReadShared(path));
                var root = doc.RootElement;
                var state = root.GetProperty("state").GetString();
                var heartbeatAt = DateTimeOffset.Parse(root.GetProperty("heartbeatAt").GetString());
                if (state == "ended" || now - heartbeatAt > StaleAfter)
                {
                    File.Delete(path);
                    return null;
                }
                var cwd = root.GetProperty("cwd").GetString() ?? "";
                return new SessionInfo(
                    root.GetProperty("sessionId").GetString(),
                    Path.GetFileName(cwd.TrimEnd('/', '\\')),
                    state switch
                    {
                        "waiting" => SessionState.Waiting,
                        "running" => SessionState.Running,
                        _ => SessionState.Idle,
                    },
                    root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                    DateTimeOffset.Parse(root.GetProperty("updatedAt").GetString()));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or FormatException or InvalidOperationException)
            {
                PluginLog.Verbose(ex, $"Failed to read session {path}");
                return null;
            }
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
