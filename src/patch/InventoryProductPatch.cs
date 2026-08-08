using System.Globalization;
using BigAmbitions.Tags;
using Controllers;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BA.src.patch;

[HarmonyPatch]
internal class InventoryProductPatch
{

    [HarmonyPatch(typeof(ItemHelper), nameof(ItemHelper.GetLowestMarketPrice))]
    [HarmonyPrefix]
    static void OnItemHelperGetLowestMarketPrice(ref bool __runOriginal, ref float __result, string itemName, string neighborhood, bool forceUpdate = false)
    {
        __runOriginal = false;


        if (!forceUpdate && ItemHelper.LmpDictionary.TryGetValue((itemName, neighborhood), out var value))
        {
            __result = value;
            return;
        }

        if (forceUpdate && ItemHelper.LmpDictionary.ContainsKey((itemName, neighborhood)))
        {
            ItemHelper.LmpDictionary.Remove((itemName, neighborhood));
        }

        float lowestPrice = -1f;
        float lowestPriceExcludePlayer = lowestPrice;

        foreach (BuildingRegistration buildingRegistration in SaveGameManager.Current.BuildingRegistrations)
        {
            if (buildingRegistration.temporarilyClosed || buildingRegistration.retailPrices == null || buildingRegistration.Neighborhood != neighborhood || string.IsNullOrEmpty(buildingRegistration.BusinessName))
            {
                continue;
            }

            foreach (RetailPrice retailPrice in buildingRegistration.retailPrices)
            {
                if (!(retailPrice.itemName != itemName) && (buildingRegistration.RentedByPlayer || !(PlayerItemPurchaser.GetShelfFillState(itemName, buildingRegistration) <= 0f)))
                {
                    if (lowestPrice < 0f || retailPrice.price < lowestPrice)
                    {
                        lowestPrice = retailPrice.price;
                    }

                    if (!(buildingRegistration.RentedByPlayer || buildingRegistration.BuildingOwnedByPlayer)
                        && (lowestPriceExcludePlayer < 0f || retailPrice.price < lowestPriceExcludePlayer))
                    {
                        lowestPriceExcludePlayer = retailPrice.price;
                    }

                    break;
                }
            }
        }

        if (lowestPrice < 0f)
        {
            lowestPrice = ItemHelper.CalculateOptimalPriceByNeighborhood(itemName, neighborhood);
        }

        if (lowestPriceExcludePlayer < 0f)
        {
            lowestPriceExcludePlayer = ItemHelper.CalculateOptimalPriceByNeighborhood(itemName, neighborhood);
        }

        ItemHelper.LmpDictionary.Add((itemName, neighborhood), lowestPrice);

        __result = forceUpdate ? lowestPriceExcludePlayer : lowestPrice;
        Plugin.Logger.LogDebug($"OnItemHelperGetLowestMarketPrice: neighborhood={neighborhood}, itemName={itemName}, lowestPrice={__result}, forceUpdate={forceUpdate}");

    }

    [HarmonyPatch(typeof(InventoryProductCellView), "UpdateAmountBackground")]
    [HarmonyPrefix]
    static bool OnInventoryProductCellViewUpdateAmountBackground(ref InventoryProductCellView __instance)
    {
        var amountBackground = Traverse.Create(__instance).Field("_amountBackground").GetValue<Image>();
        var data = Traverse.Create(__instance).Field("_data").GetValue<InventoryProductCellView.InventoryProductModel>();

        var lowestMarketPrice = ItemHelper.GetLowestMarketPrice(data.Item.itemName, data.Neighborhood, forceUpdate: true);
        var retailPrice = data.RetailPriceReference.price;

        if (retailPrice != 0f && Mathf.RoundToInt(retailPrice * 100) == Mathf.RoundToInt((lowestMarketPrice - 0.01f) * 100))
        {
            amountBackground.color = InstanceBehavior<GlobalReferences>.Instance.colors.green;

            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(InventoryProductCellView), "SetData")]
    [HarmonyPostfix]
    static void OnInventoryProductCellViewSetData(ref InventoryProductCellView __instance, InventoryProductCellView.InventoryProductModel data)
    {
        if (data == null || data.Item == null || data.Item.HasTag(TagRef.Itemtag.isbag))
        {
            return;
        }

        var lowestMarketPrice = ItemHelper.GetLowestMarketPrice(data.Item.itemName, data.Neighborhood, forceUpdate: true);
        var optimalPrice = Mathf.Max(0f, Mathf.Round((lowestMarketPrice - 0.01f) * 100f) / 100f);

        data.RetailPriceReference.price = optimalPrice;
        data.StoredRetailPriceReference.price = optimalPrice;

        var retailPrice = Traverse.Create(__instance).Field("retailPrice").GetValue<TMP_InputField>();
        retailPrice?.SetTextWithoutNotify(optimalPrice.ToString("F2", CultureInfo.CurrentCulture));
    }

}