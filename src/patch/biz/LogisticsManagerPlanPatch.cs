using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using BigAmbitions.Tags;
using Entities;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.BizMan.LogisticsManagers;

namespace BA.src.patch.biz;

internal class LogisticsManagerPlanPatch
{
    [HarmonyPatch(typeof(LogisticsManagerPlanUI), "LoadProducts")]
    [HarmonyPrefix]
    static void OnLogisticsManagerPlanUILoadProducts(LogisticsManagerPlanDestination planDestination)
    {
        var buildingRegistration = BuildingHelper.GetBuildingRegistration(planDestination.deliveryTargetAddress);

        List<string> validBuildingTypes = [
            "ba:buildingtype_retail",
                "ba:buildingtype_cinema",
                "ba:buildingtype_theater"
        ];

        if (!validBuildingTypes.Contains(buildingRegistration?.GetBuildingType()))
        {
            return;
        }

        var dailyHours = buildingRegistration.scheduleDays
            .FirstOrDefault()?
            .openingHourSlots
            .Sum(slot => slot.GetDurationInHours) ?? 0;
        Plugin.Logger.LogDebug($"OnLogisticsManagerPlanUILoadProducts: deliveryTargetAddress={planDestination.deliveryTargetAddress}, "
            + $"customerCapacity={buildingRegistration.customerCapacity}, "
            + $"dailyHours={dailyHours}, "
            + $"stockTargetsCount={planDestination.stockTargets.Count}");

        var itemCapacity = buildingRegistration.itemInstances.Values
            .Where(itemInstance => (itemInstance.ItemCached.type & ItemType.ShowcaseShelf) != 0)
            .Select(itemInstance => new
            {
                ItemInstance = itemInstance,
                StockInstance = itemInstance.GetStockInstance()
            })
            .Where(x => !string.IsNullOrEmpty(x.StockInstance.itemName)
                && (ItemsGetter.GetByName(x.StockInstance.itemName).type & ItemType.ServiceProduct) == 0)
            .GroupBy(x => x.StockInstance.itemName)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(x => x.ItemInstance.ItemCached.addedCustomersPerHour
                    * (dailyHours + 1)
                    * Plugin.BIZ_DELIVERY_CUSTOM_MULTIPLIERS.GetValueOrDefault(group.Key, 1)));

        itemCapacity.ToList()
            .ForEach(entry =>
            {
                Plugin.Logger.LogDebug($"OnLogisticsManagerPlanUILoadProducts: address={buildingRegistration.GetDisplayName()}, "
                    + $"itemName={entry.Key}, "
                    + $"deliveryAmount={entry.Value}");
            });

        if (BusinessTypeHelper.GetData(buildingRegistration).HasTag(TagRef.Businesstag.customersneedpaperbags))
        {
            var pointOfSaleItems = buildingRegistration.itemInstances.Values
                .Where(itemInstance => (itemInstance.ItemCached.type & ItemType.PointOfSale) != 0)
                .Select(itemInstance => new
                {
                    ItemInstance = itemInstance,
                    StockInstance = itemInstance.GetStockInstance()
                })
                .ToList();

            var pointOfSalePaperBagAmount = pointOfSaleItems.Sum(x => x.StockInstance.GetMaxStockCapacity(x.ItemInstance));
            var firstPointOfSaleItem = pointOfSaleItems.FirstOrDefault();

            if (firstPointOfSaleItem != null)
            {
                itemCapacity[firstPointOfSaleItem.StockInstance.itemName] =
                    Math.Max(itemCapacity.Values.Sum(), pointOfSalePaperBagAmount);
            }
        }

        planDestination.stockTargets.Clear();
        itemCapacity
            .Select(entry => new ItemAmountTarget(entry.Key, entry.Value))
            .ToList()
            .ForEach(planDestination.stockTargets.Add);
    }

}