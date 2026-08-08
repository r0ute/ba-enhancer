using HarmonyLib;
using UI.InGameUI;
using UnityEngine;

namespace BA.src.patch.biz;

[HarmonyPatch]
internal static class BuildingResumePatch
{

    [HarmonyPatch(typeof(BuildingResume), "UpdateDetails")]
    [HarmonyPostfix]
    private static void OnBuildingResumeUpdateDetails(ref BuildingResume __instance)
    {
        if (__instance.CityBuildingController?.building == null)
            return;

        Plugin.Logger.LogDebug($"OnBuildingResumeUpdateDetails: building={__instance.CityBuildingController.building.Address}");

        __instance.addressLabel.TextContainer.color = Plugin.bestBuildings.Contains(__instance.CityBuildingController.building.Address)
            ? Color.blue
            : Color.black;
    }
}