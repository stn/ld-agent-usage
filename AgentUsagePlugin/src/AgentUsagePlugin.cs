namespace Loupedeck.AgentUsagePlugin
{
    using System;

    public class AgentUsagePlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        internal UsageStore Usage { get; } = new();

        public AgentUsagePlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load() => this.Usage.Start();

        public override void Unload() => this.Usage.Dispose();
    }
}
