using Entities;
using HarmonyLib;
using UI.Smartphone.Apps.BizMan;
using UnityEngine;
using UnityEngine.UI;

namespace BA.src.patch;

[HarmonyPatch]
internal class RealEstatePatch
{
    [HarmonyPatch(typeof(RealEstateSettings), "OnEnable")]
    [HarmonyPostfix]
    static void OnRealEstateSettingsEnable(ref RealEstateSettings __instance)
    {
        Traverse traverse = Traverse.Create(__instance);
        RealEstate realEstate = traverse.Field("_realEstate").GetValue<RealEstate>();
        Slider rentSlider = traverse.Field("newRentSlider").GetValue<Slider>();

        if (realEstate == null || rentSlider == null)
        {
            return;
        }

        float marketRent = realEstate.Building.GetBuildingDailyMarketRentPerSqm();
        float minRent = marketRent * Plugin.REAL_ESTATE_MIN_RENT_MULTIPLIER;
        float maxRent = marketRent * Plugin.REAL_ESTATE_MAX_RENT_MULTIPLIER;
        float optimalRent = Mathf.Clamp(
                marketRent * Plugin.REAL_ESTATE_OPTIMAL_RENT_MULTIPLIER,
                minRent,
                maxRent
            );

        /*
         * RealEstateSettings slider mapping:
         *
         * slider = 0 -> max rent
         * slider = 1 -> min rent
         *
         * value = 1 - ((max - current) / range)
         */
        float sliderValue = 1f - ((maxRent - optimalRent) / (maxRent - minRent));
        rentSlider.value = sliderValue;
    }
}