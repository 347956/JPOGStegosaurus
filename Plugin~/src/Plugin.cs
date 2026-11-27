using BepInEx;
using BepInEx.Logging;
using JPOGStegosaurus.Configuration;
using Dawn;

namespace JPOGStegosaurus {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(DawnLib.PLUGIN_GUID)] 
    public class Plugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;

        private void Awake() {
            Logger = base.Logger;
            BoundConfig = new PluginConfig(base.Config);

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }
    }
}