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

        if (Plugin.bestBuildings.TryGetValue(__instance.CityBuildingController.building.Address, out int rank))
        {
            __instance.addressLabel.TextContainer.text = $"<color=#CC6600>#{rank} BEST</color> {__instance.addressLabel.TextContainer.text}";
        }
    }
}