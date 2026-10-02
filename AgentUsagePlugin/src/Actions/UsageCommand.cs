namespace Loupedeck.AgentUsagePlugin
{
    using System;
    using System.Globalization;

    public abstract class UsageCommand : PluginDynamicCommand
    {
        private static readonly BitmapColor Background = new(0x0F, 0x1B, 0x2D);
        private static readonly BitmapColor CodexAccent = new(0x3D, 0xD6, 0xC0);
        private static readonly BitmapColor ClaudeAccent = new(0xF0, 0xE0, 0xB8);
        private static readonly BitmapColor Warning = new(0xFF, 0x6B, 0x5A);
        private static readonly BitmapColor Muted = new(0xB8, 0xC4, 0xD6);
        private static readonly BitmapColor Track = new(0x2E, 0x3E, 0x5C);

        private const Double WarningThreshold = 20;

        // 4 ボタンの下段を揃えて切り替えるため、ボタンごとではなく共有で持つ
        private static Boolean s_showCountdown;

        private readonly String _title;
        private readonly String _resetFormat;
        private readonly BitmapColor _accent;
        private readonly Func<UsageSnapshot, UsageWindow> _selectWindow;

        protected UsageCommand(Boolean isClaude, Boolean isWeekly)
            : base(
                displayName: $"{(isClaude ? "Claude" : "Codex")} {(isWeekly ? "W" : "5h")}",
                description: "Codex / Claude の残量",
                groupName: "Agent Usage")
        {
            this._title = isClaude ? "CLAUDE" : "CODEX";
            this._resetFormat = isWeekly ? "HH:mm (ddd)" : "HH:mm";
            this._accent = isClaude ? ClaudeAccent : CodexAccent;
            this._selectWindow = (isClaude, isWeekly) switch
            {
                (true, true) => s => s.ClaudeSevenDay,
                (true, false) => s => s.ClaudeFiveHour,
                (false, true) => s => s.CodexSevenDay,
                (false, false) => s => s.CodexFiveHour,
            };
        }

        private UsageStore Usage => ((AgentUsagePlugin)this.Plugin).Usage;

        protected override Boolean OnLoad()
        {
            this.Usage.Changed += this.ActionImageChanged;
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.Usage.Changed -= this.ActionImageChanged;
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            s_showCountdown = !s_showCountdown;
            this.Usage.Refresh();
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var window = this._selectWindow(this.Usage.Current);
            var now = DateTimeOffset.Now;
            var remaining = window?.RemainingPercent(now);
            var color = remaining < WarningThreshold ? Warning : this._accent;

            using var b = new BitmapBuilder(imageSize);
            var w = b.Width;
            var h = b.Height;
            Int32 Y(Double ratio) => (Int32)(h * ratio);

            b.Clear(Background);
            b.DrawText(this._title, 0, Y(0.02), w, Y(0.12), this._accent, Y(0.11));
            b.DrawText(remaining is { } p ? $"{p:0}%" : "--", 0, Y(0.26), w, Y(0.34), color, Y(0.30));

            var barX = w / 8;
            var barW = w - (barX * 2);
            var barY = Y(0.63);
            var barH = Math.Max(2, Y(0.05));
            b.FillRectangle(barX, barY, barW, barH, Track);
            if (remaining is { } r)
            {
                b.FillRectangle(barX, barY, (Int32)(barW * r / 100), barH, color);
            }

            var resetText = window?.ResetsAt is { } at && at > now
                ? s_showCountdown ? FormatCountdown(at - now) : at.ToLocalTime().ToString(this._resetFormat, CultureInfo.InvariantCulture)
                : "";
            b.DrawText(resetText, 0, Y(0.70), w, Y(0.24), Muted, Y(0.16));

            return b.ToImage();
        }

        private static String FormatCountdown(TimeSpan left) => left switch
        {
            { TotalDays: >= 1 } => $"{(Int32)left.TotalDays}d{left.Hours}h",
            { TotalHours: >= 1 } => $"{(Int32)left.TotalHours}h{left.Minutes:00}m",
            _ => $"{left.Minutes}m",
        };
    }

    public sealed class ClaudeWeeklyCommand : UsageCommand
    {
        public ClaudeWeeklyCommand() : base(isClaude: true, isWeekly: true) { }
    }

    public sealed class ClaudeFiveHourCommand : UsageCommand
    {
        public ClaudeFiveHourCommand() : base(isClaude: true, isWeekly: false) { }
    }

    public sealed class CodexWeeklyCommand : UsageCommand
    {
        public CodexWeeklyCommand() : base(isClaude: false, isWeekly: true) { }
    }

    public sealed class CodexFiveHourCommand : UsageCommand
    {
        public CodexFiveHourCommand() : base(isClaude: false, isWeekly: false) { }
    }
}
