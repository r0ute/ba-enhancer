using System.Linq;
using HarmonyLib;
using UI.Smartphone.Apps.BizMan.Factory;

namespace BA.src.patch.biz;

[HarmonyPatch]
public static class FactoryMachineListPatch
{

    [HarmonyPatch(typeof(BizManFactoryMachineList), "FetchData")]
    [HarmonyPostfix]
    private static void OnBizManFactoryMachineListFetchData(BizManFactoryMachineList __instance)
    {
        BuildingRegistration registration =
            Traverse.Create(__instance)
                .Field("_registration")
                .GetValue<BuildingRegistration>();

        if (registration == null)
            return;

        Plugin.Logger.LogDebug($"OnBizManFactoryMachineListFetchData: registration={registration.Address}");

        registration.itemInstances.Values
            .OfType<FactoryWorkstationInstance>()
            .Where(x => x.SelectedRecipe != null)
            .GroupBy(x => x.selectedRecipeId)
            .ToList()
            .ForEach(recipeGroup =>
            {
                int productionLimit = recipeGroup.Count()
                    * recipeGroup.First().SelectedRecipe.output.amount
                    * 24;

                Plugin.Logger.LogDebug($"OnBizManFactoryMachineListFetchData: item={recipeGroup.First().SelectedRecipe.output.item}, " +
                    $"productionLimit={productionLimit:F0}");

                recipeGroup.ToList()
                    .ForEach(workstation =>
                    {
                        workstation.produceUpTo = true;
                        workstation.produceUpToValue = productionLimit;
                    });
            });
    }
}