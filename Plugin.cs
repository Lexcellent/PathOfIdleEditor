using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace PathOfIdleEditor;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("PathOfIdle.exe")]
public sealed class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    public override void Load()
    {
        Log = base.Log;

        // 管道监听在线程池中运行；所有游戏对象读写会由 BridgeBehaviour 转回 Unity 主线程。
        BridgeServer.Start();
        ClassInjector.RegisterTypeInIl2Cpp<BridgeBehaviour>();
        AddComponent<BridgeBehaviour>();
        Log.LogInfo("Path of Idle Editor bridge loaded. Start the standalone editor to connect.");
    }
}
