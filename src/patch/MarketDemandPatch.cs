using HarmonyLib;
using TMPro;
using UI.Smartphone.Apps.MarketInsider;

namespace BA.src.patch;

[HarmonyPatch]
internal class MarketDemandPatch
{
    [HarmonyPatch(typeof(MarketDemandCellView), "SetData")]
    [HarmonyPostfix]
    static void OnMarketDemandCellViewSetData(MarketDemandCellView __instance, MarketDemandCellView.DemandModel data)
    {
        if (data == null || string.IsNullOrEmpty(data.ItemName))
        {
            return;
        }

        var neighborhood = Traverse.Create(__instance.GetComponentInParent<MarketDemandScrollerController>())
            .Field("_selectedNeighbourhood").GetValue<string>();

        if (string.IsNullOrEmpty(neighborhood) || !ItemHelper.HasPlayerMonopoly(data.ItemName, neighborhood))
        {
            return;
        }

        var lowestMarketPrice = Traverse.Create(__instance).Field("lowestMarketPrice").GetValue<TextMeshProUGUI>();

        if (lowestMarketPrice != null)
        {
            lowestMarketPrice.text += $" {Plugin.BIZ_PLAYER_MONOPOLY_INDICATOR}";
        }
    }
}