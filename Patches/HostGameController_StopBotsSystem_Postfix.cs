using Fika.Core.Main.GameMode;
using FikaDynamicAI.Scripts;
using SPT.Reflection.Patching;
using System.Reflection;

namespace FikaDynamicAI.Patches;

internal class HostGameController_StopBotsSystem_Postfix : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HostGameController)
            .GetMethod(nameof(HostGameController.StopBotsSystem));
    }

    [PatchPostfix]
    public static void Postfix()
    {
        if (FikaDynamicAIManager.Instance != null)
        {
            FikaDynamicAIManager.Instance.DestroyComponent();
        }
    }
}
