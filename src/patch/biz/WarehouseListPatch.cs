using System;
using System.Linq;
using Entities;
using Extensions;
using HarmonyLib;
using UI.Smartphone.Apps.BizMan;
using UnityEngine;

namespace BA.src.patch.biz;

[HarmonyPatch]
internal class WarehouseListPatch
{
    [HarmonyPatch(typeof(WarehouseList), nameof(WarehouseList.Load))]
    [HarmonyPrefix]
    static bool OnWarehouseListLoad(WarehouseList __instance)
    {
        Plugin.Logger.LogDebug($"OnWarehouseListLoad: buildingRegistrationCount={SaveGameManager.Current.BuildingRegistrations.Count}");
        Traverse.Create(__instance).Field("warehouseEntry").GetValue<Transform>().ResetTemplate();
        SaveGameManager.Current.BuildingRegistrations
            .OrderByDescending(x => x.businessTypeName)
            .ThenBy(x => x.GetDisplayName())
            .Where(x => x.RentedByPlayer && x.GetBuildingType() == "ba:buildingtype_warehouse")
            .ToList()
            .ForEach(item => SetUpEntry(__instance, (Warehouse)item));

        return false;
    }


    [HarmonyReversePatch]
    [HarmonyPatch(typeof(WarehouseList), "SetUpEntry")]
    static void SetUpEntry(object instance, Warehouse warehouse) => throw new NotImplementedException();
}