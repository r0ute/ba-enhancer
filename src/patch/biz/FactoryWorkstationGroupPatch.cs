using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using HarmonyLib;
using UI.Smartphone.Apps.BizMan.Factory.Table;
using UnityEngine;

namespace BA.src.patch.biz;

[HarmonyPatch]
internal class FactoryWorkstationGroupPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BizManFactoryWorkstationGroupModel), MethodType.Constructor,
            typeof(int),
            typeof(BizManFactoryWorkstationGroupScrollerController),
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(List<BizManFactoryWorkstationGroupModelIngredient>))]
    static void OnBizManFactoryWorkstationGroupModelConstructor(
            int index,
            BizManFactoryWorkstationGroupScrollerController scroller,
            ref int inStock)
    {
        var groupedWorkstations = Traverse.Create(scroller)
            .Field("_groupedWorkstations")
            .GetValue<Dictionary<string, List<FactoryWorkstationInstance>>>();

        var workstationGroup = groupedWorkstations
            .ElementAt(index)
            .Value;

        if (workstationGroup == null || workstationGroup.Count == 0)
            return;

        var recipe = workstationGroup[0].SelectedRecipe;

        if (recipe == null)
            return;

        string itemName = recipe.output.item;

        var buildings = SaveGameManager.Current.BuildingRegistrations;

        float weeklyDemand = buildings
            .Where((BuildingRegistration buildingRegistration) =>
                buildingRegistration.RentedByPlayer && buildingRegistration.HasEstablishedBusiness)
            .Sum((BuildingRegistration buildingRegistration) =>
            {
                float weeklyHours = buildingRegistration.scheduleDays
                    .SelectMany((ScheduleDay day) => day.openingHourSlots)
                    .Sum((OpeningHourSlot slot) => slot.GetDurationInHours);

                return buildingRegistration.itemInstances.Values
                    .Where((ItemInstance itemInstance) =>
                        (itemInstance.ItemCached.type & ItemType.ShowcaseShelf) != 0)
                    .Where((ItemInstance itemInstance) =>
                    {
                        var stock = itemInstance.GetStockInstance();
                        return stock != null && stock.itemName == itemName;
                    })
                    .Sum((ItemInstance itemInstance) =>
                        itemInstance.ItemCached.addedCustomersPerHour * weeklyHours);
            });


        const float factoryHoursPerWeek = 24 * 7f;

        float weeklyProductionPerWorkstation =
            recipe.output.amount * factoryHoursPerWeek;

        int requiredWorkstations = weeklyProductionPerWorkstation > 0
            ? Mathf.CeilToInt(weeklyDemand / weeklyProductionPerWorkstation)
            : 0;

        int existingWorkstations = workstationGroup.Count;

        Plugin.Logger.LogDebug(
            $"OnBizManFactoryWorkstationGroupModelConstructor: Factory Analysis: " +
            $"item={itemName}, " +
            $"weeklyDemand={weeklyDemand:F0}, " +
            $"productionPerHour={recipe.output.amount:F0}, " +
            $"weeklyProductionPerWorkstation={weeklyProductionPerWorkstation:F0}, " +
            $"existingWorkstations={existingWorkstations}, " +
            $"requiredWorkstations={requiredWorkstations}");

        inStock = existingWorkstations - requiredWorkstations;
    }
}