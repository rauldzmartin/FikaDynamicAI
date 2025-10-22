using Comfort.Common;
using EFT;
using Fika.Core.Main.GameMode;
using FikaDynamicAI.Scripts;
using SPT.Reflection.Patching;
using System.Reflection;

namespace FikaDynamicAI.Patches;

internal class BotsController_SetSettings_Postfix : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotsController)
            .GetMethod(nameof(BotsController.SetSettings));
    }

    [PatchPostfix]
    public static void Postfix()
    {
#if DEBUG
        FikaDynamicAI_Plugin.PluginLogger.LogInfo("Checking for HostGameController");
#endif
        if (Singleton<IFikaGame>.Instance.GameController is HostGameController gameController)
        {
            FikaDynamicAI_Plugin.PluginLogger.LogInfo("Adding dynamic AI component");
            gameController.GameInstance.gameObject.AddComponent<FikaDynamicAIManager>();
        }
    }
}
