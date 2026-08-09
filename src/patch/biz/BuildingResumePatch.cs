using HarmonyLib;
using UI.InGameUI;

namespace BA.src.patch.biz;

[HarmonyPatch]
internal class BuildingResumePatch
{

    [HarmonyPatch(typeof(BuildingResume), "UpdateDetails")]
    [HarmonyPostfix]
    static void OnBuildingResumeUpdateDetails(ref BuildingResume __instance)
    {
        if (__instance.CityBuildingController?.building == null)
            return;

        Plugin.Logger.LogDebug($"OnBuildingResumeUpdateDetails: building={__instance.CityBuildingController.building.Address}");

        if (Plugin.bestBuildings.TryGetValue(__instance.CityBuildingController.building.Address, out int rank))
        {
            __instance.addressLabel.TextContainer.text = $"<color={Plugin.BIZ_BEST_BUILDING_COLOR}>#{rank} BEST</color> {__instance.addressLabel.TextContainer.text}";
        }
    }
}