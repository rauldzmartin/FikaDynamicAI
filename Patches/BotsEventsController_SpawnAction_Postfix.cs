using FikaDynamicAI.Scripts;
using SPT.Reflection.Patching;
using System.Reflection;

namespace FikaDynamicAI.Patches;

internal class BotsEventsController_SpawnAction_Postfix : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotsEventsController)
            .GetMethod(nameof(BotsEventsController.SpawnAction));
    }

    [PatchPostfix]
    public static void Postfix()
    {
        if (FikaDynamicAIManager.Instance != null)
        {
            FikaDynamicAIManager.Instance.AddHumans();
        }
    }
}
