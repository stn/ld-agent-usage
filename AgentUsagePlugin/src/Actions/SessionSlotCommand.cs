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
        private static readonly BitmapColor Text = new(0xF0, 0xE0, 0xB8);

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
                b.DrawText("--", 0, Y(0.35), w, Y(0.30), Idle, Y(0.20));
                return b.ToImage();
            }

            var (label, color) = session.State switch
            {
                SessionState.Waiting => ("回答待ち", Waiting),
                SessionState.Running => ("実行中", Running),
                _ => ("待機", Idle),
            };

            b.Clear(session.State == SessionState.Waiting ? WaitingBackground : Background);
            b.DrawText(session.Project, 0, Y(0.06), w, Y(0.26), Text, Y(0.15));
            b.DrawText(label, 0, Y(0.36), w, Y(0.28), color, Y(0.20));
            b.DrawText(session.Detail ?? "", 0, Y(0.68), w, Y(0.22), Idle, Y(0.13));

            return b.ToImage();
        }
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
}
