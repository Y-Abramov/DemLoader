using System;
using Topomatic.ApplicationPlatform.Plugins;

namespace DemLoader
{
    public class PluginHost : PluginHostInitializator
    {
        protected override Type[] GetTypes()
        {
            return new[] { typeof(DemLoaderPlugin) };
        }

        public override void Initialize(PluginFactory factory)
        {
            // ТОЛЬКО base.Initialize - вызов ((PluginHostInitializator)this).Initialize
            // даёт StackOverflow и краш Robur.
            base.Initialize(factory);
        }
    }
}
