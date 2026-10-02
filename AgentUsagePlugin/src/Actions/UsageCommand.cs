namespace Loupedeck.AgentUsagePlugin
{
    using System;

    public abstract class UsageCommand : PluginDynamicCommand
    {
        private static readonly BitmapColor Background = new(0x0F, 0x1B, 0x2D);
        private static readonly BitmapColor CodexAccent = new(0x3D, 0xD6, 0xC0);
        private static readonly BitmapColor ClaudeAccent = new(0xF0, 0xE0, 0xB8);
        private static readonly BitmapColor Warning = new(0xFF, 0x6B, 0x5A);
        private static readonly BitmapColor Muted = new(0xB8, 0xC4, 0xD6);
        private static readonly BitmapColor Track = new(0x2E, 0x3E, 0x5C);

        private const Double WarningThreshold = 20;

        private readonly String _title;
        private readonly String _windowLabel;
        private readonly BitmapColor _accent;
        private readonly Func<UsageSnapshot, UsageWindow> _selectWindow;

        protected UsageCommand(Boolean isClaude, Boolean isWeekly)
            : base(
                displayName: $"{(isClaude ? "Claude" : "Codex")} {(isWeekly ? "週間" : "5時間")}",
                description: "Codex / Claude の残量",
                groupName: "Agent Usage")
        {
            this._title = isClaude ? "CLAUDE" : "CODEX";
            this._windowLabel = isWeekly ? "週間" : "5時間";
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

        protected override void RunCommand(String actionParameter) => this.Usage.Refresh();

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
            b.DrawText(this._title, 0, Y(0.02), w, Y(0.18), this._accent, Y(0.15));
            b.DrawText($"{this._windowLabel}・残り", 0, Y(0.19), w, Y(0.14), Muted, Y(0.11));
            b.DrawText(remaining is { } p ? $"{p:0}%" : "--", 0, Y(0.33), w, Y(0.30), color, Y(0.27));

            var barX = w / 8;
            var barW = w - (barX * 2);
            var barY = Y(0.66);
            var barH = Math.Max(2, Y(0.05));
            b.FillRectangle(barX, barY, barW, barH, Track);
            if (remaining is { } r)
            {
                b.FillRectangle(barX, barY, (Int32)(barW * r / 100), barH, color);
            }

            var resetText = window?.ResetsAt is { } at && at > now ? at.ToLocalTime().ToString("MM/dd HH:mm") : "";
            b.DrawText(resetText, 0, Y(0.76), w, Y(0.20), Muted, Y(0.15));

            return b.ToImage();
        }
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
