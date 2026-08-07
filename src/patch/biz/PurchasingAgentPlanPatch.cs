using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UI.Smartphone.Apps.BizMan.PurchasingAgent;

namespace BA.src.patch.biz;

internal class PurchasingAgentPlanPatch
{
    [HarmonyPatch(typeof(PurchasingAgentPlanUI), "StartOrder")]
    [HarmonyPostfix]
    static void OnPurchasingAgentPlanUIStartOrder(ref PurchasingAgentPlanUI __instance)
    {
        var warehouses = __instance.productsScrollerController.data
            .Where(product => product.isTarget && product?.productRef?.assignedWarehouse != null)
            .Select(product => product.warehouses
                .FirstOrDefault(warehouse => warehouse.Address == product.productRef.assignedWarehouse))
            .Where(warehouse => warehouse != null)
            .Distinct()
            .ToList();

        if (warehouses.Count == 0)
        {
            Plugin.Logger.LogWarning($"OnPurchasingAgentPlanUIStartOrder: no assigned warehouses");
            return;
        }
        else
        {
            Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: warehousesCount={warehouses.Count}");
        }

        var itemsToPurchase = new Dictionary<string, int>();

        warehouses.ForEach(warehouse =>
        {
            Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: assignedWarehouse={warehouse.Address}");
            Dictionary<string, int> otherItemsToPurchase;

            if (warehouse.businessTypeName == "ba:businesstype_factory")
            {
                otherItemsToPurchase = handleFactoryPurchases(warehouse);
            }
            else if (warehouse.businessTypeName == "ba:businesstype_warehouse")
            {
                Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: {warehouse.businessTypeName}");
                return; // todo: handle it
            }
            else
            {
                return;
            }

            otherItemsToPurchase.ToList()
                .ForEach(item =>
                    itemsToPurchase[item.Key] = itemsToPurchase.GetValueOrDefault(item.Key) + item.Value);
        });

        if (itemsToPurchase.Count == 0)
        {
            return;
        }

        __instance.productsScrollerController.data.ForEach(productModel =>
            {
                productModel.UpdateAmount(itemsToPurchase.GetValueOrDefault(productModel.productRef.itemName));
                Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: item={productModel.productRef.itemName},"
                    + $"amount={productModel.productRef.amount}");
            });
        __instance.productsScrollerController.scroller.ReloadData(0f);
    }

    internal static Dictionary<string, int> handleFactoryPurchases(BuildingRegistration buildingRegistration)
    {
        Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: {buildingRegistration.businessTypeName}");
        var recipeItems = new Dictionary<string, int>();

        buildingRegistration.itemInstances.Values
            .OfType<FactoryWorkstationInstance>()
            .Where(instance => !string.IsNullOrEmpty(instance.selectedRecipeId))
            .ToList()
            .ForEach(instance =>
            {
                Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: selectedRecipe={instance.SelectedRecipe}");

                instance.SelectedRecipe.ingredients
                    .ToList()
                    .ForEach(ingredient =>
                        recipeItems[ingredient.item] = recipeItems.GetValueOrDefault(ingredient.item) + ingredient.amount);
            });

        var weeklyHours = buildingRegistration.scheduleDays
            .SelectMany(day => day.openingHourSlots)
            .Sum(slot => slot.GetDurationInHours);

        Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: weeklyHours={weeklyHours}");

        recipeItems.Keys
            .ToList()
            .ForEach(key =>
            {
                Plugin.Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: recipeItem={key}, total={recipeItems[key]}");
                recipeItems[key] *= weeklyHours;
            });

        return recipeItems;
    }
}