namespace Loupedeck.AgentUsagePlugin
{
    using System;

    // 回答待ち → 実行中 → 待機の順に並べたセッションの index 番目を表示する
    public abstract class SessionSlotCommand : PluginDynamicCommand
    {
        private static readonly BitmapColor Background = new(0x0F, 0x1B, 0x2D);
        private static readonly BitmapColor WaitingBackground = new(0x5A, 0x1E, 0x1A);
        private static readonly BitmapColor Waiting = new(0xFF, 0x6B, 0x5A);
        private static readonly BitmapColor Running = new(0x3D, 0xD6, 0xC0);
        private static readonly BitmapColor Idle = new(0xB8, 0xC4, 0xD6);
        private static readonly BitmapColor ProjectBackdrop = new(new BitmapColor(0xF0, 0xE0, 0xB8), 70);

        private const Double LineHeightRatio = 1.15;

        private readonly Int32 _index;

        protected SessionSlotCommand(Int32 index)
            : base(
                displayName: $"セッション {index + 1}",
                description: "Claude Code セッションの状態（回答待ちを優先表示）",
                groupName: "Agent Sessions")
        {
            this._index = index;
        }

        private SessionStore Sessions => ((AgentUsagePlugin)this.Plugin).Sessions;

        protected override Boolean OnLoad()
        {
            this.Sessions.Changed += this.ActionImageChanged;
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.Sessions.Changed -= this.ActionImageChanged;
            return true;
        }

        protected override void RunCommand(String actionParameter) => this.Sessions.Refresh();

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var sessions = this.Sessions.Current;
            var session = this._index < sessions.Count ? sessions[this._index] : null;

            using var b = new BitmapBuilder(imageSize);
            var w = b.Width;
            var h = b.Height;
            Int32 Y(Double ratio) => (Int32)(h * ratio);

            if (session is null)
            {
                b.Clear(Background);
                return b.ToImage();
            }

            var (label, color) = session.State switch
            {
                SessionState.Waiting => ("Wait", Waiting),
                SessionState.Running => ("Run", Running),
                _ => ("", Idle),
            };

            b.Clear(session.State == SessionState.Waiting ? WaitingBackground : Background);
            DrawProjectBackdrop(b, session.Project);
            b.DrawText(label, 0, Y(0.30), w, Y(0.40), color, Y(0.32));
            b.DrawText(session.Detail ?? "", 0, Y(0.68), w, Y(0.22), Idle, Y(0.13));

            return b.ToImage();
        }

        // プロジェクト名を折り返して画像全体に大きく敷き、状態表示はその上に重ねる
        private static void DrawProjectBackdrop(BitmapBuilder b, String project)
        {
            if (String.IsNullOrEmpty(project))
            {
                return;
            }

            var pad = b.Width * 0.06;
            var areaW = b.Width - (pad * 2);
            var areaH = b.Height - (pad * 2);

            // 行数を変えて、文字が最も大きくなる折り返し方を選ぶ
            String[] bestLines = [project];
            var bestSize = 0.0;
            for (var count = 1; count <= 4 && count <= project.Length; count++)
            {
                var lines = SplitByWidth(project, count);
                var widest = lines.Max(EstimateWidth);
                var size = Math.Min(areaW / widest, areaH / (lines.Length * LineHeightRatio));
                if (size > bestSize)
                {
                    bestSize = size;
                    bestLines = lines;
                }
            }

            var lineH = bestSize * LineHeightRatio;
            var top = (b.Height - (lineH * bestLines.Length)) / 2;
            for (var i = 0; i < bestLines.Length; i++)
            {
                b.DrawText(bestLines[i], (Int32)pad, (Int32)(top + (lineH * i)), (Int32)areaW, (Int32)lineH, ProjectBackdrop, (Int32)bestSize);
            }
        }

        // 推定字幅の累積が total * k / count に最も近い位置で切り、各行の見た目の幅をそろえる
        private static String[] SplitByWidth(String text, Int32 count)
        {
            var total = EstimateWidth(text);
            var lines = new List<String>();
            var start = 0;
            var cumulative = 0.0;
            for (var i = 0; i < text.Length; i++)
            {
                var cw = CharWidth(text[i]);
                if (i > start && lines.Count < count - 1 && cumulative + (cw / 2) > total * (lines.Count + 1) / count)
                {
                    lines.Add(text[start..i]);
                    start = i;
                }

                cumulative += cw;
            }

            lines.Add(text[start..]);
            return [.. lines];
        }

        private static Double EstimateWidth(String text) => text.Sum(CharWidth);

        // SDK に文字幅の計測 API がないため、プロポーショナルフォントのおおよその字幅（em 単位）で見積もる
        private static Double CharWidth(Char c) => c switch
        {
            'i' or 'j' or 'l' or 'I' or '.' or ',' or '\'' or '|' or '!' or ':' => 0.30,
            'f' or 't' or 'r' or '-' or ' ' or '(' or ')' => 0.40,
            'm' or 'w' or 'M' or 'W' => 0.88,
            _ when c > 0x7F => 1.0,
            _ when Char.IsUpper(c) => 0.70,
            _ => 0.58,
        };
    }

    public sealed class SessionSlot1Command : SessionSlotCommand
    {
        public SessionSlot1Command() : base(0) { }
    }

    public sealed class SessionSlot2Command : SessionSlotCommand
    {
        public SessionSlot2Command() : base(1) { }
    }

    public sealed class SessionSlot3Command : SessionSlotCommand
    {
        public SessionSlot3Command() : base(2) { }
    }

    public sealed class SessionSlot4Command : SessionSlotCommand
    {
        public SessionSlot4Command() : base(3) { }
    }

    public sealed class SessionSlot5Command : SessionSlotCommand
    {
        public SessionSlot5Command() : base(4) { }
    }

    public sealed class SessionSlot6Command : SessionSlotCommand
    {
        public SessionSlot6Command() : base(5) { }
    }

    public sealed class SessionSlot7Command : SessionSlotCommand
    {
        public SessionSlot7Command() : base(6) { }
    }

    public sealed class SessionSlot8Command : SessionSlotCommand
    {
        public SessionSlot8Command() : base(7) { }
    }

    public sealed class SessionSlot9Command : SessionSlotCommand
    {
        public SessionSlot9Command() : base(8) { }
    }

    public sealed class SessionSlot10Command : SessionSlotCommand
    {
        public SessionSlot10Command() : base(9) { }
    }

    public sealed class SessionSlot11Command : SessionSlotCommand
    {
        public SessionSlot11Command() : base(10) { }
    }

    public sealed class SessionSlot12Command : SessionSlotCommand
    {
        public SessionSlot12Command() : base(11) { }
    }
}
